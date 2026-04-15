using System.Diagnostics;
using System.Text;

namespace SuperTweaker.Core;

public class WingetHelper
{
    private readonly Logger? _log;

    public WingetHelper(Logger? log = null) => _log = log;

    public static bool IsWingetAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("winget", "--version")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            });
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>Installs a package silently. Calls back with output lines.</summary>
    public async Task<bool> InstallAsync(string wingetId, Action<string>? onOutput = null,
        CancellationToken ct = default)
    {
        _log?.Info($"Installing: {wingetId}");

        var args = $"install --id {wingetId} -e --silent " +
                   "--accept-package-agreements --accept-source-agreements";

        return await RunAsync("winget", args, onOutput, ct);
    }

    public async Task<bool> UninstallAsync(string wingetId, Action<string>? onOutput = null,
        CancellationToken ct = default)
    {
        _log?.Info($"Uninstalling: {wingetId}");
        return await RunAsync("winget", $"uninstall --id {wingetId} -e --silent", onOutput, ct);
    }

    public async Task<bool> UpgradeAllAsync(Action<string>? onOutput = null,
        CancellationToken ct = default)
    {
        _log?.Info("Upgrading all winget packages...");
        return await RunAsync("winget",
            "upgrade --all --silent --accept-package-agreements --accept-source-agreements",
            onOutput, ct);
    }

    private async Task<bool> RunAsync(string exe, string args,
        Action<string>? onOutput, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var p = new Process();
                p.StartInfo = new ProcessStartInfo(exe, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                p.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    onOutput?.Invoke(e.Data);
                    _log?.Info(e.Data);
                };
                p.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    _log?.Warn(e.Data);
                };

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                while (!p.WaitForExit(500))
                    if (ct.IsCancellationRequested) { p.Kill(); return false; }

                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _log?.Error($"winget error: {ex.Message}");
                return false;
            }
        }, ct);
    }
}
