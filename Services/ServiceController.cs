using System.Diagnostics;
using RustServerHealth.Models;

namespace RustServerHealth.Services;

/// <summary>
/// Wraps systemctl to start/stop/query game server services.
/// On Linux: uses "systemctl --user" (user services) or "sudo systemctl" (system services).
/// On Windows: returns Unknown/no-ops so the dev environment still builds and runs.
/// </summary>
public class ServiceController
{
    private readonly ILogger<ServiceController> _logger;

    public bool IsAvailable => OperatingSystem.IsLinux();

    public ServiceController(ILogger<ServiceController> logger) => _logger = logger;

    public async Task<bool> StartAsync(string serviceName, bool isUserService)
    {
        var (cmd, args) = BuildArgs("start", serviceName, isUserService);
        var (exit, _) = await RunAsync(cmd, args);
        return exit == 0;
    }

    public async Task<bool> StopAsync(string serviceName, bool isUserService)
    {
        var (cmd, args) = BuildArgs("stop", serviceName, isUserService);
        var (exit, _) = await RunAsync(cmd, args);
        return exit == 0;
    }

    public async Task<bool> RestartAsync(string serviceName, bool isUserService)
    {
        var (cmd, args) = BuildArgs("restart", serviceName, isUserService);
        var (exit, _) = await RunAsync(cmd, args);
        return exit == 0;
    }

    public async Task<ServiceState> GetStateAsync(string serviceName, bool isUserService)
    {
        if (!IsAvailable) return ServiceState.Unknown;
        var (cmd, args) = BuildArgs("is-active", serviceName, isUserService);
        var (exit, stdout) = await RunAsync(cmd, args);
        return stdout.Trim() switch
        {
            "active" => ServiceState.Running,
            "inactive" or "dead" => ServiceState.Stopped,
            "failed" => ServiceState.Failed,
            _ => ServiceState.Unknown
        };
    }

    /// <summary>
    /// Writes content to a .service file and enables it.
    /// Requires appropriate write permissions (user service path or sudo for system path).
    /// </summary>
    public async Task<(bool Ok, string Output)> InstallServiceAsync(
        string serviceName, string content, bool isUserService)
    {
        if (!IsAvailable)
            return (false, "Service installation only available on Linux.");

        string path;
        if (isUserService)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "systemd", "user");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, $"{serviceName}.service");
            await File.WriteAllTextAsync(path, content);
        }
        else
        {
            path = $"/etc/systemd/system/{serviceName}.service";
            var (we, wo) = await RunAsync("sudo", $"tee {path}",
                stdin: content);
            if (we != 0) return (false, wo);
        }

        var (re, ro) = isUserService
            ? await RunAsync("systemctl", $"--user daemon-reload && systemctl --user enable {serviceName}")
            : await RunAsync("sudo", $"systemctl daemon-reload && sudo systemctl enable {serviceName}");

        return (re == 0, ro);
    }

    private static (string cmd, string args) BuildArgs(
        string action, string serviceName, bool isUserService) =>
        isUserService
            ? ("systemctl", $"--user {action} {serviceName}")
            : ("sudo", $"systemctl {action} {serviceName}");

    public async Task<(int ExitCode, string Output)> RunAsync(
        string cmd, string args, string? stdin = null)
    {
        if (!IsAvailable)
            return (0, $"[Windows dev mode — would run: {cmd} {args}]");

        try
        {
            var psi = new ProcessStartInfo(cmd, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)!;

            if (stdin is not null)
            {
                await proc.StandardInput.WriteAsync(stdin);
                proc.StandardInput.Close();
            }

            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, stdout + stderr);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Process failed: {Cmd} {Args} — {Msg}", cmd, args, ex.Message);
            return (-1, ex.Message);
        }
    }
}
