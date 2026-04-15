using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using SuperTweaker.Core;

namespace SuperTweaker.Modules.GoldenSetup;

public sealed class TweakApplier
{
    private readonly Logger         _log;
    private readonly PowerShellRunner _ps;
    private readonly WindowsInfo    _osInfo;

    /// <summary>Fired after each tweak attempt. Args: completed, total, label.</summary>
    public event Action<int, int, string>? OnProgress;

    public TweakApplier(Logger log, WindowsInfo osInfo)
    {
        _log    = log;
        _osInfo = osInfo;
        _ps     = new PowerShellRunner(log);
    }

    // ──────────────── Profile Loading ────────────────

    public GoldenProfile? LoadProfile()
    {
        var fileName = _osInfo.IsWindows11 ? "golden-win11.json" : "golden-win10.json";

        // Try beside the exe first, then embedded fallback location
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "profiles", fileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName)
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                _log.Info($"Loading profile: {path}");
                var json    = File.ReadAllText(path, System.Text.Encoding.UTF8);
                var profile = JsonSerializer.Deserialize<GoldenProfile>(json, GoldenProfile.JsonOptions);
                if (profile == null)
                {
                    _log.Error("Profile deserialized as null.");
                    return null;
                }
                _log.Info($"Profile '{profile.Name}' loaded — {profile.Tweaks.Count} tweaks.");
                return profile;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to parse profile {path}: {ex.Message}");
                return null;
            }
        }

        _log.Error($"Profile file not found. Expected: {candidates[0]}");
        return null;
    }

    // ──────────────── Apply / Revert / DryRun ────────────────

    public async Task<List<TweakResult>> ApplyAsync(GoldenProfile profile,
        bool dryRun = false, CancellationToken ct = default)
    {
        var results = new List<TweakResult>();
        var tweaks  = FilterForOs(profile.Tweaks);
        int total   = tweaks.Count, done = 0;

        _log.Info(dryRun
            ? $"=== DRY RUN: {profile.Name} ({total} tweaks) ==="
            : $"=== Applying: {profile.Name} ({total} tweaks) ===");

        foreach (var tweak in tweaks)
        {
            if (ct.IsCancellationRequested)
            {
                _log.Warn("Operation cancelled by user.");
                break;
            }

            OnProgress?.Invoke(done, total, tweak.Name);
            _log.Info($"[{done + 1}/{total}] {(dryRun ? "DRY " : "")}Apply: {tweak.Name}");

            string msg;
            bool   ok;
            try
            {
                if (dryRun)
                {
                    var (valid, reason) = ValidateTweak(tweak);
                    ok  = valid;
                    msg = valid ? "OK — passes validation" : $"INVALID — {reason}";
                }
                else
                {
                    await ApplyTweakAsync(tweak, ct);
                    ok  = true;
                    msg = "Applied";
                }
                _log.Success($"  ✓ {tweak.Name}");
            }
            catch (Exception ex)
            {
                ok  = false;
                msg = ex.Message;
                _log.Error($"  ✗ {tweak.Name}: {ex.Message}");
            }

            results.Add(new TweakResult(tweak.Id, tweak.Name, ok, msg, dryRun));
            done++;
        }

        OnProgress?.Invoke(total, total, dryRun ? "Dry run complete" : "Done");
        _log.Success(dryRun
            ? $"Dry run complete. {results.Count(r => r.Success)}/{total} passed."
            : $"Apply complete. {results.Count(r => r.Success)}/{total} succeeded.");

        return results;
    }

    public async Task<List<TweakResult>> RevertAsync(GoldenProfile profile,
        bool dryRun = false, CancellationToken ct = default)
    {
        var results = new List<TweakResult>();
        // Revert in reverse order for cleanliness
        var tweaks  = FilterForOs(profile.Tweaks);
        tweaks.Reverse();
        int total   = tweaks.Count, done = 0;

        _log.Info($"=== {(dryRun ? "DRY RUN " : "")}Reverting: {profile.Name} ({total} tweaks) ===");

        foreach (var tweak in tweaks)
        {
            if (ct.IsCancellationRequested) break;

            OnProgress?.Invoke(done, total, $"Reverting: {tweak.Name}");
            _log.Info($"[{done + 1}/{total}] {(dryRun ? "DRY " : "")}Revert: {tweak.Name}");

            string msg;
            bool   ok;
            try
            {
                if (dryRun)
                {
                    var (valid, reason) = ValidateUndo(tweak);
                    ok  = valid;
                    msg = valid ? "Revert data present" : $"Missing undo data — {reason}";
                }
                else
                {
                    await RevertTweakAsync(tweak, ct);
                    ok  = true;
                    msg = "Reverted";
                }
                _log.Success($"  ✓ {tweak.Name}");
            }
            catch (Exception ex)
            {
                ok  = false;
                msg = ex.Message;
                _log.Error($"  ✗ Revert {tweak.Name}: {ex.Message}");
            }

            results.Add(new TweakResult(tweak.Id, tweak.Name, ok, msg, dryRun));
            done++;
        }

        OnProgress?.Invoke(total, total, "Done");
        return results;
    }

    // ──────────────── Validation (Dry-Run) ────────────────

    /// <summary>Checks a tweak has valid structure without touching the system.</summary>
    public static (bool Valid, string Reason) ValidateTweak(Tweak tweak)
    {
        if (string.IsNullOrWhiteSpace(tweak.Id))   return (false, "Missing Id");
        if (string.IsNullOrWhiteSpace(tweak.Name)) return (false, "Missing Name");

        if (tweak.Registry != null)
            foreach (var r in tweak.Registry)
            {
                if (string.IsNullOrWhiteSpace(r.Path)) return (false, $"Registry entry missing Path");
                if (string.IsNullOrWhiteSpace(r.Name)) return (false, $"Registry entry missing Name");
                if (!IsValidRegPath(r.Path))            return (false, $"Invalid registry path: {r.Path}");
                if (!IsValidValueKind(r.ValueKind))     return (false, $"Unknown ValueKind: {r.ValueKind}");
            }

        if (tweak.Services != null)
            foreach (var s in tweak.Services)
            {
                if (string.IsNullOrWhiteSpace(s.ServiceName))
                    return (false, "Service entry missing ServiceName");
            }

        return (true, "OK");
    }

    public static (bool Valid, string Reason) ValidateUndo(Tweak tweak)
    {
        // A tweak is revertible if every action has corresponding undo data
        if (tweak.Registry != null)
            foreach (var r in tweak.Registry)
            {
                // UndoValue==null is valid (means delete key); but path must be valid
                if (!IsValidRegPath(r.Path))
                    return (false, $"Invalid registry path for undo: {r.Path}");
            }

        if (!string.IsNullOrEmpty(tweak.PowerShellApply) &&
            string.IsNullOrEmpty(tweak.PowerShellUndo))
            return (false, "PowerShellApply present but no PowerShellUndo defined");

        return (true, "OK");
    }

    // ──────────────── Internal Apply/Revert ────────────────

    private async Task ApplyTweakAsync(Tweak tweak, CancellationToken ct)
    {
        if (tweak.Registry != null)
            foreach (var r in tweak.Registry)
                ApplyRegistry(r);

        if (tweak.Services != null)
            foreach (var s in tweak.Services)
            {
                ServiceManager.Stop(s.ServiceName, _log);
                ServiceManager.SetStartMode(s.ServiceName, s.ApplyStartMode, _log);
            }

        if (tweak.Tasks != null)
            foreach (var t in tweak.Tasks)
                SetScheduledTask(t.TaskPath, disable: t.Disable);

        if (!string.IsNullOrWhiteSpace(tweak.PowerShellApply))
        {
            var (ok, out_) = await _ps.RunAsync(tweak.PowerShellApply, ct);
            if (!ok) _log.Warn($"PS Apply returned non-zero: {out_}");
        }
    }

    private async Task RevertTweakAsync(Tweak tweak, CancellationToken ct)
    {
        if (tweak.Registry != null)
            foreach (var r in tweak.Registry)
                UndoRegistry(r);

        if (tweak.Services != null)
            foreach (var s in tweak.Services)
                ServiceManager.SetStartMode(s.ServiceName, s.UndoStartMode, _log);

        if (tweak.Tasks != null)
            foreach (var t in tweak.Tasks)
                SetScheduledTask(t.TaskPath, disable: !t.Disable); // invert: undo = re-enable

        if (!string.IsNullOrWhiteSpace(tweak.PowerShellUndo))
        {
            var (ok, out_) = await _ps.RunAsync(tweak.PowerShellUndo, ct);
            if (!ok) _log.Warn($"PS Undo returned non-zero: {out_}");
        }
    }

    // ──────────────── Registry Helpers ────────────────

    private void ApplyRegistry(RegistryEntry r)
    {
        RegistryHelper.EnsureKeyExists(r.Path);
        var kind = ParseKind(r.ValueKind);
        var val  = ResolveValue(r.Value, kind);
        RegistryHelper.SetValue(r.Path, r.Name, val, kind);
        _log.Info($"  REG SET  {r.Path}\\{r.Name} = {val}");
    }

    private void UndoRegistry(RegistryEntry r)
    {
        if (r.UndoValue == null)
        {
            RegistryHelper.DeleteValue(r.Path, r.Name);
            _log.Info($"  REG DEL  {r.Path}\\{r.Name}");
        }
        else
        {
            var kind = ParseKind(r.ValueKind);
            var val  = ResolveValue(r.UndoValue.Value, kind);
            RegistryHelper.SetValue(r.Path, r.Name, val, kind);
            _log.Info($"  REG SET  {r.Path}\\{r.Name} = {val} (undo)");
        }
    }

    // ──────────────── Scheduled Tasks ────────────────

    /// <summary>
    /// Enables or disables a scheduled task via schtasks.exe.
    /// Correct syntax: schtasks /Change /TN "path" /Disable|/Enable
    /// Silently ignores errors — many tasks don't exist on all builds.
    /// </summary>
    private static void SetScheduledTask(string taskPath, bool disable)
    {
        var flag = disable ? "/Disable" : "/Enable";
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo(
                    "schtasks.exe",
                    $"/Change /TN \"{taskPath}\" {flag}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                }
            };
            p.Start();
            p.WaitForExit(8_000);
        }
        catch { /* task may not exist on all Windows builds — ignore */ }
    }

    // ──────────────── OS Filter ────────────────

    private List<Tweak> FilterForOs(List<Tweak> all) => all.Where(t =>
        t.Os == TweakOs.Both ||
        (t.Os == TweakOs.Win11Only && _osInfo.IsWindows11) ||
        (t.Os == TweakOs.Win10Only && _osInfo.IsWindows10)
    ).ToList();

    // ──────────────── Conversion ────────────────

    /// <summary>
    /// Converts a JsonElement (from JSON deserialization) to the correct .NET type
    /// for registry writes. Handles number, string, and null gracefully.
    /// </summary>
    public static object ResolveValue(JsonElement el, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.DWord => el.ValueKind switch
            {
                JsonValueKind.Number => el.GetInt32(),
                JsonValueKind.String => int.TryParse(el.GetString(), out var n) ? n : 0,
                _                   => 0
            },
            RegistryValueKind.QWord => el.ValueKind switch
            {
                JsonValueKind.Number => el.GetInt64(),
                JsonValueKind.String => long.TryParse(el.GetString(), out var n) ? n : 0L,
                _                   => 0L
            },
            _ => el.ValueKind switch
            {
                JsonValueKind.String => el.GetString() ?? "",
                JsonValueKind.Number => el.GetRawText(),
                _                   => el.GetRawText()
            }
        };
    }

    public static RegistryValueKind ParseKind(string k) => k.ToUpperInvariant() switch
    {
        "DWORD"        => RegistryValueKind.DWord,
        "QWORD"        => RegistryValueKind.QWord,
        "STRING"       => RegistryValueKind.String,
        "EXPANDSTRING" => RegistryValueKind.ExpandString,
        "BINARY"       => RegistryValueKind.Binary,
        "MULTISTRING"  => RegistryValueKind.MultiString,
        _              => RegistryValueKind.DWord
    };

    // ──────────────── Validation Helpers ────────────────

    private static readonly HashSet<string> ValidHives = new(StringComparer.OrdinalIgnoreCase)
    {
        "HKEY_LOCAL_MACHINE", "HKLM",
        "HKEY_CURRENT_USER",  "HKCU",
        "HKEY_CLASSES_ROOT",  "HKCR",
        "HKEY_USERS",         "HKU",
        "HKEY_CURRENT_CONFIG","HKCC"
    };

    private static bool IsValidRegPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var sep = path.IndexOf('\\');
        if (sep < 0) return false;
        return ValidHives.Contains(path[..sep]);
    }

    private static readonly HashSet<string> ValidKinds =
        new(StringComparer.OrdinalIgnoreCase)
        { "DWORD","QWORD","STRING","EXPANDSTRING","BINARY","MULTISTRING" };

    private static bool IsValidValueKind(string k) => ValidKinds.Contains(k);
}
