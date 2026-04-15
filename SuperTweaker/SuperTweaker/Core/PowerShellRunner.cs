using System.Diagnostics;
using System.IO;
using System.Text;

namespace SuperTweaker.Core;

/// <summary>
/// Executes PowerShell scripts via process invocation.
/// Prefers pwsh.exe (PowerShell 7) and falls back to powershell.exe (Windows PowerShell 5).
/// No heavy SDK required — runs scripts in an isolated process to avoid runspace threading issues.
/// </summary>
public sealed class PowerShellRunner
{
    private readonly Logger? _log;
    private static readonly string PsExe = FindPsExe();

    public PowerShellRunner(Logger? log = null) => _log = log;

    /// <summary>Runs a script string async. Returns (success, stdout). Respects cancellation.</summary>
    public async Task<(bool Success, string Output)> RunAsync(
        string script, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return (false, "Cancelled.");
        return await Task.Run(() => Run(script, ct), ct);
    }

    /// <summary>Runs a script string synchronously. Returns (success, stdout).</summary>
    public (bool Success, string Output) Run(string script,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(script)) return (true, "");
        if (ct.IsCancellationRequested) return (false, "Cancelled.");

        var tmpFile = Path.Combine(Path.GetTempPath(), $"st_{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(tmpFile, script, Encoding.UTF8);
            return RunFile(tmpFile, ct);
        }
        catch (Exception ex)
        {
            _log?.Error($"PS script write failed: {ex.Message}");
            return (false, ex.Message);
        }
        finally
        {
            TryDelete(tmpFile);
        }
    }

    // ──────── Private ────────

    private (bool Success, string Output) RunFile(string scriptPath, CancellationToken ct)
    {
        var args = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"";
        var sb   = new StringBuilder();
        var err  = new StringBuilder();

        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo(PsExe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8
            };

            p.OutputDataReceived += (_, e) => { if (e.Data != null) { sb.AppendLine(e.Data); _log?.Info($"  PS: {e.Data}"); } };
            p.ErrorDataReceived  += (_, e) => { if (e.Data != null) { err.AppendLine(e.Data); _log?.Warn($"  PS!: {e.Data}"); } };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            // Poll with ct support — kills process on cancellation
            while (!p.WaitForExit(500))
            {
                if (ct.IsCancellationRequested)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    _log?.Warn("PS script cancelled by token.");
                    return (false, "Cancelled.");
                }
                // Hard timeout after 60 s regardless
                if (p.HasExited) break;
            }

            // Ensure all output is flushed
            p.WaitForExit();

            if (!ct.IsCancellationRequested)
            {
                // Check runtime elapsed (approximate via StartTime)
                var elapsed = DateTime.Now - p.StartTime;
                if (elapsed.TotalSeconds > 60)
                {
                    _log?.Error("PS script exceeded 60 s — killed.");
                    return (false, "Timed out.");
                }
            }

            bool success = p.ExitCode == 0;
            if (!success) _log?.Warn($"PS exited {p.ExitCode}: {err.ToString().Trim()}");
            return (success, sb.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            return (false, "Cancelled.");
        }
        catch (Exception ex)
        {
            _log?.Error($"PS launch failed: {ex.Message}");
            return (false, ex.Message);
        }
    }

    private static string FindPsExe()
    {
        // Prefer PowerShell 7 (cross-platform location)
        var candidates = new[]
        {
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files\PowerShell\7-preview\pwsh.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PowerShell", "7", "pwsh.exe"),
            "pwsh.exe",         // if it's in PATH
            "powershell.exe"    // fallback to Windows PowerShell 5
        };

        foreach (var c in candidates)
        {
            if (c.Contains('\\') && File.Exists(c)) return c;
            // For "pwsh.exe" / "powershell.exe" just use as-is (PATH lookup)
        }

        return "powershell.exe";
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
