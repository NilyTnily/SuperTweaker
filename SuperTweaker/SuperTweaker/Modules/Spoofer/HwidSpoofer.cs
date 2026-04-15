using Microsoft.Win32;
using SuperTweaker.Core;

namespace SuperTweaker.Modules.Spoofer;

/// <summary>
/// User-mode / registry-level HWID identifiers.
/// Scope: MachineGuid, HwProfileGuid, SQM MachineId, ProductId, SusClientId, optional hostname.
/// Original values are backed up on first spoof and can be restored any time.
/// Kernel/SMBIOS/EFI spoofing is intentionally out of scope.
/// </summary>
public sealed class HwidSpoofer
{
    private readonly Logger _log;

    private const string BackupKeyPath =
        @"HKEY_LOCAL_MACHINE\SOFTWARE\SuperTweaker\HwidBackup";

    private const string MachineGuidPath =
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography";
    private const string HwProfileGuidPath =
        @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\IDConfigDB\Hardware Profiles\0001";
    private const string SqmMachinePath =
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\SQMClient";
    private const string NtCurrentVersionPath =
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string WindowsUpdatePath =
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate";
    private const string ComputerNamePath =
        @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName";
    private const string ActiveComputerNamePath =
        @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\ComputerName\ActiveComputerName";

    public HwidSpoofer(Logger log) => _log = log;

    // ──────────────── Read Current IDs ────────────────

    public IReadOnlyDictionary<string, string> GetCurrentIds()
    {
        return new Dictionary<string, string>
        {
            ["MachineGuid"]      = Read(MachineGuidPath,  "MachineGuid"),
            ["HwProfileGuid"]    = Read(HwProfileGuidPath,"HwProfileGuid"),
            ["SqmMachineId"]     = Read(SqmMachinePath,   "MachineId"),
            ["ProductId"]        = Read(NtCurrentVersionPath, "ProductId"),
            ["SusClientId"]      = Read(WindowsUpdatePath, "SusClientId"),
            ["ComputerName"]     = Environment.MachineName,
            ["InstallDate"]      = ReadInstallDate()
        };
    }

    public bool HasBackup =>
        RegistryHelper.GetValue(BackupKeyPath, "MachineGuid") is not null;

    // ──────────────── Spoof ────────────────

    public async Task<bool> SpoofAsync(bool randomiseComputerName,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            _log.Info("=== Spoofing user-mode HWID identifiers ===");

            // Step 1: backup if first time
            if (!HasBackup) Backup();

            // MachineGuid
            var mguid = Guid.NewGuid().ToString("D").ToLower();
            if (!Write(MachineGuidPath, "MachineGuid", mguid, RegistryValueKind.String))
                return false;
            _log.Success($"  MachineGuid → {mguid}");

            // HwProfileGuid — must include braces
            var hwguid = "{" + Guid.NewGuid().ToString("D").ToUpper() + "}";
            Write(HwProfileGuidPath, "HwProfileGuid", hwguid, RegistryValueKind.String);
            _log.Success($"  HwProfileGuid → {hwguid}");

            // SQM MachineId
            var sqmId = "{" + Guid.NewGuid().ToString("D").ToUpper() + "}";
            RegistryHelper.EnsureKeyExists(SqmMachinePath);
            Write(SqmMachinePath, "MachineId", sqmId, RegistryValueKind.String);
            _log.Success($"  SqmMachineId → {sqmId}");

            // ProductId (Windows product display identifier style)
            var productId = $"{RandomDigits(5)}-{RandomDigits(5)}-{RandomDigits(5)}-{RandomDigits(5)}";
            Write(NtCurrentVersionPath, "ProductId", productId, RegistryValueKind.String);
            _log.Success($"  ProductId → {productId}");

            // Windows Update SusClientId
            var susClientId = "{" + Guid.NewGuid().ToString("D").ToUpper() + "}";
            RegistryHelper.EnsureKeyExists(WindowsUpdatePath);
            Write(WindowsUpdatePath, "SusClientId", susClientId, RegistryValueKind.String);
            _log.Success($"  SusClientId → {susClientId}");

            // ComputerName (optional)
            if (randomiseComputerName)
            {
                var newName = "DESKTOP-" + RandomHex(7);
                RegistryHelper.EnsureKeyExists(ComputerNamePath);
                RegistryHelper.EnsureKeyExists(ActiveComputerNamePath);
                Write(ComputerNamePath,       "ComputerName", newName, RegistryValueKind.String);
                Write(ActiveComputerNamePath, "ComputerName", newName, RegistryValueKind.String);
                _log.Success($"  ComputerName → {newName} (effective on next reboot)");
            }

            _log.Success("HWID spoof complete. Reboot recommended.");
            return true;
        }, ct);
    }

    // ──────────────── Restore ────────────────

    public async Task<bool> RestoreAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            if (!HasBackup)
            {
                _log.Warn("No backup found. Nothing to restore.");
                return false;
            }

            _log.Info("=== Restoring original HWID identifiers ===");

            RestoreValue(MachineGuidPath,  "MachineGuid",  "MachineGuid");
            RestoreValue(HwProfileGuidPath,"HwProfileGuid","HwProfileGuid");
            RestoreValue(SqmMachinePath,   "MachineId",    "SqmMachineId");
            RestoreValue(NtCurrentVersionPath, "ProductId", "ProductId");
            RestoreValue(WindowsUpdatePath, "SusClientId", "SusClientId");
            RestoreValue(ComputerNamePath,       "ComputerName", "ComputerName");
            RestoreValue(ActiveComputerNamePath, "ComputerName", "ComputerName");

            _log.Success("HWID restore complete. Reboot recommended.");
            return true;
        }, ct);
    }

    public async Task<bool> RestoreFromSnapshotAsync(IReadOnlyDictionary<string, string> snapshot,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            _log.Info("=== Restoring HWID identifiers from custom snapshot ===");
            if (snapshot.Count == 0)
            {
                _log.Warn("Snapshot is empty.");
                return false;
            }

            if (snapshot.TryGetValue("MachineGuid", out var machineGuid) && !string.IsNullOrWhiteSpace(machineGuid))
                Write(MachineGuidPath, "MachineGuid", machineGuid, RegistryValueKind.String);

            if (snapshot.TryGetValue("HwProfileGuid", out var hwProfileGuid) && !string.IsNullOrWhiteSpace(hwProfileGuid))
                Write(HwProfileGuidPath, "HwProfileGuid", hwProfileGuid, RegistryValueKind.String);

            if (snapshot.TryGetValue("SqmMachineId", out var sqmMachineId) && !string.IsNullOrWhiteSpace(sqmMachineId))
                Write(SqmMachinePath, "MachineId", sqmMachineId, RegistryValueKind.String);

            if (snapshot.TryGetValue("ProductId", out var productId) && !string.IsNullOrWhiteSpace(productId))
                Write(NtCurrentVersionPath, "ProductId", productId, RegistryValueKind.String);

            if (snapshot.TryGetValue("SusClientId", out var susClientId) && !string.IsNullOrWhiteSpace(susClientId))
                Write(WindowsUpdatePath, "SusClientId", susClientId, RegistryValueKind.String);

            if (snapshot.TryGetValue("ComputerName", out var computerName) && !string.IsNullOrWhiteSpace(computerName))
            {
                Write(ComputerNamePath, "ComputerName", computerName, RegistryValueKind.String);
                Write(ActiveComputerNamePath, "ComputerName", computerName, RegistryValueKind.String);
            }

            _log.Success("Custom HWID snapshot restore complete. Reboot recommended.");
            return true;
        }, ct);
    }

    // ──────────────── Private ────────────────

    private void Backup()
    {
        RegistryHelper.EnsureKeyExists(BackupKeyPath);
        var ids = GetCurrentIds();
        foreach (var (k, v) in ids)
            RegistryHelper.SetValue(BackupKeyPath, k, v, RegistryValueKind.String);
        _log.Info("Original HWID values backed up.");
    }

    private void RestoreValue(string targetPath, string valueName, string backupKey)
    {
        var saved = RegistryHelper.GetValue(BackupKeyPath, backupKey)?.ToString();
        if (saved == null) { _log.Warn($"  No backup for {backupKey}, skipping."); return; }
        Write(targetPath, valueName, saved, RegistryValueKind.String);
        _log.Success($"  {valueName} → {saved}");
    }

    private bool Write(string path, string name, string value, RegistryValueKind kind)
    {
        if (RegistryHelper.SetValue(path, name, value, kind)) return true;
        _log.Error($"  Failed to write {path}\\{name}");
        return false;
    }

    private static string Read(string path, string name)
        => RegistryHelper.GetValue(path, name)?.ToString() ?? "—";

    private static string ReadInstallDate()
    {
        try
        {
            var epoch = RegistryHelper.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                "InstallDate");
            if (epoch == null) return "—";
            var dt = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(epoch));
            return dt.LocalDateTime.ToString("yyyy-MM-dd");
        }
        catch { return "—"; }
    }

    private static string RandomHex(int chars)
    {
        const string hex = "0123456789ABCDEF";
        return new string(Enumerable.Range(0, chars)
            .Select(_ => hex[Random.Shared.Next(16)]).ToArray());
    }

    private static string RandomDigits(int chars)
    {
        const string digits = "0123456789";
        return new string(Enumerable.Range(0, chars)
            .Select(_ => digits[Random.Shared.Next(10)]).ToArray());
    }
}
