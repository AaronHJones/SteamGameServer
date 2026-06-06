using System.Text.Json;
using System.Text.Json.Serialization;
using RustServerHealth.Models;

namespace RustServerHealth.Services;

public class GameServerRegistry
{
    private readonly string _filePath;
    private readonly ILogger<GameServerRegistry> _logger;
    private List<GameServer> _servers = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public event Action? OnChanged;

    public GameServerRegistry(ILogger<GameServerRegistry> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(AppContext.BaseDirectory, "servers.json");
    }

    public async Task LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_filePath))
            {
                _servers = [DefaultRustServer()];
                await SaveInternalAsync();
                _logger.LogInformation("Created default servers.json");
                return;
            }

            var json = await File.ReadAllTextAsync(_filePath);
            _servers = JsonSerializer.Deserialize<List<GameServer>>(json, JsonOpts) ?? [];
            _logger.LogInformation("Loaded {Count} server(s)", _servers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to load servers.json: {Msg}", ex.Message);
            _servers = [DefaultRustServer()];
        }
        finally { _lock.Release(); }
    }

    public List<GameServer> GetAll()
    {
        lock (_servers) return [.. _servers];
    }

    public GameServer? Get(string id)
    {
        lock (_servers) return _servers.FirstOrDefault(s => s.Id == id);
    }

    public async Task AddAsync(GameServer server)
    {
        await _lock.WaitAsync();
        try { _servers.Add(server); await SaveInternalAsync(); }
        finally { _lock.Release(); }
        OnChanged?.Invoke();
    }

    public async Task UpdateAsync(GameServer updated)
    {
        await _lock.WaitAsync();
        try
        {
            var idx = _servers.FindIndex(s => s.Id == updated.Id);
            if (idx >= 0) _servers[idx] = updated;
            await SaveInternalAsync();
        }
        finally { _lock.Release(); }
        OnChanged?.Invoke();
    }

    public async Task RemoveAsync(string id)
    {
        await _lock.WaitAsync();
        try { _servers.RemoveAll(s => s.Id == id); await SaveInternalAsync(); }
        finally { _lock.Release(); }
        OnChanged?.Invoke();
    }

    private async Task SaveInternalAsync()
    {
        var json = JsonSerializer.Serialize(_servers, JsonOpts);
        await File.WriteAllTextAsync(_filePath, json);
    }

    private static GameServer DefaultRustServer() => new()
    {
        Id = "rust-1",
        Name = "My Rust Server",
        GameType = GameType.Rust,
        ServiceName = "rust-server",
        IsUserService = false,
        Host = "localhost",
        GamePort = 28015,
        QueryPort = 28016,
        RconPort = 28017,
        RconPassword = "",
        MaxPlayers = 50,
        SteamAppId = 258550,
        InstallDir = "/home/youruser/gameservers/rust_dedicated",
        Enabled = true,
        PollIntervalSeconds = 10,
        LogBufferSize = 500
    };
}
