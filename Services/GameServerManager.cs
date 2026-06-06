using RustServerHealth.Models;

namespace RustServerHealth.Services;

/// <summary>
/// Singleton BackgroundService that owns all GameServerMonitor instances.
/// Registers with GameServerRegistry.OnChanged to hot-add/remove servers.
/// </summary>
public class GameServerManager : BackgroundService
{
    private readonly GameServerRegistry _registry;
    private readonly ServiceController _svc;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<GameServerManager> _logger;

    private readonly Dictionary<string, GameServerMonitor> _monitors = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event Action? OnAnyUpdate;

    public GameServerManager(
        GameServerRegistry registry,
        ServiceController svc,
        ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _svc = svc;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<GameServerManager>();
    }

    public GameServerMonitor? GetMonitor(string serverId)
    {
        lock (_monitors)
            return _monitors.TryGetValue(serverId, out var m) ? m : null;
    }

    public List<GameServerMonitor> GetAll()
    {
        lock (_monitors) return [.. _monitors.Values];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _registry.LoadAsync();
        _registry.OnChanged += OnRegistryChanged;

        foreach (var server in _registry.GetAll().Where(s => s.Enabled))
            await AddMonitorAsync(server);

        // Keep running until host shuts down
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }

        // Cleanup
        _registry.OnChanged -= OnRegistryChanged;
        foreach (var m in GetAll())
            await m.DisposeAsync();
    }

    private void OnRegistryChanged()
    {
        _ = Task.Run(SyncMonitorsAsync);
    }

    private async Task SyncMonitorsAsync()
    {
        var current = _registry.GetAll();

        // Add missing monitors
        foreach (var server in current.Where(s => s.Enabled))
        {
            bool exists;
            lock (_monitors) exists = _monitors.ContainsKey(server.Id);
            if (!exists) await AddMonitorAsync(server);
        }

        // Remove monitors for deleted/disabled servers
        List<string> toRemove;
        lock (_monitors)
        {
            var activeIds = current.Where(s => s.Enabled).Select(s => s.Id).ToHashSet();
            toRemove = _monitors.Keys.Where(id => !activeIds.Contains(id)).ToList();
        }

        foreach (var id in toRemove)
            await RemoveMonitorAsync(id);
    }

    private async Task AddMonitorAsync(GameServer server)
    {
        await _lock.WaitAsync();
        try
        {
            if (_monitors.ContainsKey(server.Id)) return;
            var monitor = new GameServerMonitor(server, _svc, _loggerFactory);
            monitor.OnUpdate += () => OnAnyUpdate?.Invoke();
            _monitors[server.Id] = monitor;
            monitor.Start();
            _logger.LogInformation("Started monitor for [{Id}] {Name}", server.Id, server.Name);
        }
        finally { _lock.Release(); }
    }

    private async Task RemoveMonitorAsync(string id)
    {
        await _lock.WaitAsync();
        GameServerMonitor? monitor = null;
        try
        {
            _monitors.TryGetValue(id, out monitor);
            _monitors.Remove(id);
        }
        finally { _lock.Release(); }

        if (monitor is not null)
        {
            await monitor.DisposeAsync();
            _logger.LogInformation("Stopped monitor for [{Id}]", id);
        }
    }

    /// <summary>
    /// Apply updated settings to a running monitor (e.g. changed RCON password).
    /// </summary>
    public async Task ApplyServerSettingsAsync(GameServer updated)
    {
        await _registry.UpdateAsync(updated);

        GameServerMonitor? monitor;
        lock (_monitors) _monitors.TryGetValue(updated.Id, out monitor);
        if (monitor is not null)
            await monitor.ApplySettingsAsync(updated);
    }
}
