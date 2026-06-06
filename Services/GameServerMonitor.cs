using RustServerHealth.Models;

namespace RustServerHealth.Services;

/// <summary>
/// Monitors a single game server via A2S query + WebSocket RCON.
/// Managed by GameServerManager — not a BackgroundService itself.
/// </summary>
public class GameServerMonitor : IAsyncDisposable
{
    private GameServer _server;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<GameServerMonitor> _logger;
    private readonly ServiceController _svc;

    private SteamQueryService? _query;
    private RconService? _rcon;
    private CancellationTokenSource _cts = new();
    private Task? _pollTask;

    private volatile ServerSnapshot _snapshot;
    private readonly CircularBuffer<LogEntry> _logBuffer;
    private readonly object _historyLock = new();
    private readonly List<(int Count, DateTime Time)> _playerHistory = [];

    public string ServerId => _server.Id;
    public event Action? OnUpdate;

    public GameServerMonitor(GameServer server, ServiceController svc, ILoggerFactory loggerFactory)
    {
        _server = server;
        _svc = svc;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<GameServerMonitor>();
        _logBuffer = new CircularBuffer<LogEntry>(server.LogBufferSize);
        _snapshot = EmptySnapshot();
    }

    public ServerSnapshot GetSnapshot() => _snapshot;
    public List<LogEntry> GetLogs() => _logBuffer.ToList();
    public List<(int Count, DateTime Time)> GetPlayerHistory()
    {
        lock (_historyLock) return [.. _playerHistory];
    }
    public GameServer Server => _server;

    public void Start()
    {
        BuildServices();
        _cts = new CancellationTokenSource();
        _pollTask = RunPollLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_pollTask is not null)
            try { await _pollTask; } catch { }
        if (_rcon is not null) await _rcon.DisposeAsync();
    }

    /// <summary>Hot-apply new server settings (e.g. changed password).</summary>
    public async Task ApplySettingsAsync(GameServer updated)
    {
        _server = updated;
        if (_rcon is not null)
        {
            _rcon.OnConsoleMessage -= OnRconConsole;
            await _rcon.DisposeAsync();
        }
        BuildServices();
        await _rcon!.ConnectAsync();
        OnUpdate?.Invoke();
    }

    private void BuildServices()
    {
        _query = new SteamQueryService(
            _server.Host, _server.QueryPort,
            _loggerFactory.CreateLogger<SteamQueryService>());

        _rcon = new RconService(
            _server.Host, _server.RconPort, _server.RconPassword,
            _loggerFactory.CreateLogger<RconService>());

        _rcon.OnConsoleMessage += OnRconConsole;
    }

    private async Task RunPollLoopAsync(CancellationToken ct)
    {
        // Stagger startup so all servers don't poll simultaneously
        await Task.Delay(Random.Shared.Next(0, 3000), ct);

        while (!ct.IsCancellationRequested)
        {
            await PollAsync();
            try { await Task.Delay(TimeSpan.FromSeconds(_server.PollIntervalSeconds), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollAsync()
    {
        var info = await _query!.GetServerInfoAsync();
        var players = info is not null ? await _query.GetPlayersAsync() : [];

        if (!_rcon!.IsConnected) await _rcon.ConnectAsync();

        var svcState = ServiceState.Unknown;
        if (!string.IsNullOrWhiteSpace(_server.ServiceName))
            svcState = await _svc.GetStateAsync(_server.ServiceName, _server.IsUserService);

        _snapshot = new ServerSnapshot(
            ServerId: _server.Id,
            IsOnline: info is not null,
            Info: info,
            Players: players,
            RconConnected: _rcon.IsConnected,
            ServiceState: svcState,
            LastUpdated: DateTime.Now);

        lock (_historyLock)
        {
            _playerHistory.Add((players.Count, DateTime.Now));
            if (_playerHistory.Count > 60) _playerHistory.RemoveAt(0);
        }

        OnUpdate?.Invoke();
    }

    private void OnRconConsole(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _logBuffer.Add(new LogEntry
        {
            Timestamp = DateTime.Now,
            Message = message.Trim(),
            Level = ClassifyLog(message)
        });
        OnUpdate?.Invoke();
    }

    private static LogEntryLevel ClassifyLog(string msg)
    {
        if (msg.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
            return LogEntryLevel.Error;
        if (msg.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("WARN", StringComparison.OrdinalIgnoreCase))
            return LogEntryLevel.Warning;
        return LogEntryLevel.Info;
    }

    private ServerSnapshot EmptySnapshot() => new(
        _server.Id, false, null, [], false, ServiceState.Unknown, DateTime.MinValue);

    public async ValueTask DisposeAsync() => await StopAsync();
}

/// <summary>Thread-safe circular ring buffer.</summary>
public sealed class CircularBuffer<T>
{
    private readonly T?[] _buf;
    private int _head, _count;
    private readonly object _lock = new();
    public CircularBuffer(int capacity) => _buf = new T[capacity];

    public void Add(T item)
    {
        lock (_lock)
        {
            _buf[_head % _buf.Length] = item;
            _head++;
            if (_count < _buf.Length) _count++;
        }
    }

    public List<T> ToList()
    {
        lock (_lock)
        {
            var r = new List<T>(_count);
            if (_count < _buf.Length)
                for (int i = 0; i < _count; i++) r.Add(_buf[i]!);
            else
            {
                var s = _head % _buf.Length;
                for (int i = 0; i < _buf.Length; i++) r.Add(_buf[(s + i) % _buf.Length]!);
            }
            return r;
        }
    }
}
