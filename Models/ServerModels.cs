using RustServerHealth.Services;

namespace RustServerHealth.Models;

/// <summary>Immutable snapshot of a server's state at a point in time.</summary>
public record ServerSnapshot(
    string ServerId,
    bool IsOnline,
    ServerInfo? Info,
    List<PlayerInfo> Players,
    bool RconConnected,
    ServiceState ServiceState,
    DateTime LastUpdated
);

public enum ServiceState { Unknown, Running, Stopped, Failed }

public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Message { get; init; } = "";
    public LogEntryLevel Level { get; init; }
}

public enum LogEntryLevel { Info, Warning, Error }
