using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustServerHealth.Services;

public class RconMessage
{
    [JsonPropertyName("Identifier")] public int Identifier { get; set; }
    [JsonPropertyName("Message")] public string Message { get; set; } = "";
    [JsonPropertyName("Type")] public string Type { get; set; } = "";
    [JsonPropertyName("Stacktrace")] public string? Stacktrace { get; set; }
}

/// <summary>
/// Facepunch WebSocket RCON client for Rust dedicated servers (rcon.web 1).
/// Connects to ws://host:port/password
/// </summary>
public class RconService : IAsyncDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pending = new();
    private Task? _receiveTask;
    private int _idCounter = 1;

    private readonly string _host;
    private readonly int _port;
    private readonly string _password;
    private readonly ILogger<RconService> _logger;

    public event Action<string>? OnConsoleMessage;
    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public RconService(string host, int port, string password, ILogger<RconService> logger)
    {
        _host = host;
        _port = port;
        _password = password;
        _logger = logger;
    }

    public async Task<bool> ConnectAsync()
    {
        await _connectLock.WaitAsync();
        try
        {
            if (IsConnected) return true;

            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();

            _ws?.Dispose();
            _ws = new ClientWebSocket();

            // Facepunch RCON URL: ws://host:port/password  (empty password = trailing slash)
            var escapedPw = Uri.EscapeDataString(_password);
            var uri = new Uri($"ws://{_host}:{_port}/{escapedPw}");

            await _ws.ConnectAsync(uri, _cts.Token).WaitAsync(TimeSpan.FromSeconds(5));
            _receiveTask = Task.Run(ReceiveLoopAsync);
            _logger.LogInformation("RCON connected to {Host}:{Port}", _host, _port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("RCON connect failed: {Msg}", ex.Message);
            return false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task<string?> SendCommandAsync(string command, int timeoutMs = 8000)
    {
        if (!IsConnected && !await ConnectAsync())
            return null;

        var id = Interlocked.Increment(ref _idCounter);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var payload = JsonSerializer.Serialize(new RconMessage
        {
            Identifier = id,
            Message = command,
            Type = "Request"
        });

        try
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            await _ws!.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
            return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);
            _logger.LogDebug("RCON send failed: {Msg}", ex.Message);
            return null;
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[65536];
        try
        {
            while (_ws?.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                ProcessMessage(sb.ToString());
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug("RCON receive loop ended: {Msg}", ex.Message);
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<RconMessage>(json);
            if (msg is null) return;

            if (msg.Identifier > 0 && _pending.TryRemove(msg.Identifier, out var tcs))
                tcs.TrySetResult(msg.Message);
            else
                OnConsoleMessage?.Invoke(msg.Message); // Identifier <= 0 = broadcast console
        }
        catch (JsonException)
        {
            OnConsoleMessage?.Invoke(json);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_receiveTask is not null)
            try { await _receiveTask; } catch { }
        _ws?.Dispose();
        _cts.Dispose();
    }
}
