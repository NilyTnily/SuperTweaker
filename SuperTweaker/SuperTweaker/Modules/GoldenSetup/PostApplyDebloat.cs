using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SuperTweaker.Core;

namespace SuperTweaker.Modules.GoldenSetup;

/// <summary>
/// After Golden Setup tweaks, optionally runs <b>Sophia Script</b> in <b>performance mode</b>
/// (<c>-Functions</c> telemetry/minimal diagnostics/high power plan) so the full preset and <c>PostActions</c>
/// are not executed — avoids interactive UWP uninstall dialogs and keeps behavior predictable.
/// Does not schedule a Windows restart (Hellzerg handles the one allowed reboot afterward).
/// </summary>
public static class PostApplyDebloat
{
    public const string WingetSophiaId = "TeamSophia.SophiaScript";

    /// <summary>
    /// Sophia functions applied in performance mode (same names on Win10/Win11 current Sophia builds).
    /// </summary>
    /// <summary>Passed to Sophia.ps1 <c>-Functions</c> (performance-oriented; no full preset / PostActions).</summary>
    private const string SophiaPerformanceFunctionsArray =
        "@('DiagTrackService -Disable','DiagnosticDataLevel -Minimal','PowerPlan -High')";

    public static async Task RunAsync(
        Logger log,
        WindowsInfo os,
        WingetHelper winget,
        bool enabled,
        CancellationToken ct)
    {
        if (!enabled)
        {
            log.Info("Post-apply Sophia (performance mode) skipped (unchecked).");
            return;
        }

        if (!os.IsWindows10 && !os.IsWindows11)
        {
            log.Warn("Post-apply Sophia is only for Windows 10 or 11.");
            return;
        }

        if (ct.IsCancellationRequested) return;

        var label = os.IsWindows11
            ? "Sophia Script (Windows 11) — performance mode"
            : "Sophia Script (Windows 10) — performance mode";

        log.Info($"=== Post-apply: {label} ===");

        string? scriptPath = await Task.Run(() => FindSophiaScript(log, os.IsWindows11), ct);
        if (scriptPath == null && WingetHelper.IsWingetAvailable())
        {
            log.Info($"Installing {WingetSophiaId} via winget (if missing)...");
            await winget.InstallAsync(WingetSophiaId, line => log.Info($"  winget: {line}"), ct);
            if (ct.IsCancellationRequested) return;
            scriptPath = await Task.Run(() => FindSophiaScript(log, os.IsWindows11), ct);
        }

        if (string.IsNullOrEmpty(scriptPath))
        {
            log.Error(
                "Could not locate Sophia.ps1 for your OS after install. " +
                "Install manually: winget install TeamSophia.SophiaScript");
            return;
        }

        var workDir = Path.GetDirectoryName(scriptPath);
        if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
        {
            log.Error("Sophia working directory is invalid.");
            return;
        }

        // -Functions: targeted performance/debloat-related calls; avoids full preset + PostActions (no scripted reboot).
        var args =
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command " +
            $"\"& '{EscapePsSingle(scriptPath)}' -Functions {SophiaPerformanceFunctionsArray}\"";

        var psi = CreatePowerShellStartInfo(args, workDir);
        await RunDebloatProcessAsync(log, psi, label, ct);
    }

    private static string EscapePsSingle(string path) => path.Replace("'", "''");

    private static ProcessStartInfo CreatePowerShellStartInfo(string arguments, string? workingDirectory)
    {
        var admin = IsCurrentProcessAdmin();
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        if (!admin)
        {
            psi.UseShellExecute = true;
            psi.CreateNoWindow = false;
            psi.RedirectStandardOutput = false;
            psi.RedirectStandardError = false;
            psi.Verb = "runas";
        }

        return psi;
    }

    private static Task RunDebloatProcessAsync(Logger log, ProcessStartInfo psi, string label, CancellationToken ct)
    {
        var admin = IsCurrentProcessAdmin();
        try
        {
            log.Info(admin
                ? $"Launching {label} (running as Administrator)..."
                : $"Launching {label} — approve the UAC prompt if shown...");

            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (admin && psi.RedirectStandardOutput)
            {
                p.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    log.Info($"  sophia: {e.Data}");
                };
                p.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    log.Warn($"  sophia: {e.Data}");
                };
            }

            if (!p.Start())
            {
                log.Error($"Could not start {label} process.");
                return Task.CompletedTask;
            }

            if (admin && psi.RedirectStandardOutput)
            {
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }

            const int maxWaitMs = 45 * 60 * 1000;
            var start = Environment.TickCount64;
            while (!p.WaitForExit(500))
            {
                if (ct.IsCancellationRequested)
                {
                    try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    log.Warn("Post-apply Sophia cancelled.");
                    return Task.CompletedTask;
                }

                if (Environment.TickCount64 - start > maxWaitMs)
                {
                    try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    log.Error("Post-apply Sophia exceeded time limit and was stopped.");
                    return Task.CompletedTask;
                }
            }

            if (p.ExitCode == 0)
                log.Success($"{label} finished successfully.");
            else
                log.Warn($"{label} exited with code {p.ExitCode}. If -Functions is unsupported in your Sophia build, update TeamSophia.SophiaScript.");
        }
        catch (Exception ex)
        {
            log.Error($"Post-apply Sophia failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static bool IsCurrentProcessAdmin()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Finds Sophia.ps1 for Windows 10 or 11 under the TeamSophia winget package.</summary>
    private static string? FindSophiaScript(Logger log, bool windows11)
    {
        try
        {
            var packagesRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Packages");

            if (!Directory.Exists(packagesRoot))
                return null;

            var marker = windows11 ? "Windows_11" : "Windows_10";

            foreach (var pattern in new[] { "*Sophia*", "*TeamSophia*" })
            {
                foreach (var dir in Directory.EnumerateDirectories(packagesRoot, pattern, SearchOption.TopDirectoryOnly))
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "Sophia.ps1", SearchOption.AllDirectories))
                    {
                        if (file.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            file.IndexOf($"Sophia_Script_for_{marker}", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            log.Info($"Found Sophia.ps1 ({marker}): {file}");
                            return file;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Warn($"Sophia script search: {ex.Message}");
        }

        return null;
    }
}
