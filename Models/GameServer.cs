namespace RustServerHealth.Models;

public enum GameType { Rust, PalWorld, SevenDaysToDie, Generic }

public class GameServer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public GameType GameType { get; set; } = GameType.Rust;

    // systemd service name (e.g. "rust-server")
    public string ServiceName { get; set; } = "";
    // true = systemctl --user (user service), false = sudo systemctl (system service)
    public bool IsUserService { get; set; } = false;

    public string Host { get; set; } = "localhost";
    public int GamePort { get; set; } = 28015;
    public int QueryPort { get; set; } = 28016;
    public int RconPort { get; set; } = 28017;
    public string RconPassword { get; set; } = "";
    public int MaxPlayers { get; set; } = 50;
    public int SteamAppId { get; set; } = 258550;
    public string InstallDir { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 10;
    public int LogBufferSize { get; set; } = 500;

    public static readonly Dictionary<GameType, GameMeta> KnownGames = new()
    {
        [GameType.Rust]           = new(258550,  "Rust",             "mdi-pickaxe",    28015, 28016, 28017),
        [GameType.PalWorld]       = new(2394010, "Palworld",         "mdi-dragon",     8211,  27015, 25575),
        [GameType.SevenDaysToDie] = new(294420,  "7 Days to Die",    "mdi-skull",      26900, 26901, 8081),
        [GameType.Generic]        = new(0,        "Generic",          "mdi-server",     0,     0,     0),
    };
}

public record GameMeta(int AppId, string DisplayName, string Icon, int GamePort, int QueryPort, int RconPort);
