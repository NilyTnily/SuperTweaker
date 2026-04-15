using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SuperTweaker.Core;

namespace SuperTweaker.Modules.GoldenSetup;

/// <summary>
/// After Sophia, optionally runs <b>Hellzerg Optimizer</b> with a JSON template focused on
/// hardware/latency tweaks (HPET, performance tweaks, svchost splitting). This is the <b>only</b>
/// step that requests a <b>system restart</b> (<see cref="PostAction"/>), so the PC does not reboot after Sophia.
/// </summary>
public static class PostApplyHellzerg
{
    public const string WingetHellzergId = "Hellzerg.Optimizer";

    public static async Task RunAsync(
        Logger log,
        WindowsInfo os,
        WingetHelper winget,
        bool enabled,
        CancellationToken ct)
    {
        if (!enabled)
        {
            log.Info("Hellzerg Optimizer skipped (unchecked).");
            return;
        }

        if (!os.IsWindows10 && !os.IsWindows11)
        {
            log.Warn("Hellzerg Optimizer supports Windows 10/11 only.");
            return;
        }

        if (ct.IsCancellationRequested) return;

        log.Info("=== Post-apply: Hellzerg Optimizer (hardware / latency; restart when done) ===");

        string? exePath = await Task.Run(() => FindOptimizerExe(log), ct);
        if (exePath == null && WingetHelper.IsWingetAvailable())
        {
            log.Info($"Installing {WingetHellzergId} via winget (if missing)...");
            await winget.InstallAsync(WingetHellzergId, line => log.Info($"  winget: {line}"), ct);
            if (ct.IsCancellationRequested) return;
            exePath = await Task.Run(() => FindOptimizerExe(log), ct);
        }

        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            log.Error(
                "Could not find Optimizer.exe. Install manually if needed: winget install Hellzerg.Optimizer " +
                "(or download from github.com/hellzerg/optimizer/releases).");
            return;
        }

        var baseName = os.IsWindows11 ? "hellzerg-base-win11.json" : "hellzerg-base-win10.json";
        var basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "optimizer", baseName);
        if (!File.Exists(basePath))
        {
            log.Error($"Missing template: {basePath}");
            return;
        }

        string tempJson;
        try
        {
            if (!HellzergTemplateBuilder.TryBuildPatchedHellzergTemplate(basePath, os, out var patched, out var err) ||
                patched == null)
            {
                log.Error($"Failed to build Hellzerg template: {err}");
                return;
            }

            tempJson = Path.Combine(Path.GetTempPath(), $"st_hellzerg_{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(tempJson, patched, Encoding.UTF8, ct);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to build Hellzerg template: {ex.Message}");
            return;
        }

        log.Info($"Hellzerg template: {tempJson}");
        log.Info("A normal restart will be scheduled after Optimizer finishes (PostAction.Restart).");

        var psi = new ProcessStartInfo
        {
            FileName               = exePath,
            Arguments              = $"/config=\"{tempJson}\"",
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
            WorkingDirectory       = Path.GetDirectoryName(exePath) ?? ""
        };

        var admin = IsCurrentProcessAdmin();
        if (!admin)
        {
            psi.UseShellExecute = true;
            psi.CreateNoWindow = false;
            psi.RedirectStandardOutput = false;
            psi.RedirectStandardError = false;
            psi.Verb = "runas";
        }

        try
        {
            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (admin && psi.RedirectStandardOutput)
            {
                p.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    log.Info($"  optimizer: {e.Data}");
                };
                p.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    log.Warn($"  optimizer: {e.Data}");
                };
            }

            if (!p.Start())
            {
                log.Error("Could not start Hellzerg Optimizer.");
                return;
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
                    log.Warn("Hellzerg Optimizer cancelled.");
                    TryDeleteQuiet(tempJson);
                    return;
                }

                if (Environment.TickCount64 - start > maxWaitMs)
                {
                    try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    log.Error("Hellzerg Optimizer exceeded time limit.");
                    TryDeleteQuiet(tempJson);
                    return;
                }
            }

            if (p.ExitCode == 0)
                log.Success("Hellzerg Optimizer finished. If a restart was requested, Windows should reboot shortly.");
            else
                log.Warn($"Hellzerg Optimizer exited with code {p.ExitCode}.");

            TryDeleteQuiet(tempJson);
        }
        catch (Exception ex)
        {
            log.Error($"Hellzerg Optimizer failed: {ex.Message}");
        }

    }

    private static void TryDeleteQuiet(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
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

    private static string? FindOptimizerExe(Logger log)
    {
        try
        {
            var packagesRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Packages");

            if (Directory.Exists(packagesRoot))
            {
                foreach (var pattern in new[] { "*Hellzerg*", "*hellzerg*" })
                {
                    foreach (var dir in Directory.EnumerateDirectories(packagesRoot, pattern, SearchOption.TopDirectoryOnly))
                    {
                        var exe = Directory.EnumerateFiles(dir, "Optimizer.exe", SearchOption.AllDirectories)
                            .FirstOrDefault();
                        if (exe != null)
                        {
                            log.Info($"Found Optimizer.exe: {exe}");
                            return exe;
                        }
                    }
                }
            }

            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            foreach (var baseDir in new[] { Path.Combine(pf, "Optimizer"), Path.Combine(pf86, "Optimizer") })
            {
                var candidate = Path.Combine(baseDir, "Optimizer.exe");
                if (File.Exists(candidate))
                {
                    log.Info($"Found Optimizer.exe: {candidate}");
                    return candidate;
                }
            }
        }
        catch (Exception ex)
        {
            log.Warn($"Optimizer.exe search: {ex.Message}");
        }

        return null;
    }
}
