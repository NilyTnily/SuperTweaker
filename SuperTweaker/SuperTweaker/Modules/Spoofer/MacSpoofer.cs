using System.Management;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using SuperTweaker.Core;

namespace SuperTweaker.Modules.Spoofer;

/// <summary>
/// NIC adapter info combining WMI and .NET NetworkInterface data.
/// AdapterGuid is the NetCfgInstanceId used for registry lookup — far more reliable
/// than the WMI DeviceID index which can change across reboots.
/// </summary>
public sealed class NicInfo
{
    public string Name        { get; init; } = "";
    public string DeviceId    { get; init; } = "";  // WMI numeric index (for WMI restart)
    public string AdapterGuid { get; init; } = "";  // {xxxx-...} — used for registry path
    public string CurrentMac  { get; init; } = "";  // hardware or currently-active MAC
    public string RegistryMac { get; init; } = "";  // value in NetworkAddress registry key (if spoofed)
    public bool   IsActive    { get; init; }
    public string DisplayLine => $"{Name}  [{FormatMac(CurrentMac)}]";

    public static string FormatMac(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        raw = raw.Replace(":", "").Replace("-", "").ToUpper();
        if (raw.Length != 12) return raw;
        return string.Join(":", Enumerable.Range(0, 6).Select(i => raw.Substring(i * 2, 2)));
    }
}

public sealed class MacSpoofer
{
    private readonly Logger _log;

    // NIC class GUID — all network adapters live under this key
    private const string NicClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}";

    public MacSpoofer(Logger log) => _log = log;

    // ──────────────── List Adapters ────────────────

    public List<NicInfo> GetAdapters()
    {
        var result = new List<NicInfo>();
        try
        {
            // Get all .NET interfaces so we can check operational status
            var netIfs = NetworkInterface.GetAllNetworkInterfaces();

            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name, MACAddress, GUID, PhysicalAdapter FROM Win32_NetworkAdapter " +
                "WHERE PhysicalAdapter=True");

            foreach (ManagementObject obj in searcher.Get())
            {
                var name     = obj["Name"]?.ToString()        ?? "";
                var deviceId = obj["DeviceID"]?.ToString()    ?? "";
                var macRaw   = obj["MACAddress"]?.ToString()  ?? "";
                var guid     = obj["GUID"]?.ToString()        ?? "";

                // Normalise MAC to 12 uppercase hex chars
                var macNorm = macRaw.Replace(":", "").Replace("-", "").ToUpper();

                // Registry MAC (empty string if not spoofed)
                var regMac = GetRegistryMac(guid);

                // Active = the .NET interface with this MAC is Up
                bool active = netIfs.Any(n =>
                    n.GetPhysicalAddress().ToString() == macNorm &&
                    n.OperationalStatus == OperationalStatus.Up);

                result.Add(new NicInfo
                {
                    Name        = name,
                    DeviceId    = deviceId,
                    AdapterGuid = guid,
                    CurrentMac  = NicInfo.FormatMac(macNorm),
                    RegistryMac = NicInfo.FormatMac(regMac),
                    IsActive    = active
                });
            }
        }
        catch (Exception ex) { _log.Error($"GetAdapters: {ex.Message}"); }
        return result;
    }

    // ──────────────── Spoof ────────────────

    /// <param name="adapterGuid">
    /// The GUID from <see cref="NicInfo.AdapterGuid"/> — used for registry path lookup.
    /// </param>
    public async Task<bool> SpoofAsync(string adapterGuid, string wmiDeviceId,
        string newMac, CancellationToken ct = default)
    {
        newMac = NormaliseMac(newMac);
        if (newMac.Length != 12)
        {
            _log.Error("Invalid MAC: must be exactly 12 hex characters (e.g. AA:BB:CC:DD:EE:FF).");
            return false;
        }

        // Warn on multicast bit — routers/switches may reject frames
        if ((Convert.ToByte(newMac[..2], 16) & 0x01) != 0)
            _log.Warn("First octet is odd — multicast bit set. This may cause connectivity issues.");

        _log.Info($"Spoofing NIC GUID={adapterGuid} → {NicInfo.FormatMac(newMac)}");

        return await Task.Run(() =>
        {
            var subKeyPath = FindNicSubKey(adapterGuid);
            if (subKeyPath == null)
            {
                _log.Error($"Registry subkey not found for GUID {adapterGuid}. Ensure the adapter exists.");
                return false;
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(subKeyPath, writable: true);
                if (key == null)
                {
                    _log.Error($"Cannot open registry key (need admin): {subKeyPath}");
                    return false;
                }

                key.SetValue("NetworkAddress", newMac, RegistryValueKind.String);
                _log.Info($"  Registry updated: {subKeyPath}\\NetworkAddress = {newMac}");

                bool ok = RestartAdapterByDeviceId(wmiDeviceId);
                if (ok)
                    _log.Success($"MAC spoofed to {NicInfo.FormatMac(newMac)}");
                else
                    _log.Warn("Adapter restart may have failed. Try netsh or reboot to apply.");

                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Spoof registry write failed: {ex.Message}");
                return false;
            }
        }, ct);
    }

    // ──────────────── Restore ────────────────

    public async Task<bool> RestoreAsync(string adapterGuid, string wmiDeviceId,
        CancellationToken ct = default)
    {
        _log.Info($"Restoring hardware MAC for GUID {adapterGuid}...");

        return await Task.Run(() =>
        {
            var subKeyPath = FindNicSubKey(adapterGuid);
            if (subKeyPath == null)
            {
                _log.Error("Registry subkey not found.");
                return false;
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(subKeyPath, writable: true);
                if (key == null) { _log.Error("Cannot open registry key."); return false; }

                key.DeleteValue("NetworkAddress", throwOnMissingValue: false);
                _log.Info("  NetworkAddress registry value removed.");

                RestartAdapterByDeviceId(wmiDeviceId);
                _log.Success("Hardware MAC restored. Adapter restarted.");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Restore failed: {ex.Message}");
                return false;
            }
        }, ct);
    }

    public async Task<bool> ApplyRegistryMacAsync(string adapterGuid, string wmiDeviceId, string? registryMac,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(registryMac))
            return await RestoreAsync(adapterGuid, wmiDeviceId, ct);

        return await SpoofAsync(adapterGuid, wmiDeviceId, registryMac, ct);
    }

    // ──────────────── Static Helpers ────────────────

    public static string GenerateRandomMac()
    {
        var bytes = new byte[6];
        Random.Shared.NextBytes(bytes);
        // Clear multicast bit (bit 0) and set locally-administered bit (bit 1)
        bytes[0] = (byte)((bytes[0] & 0xFE) | 0x02);
        return string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    public static bool IsValidMac(string mac)
    {
        var norm = NormaliseMac(mac);
        return norm.Length == 12 && norm.All(c => "0123456789ABCDEF".Contains(c));
    }

    // ──────────────── Private ────────────────

    /// <summary>
    /// Finds the NIC class subkey (e.g. 0003) whose NetCfgInstanceId matches
    /// the adapter GUID from WMI. This is the correct, reliable lookup.
    /// </summary>
    private static string? FindNicSubKey(string adapterGuid)
    {
        if (string.IsNullOrEmpty(adapterGuid)) return null;

        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(NicClassKey, false);
            if (classKey == null) return null;

            foreach (var sub in classKey.GetSubKeyNames())
            {
                // Skip non-numeric entries like "Properties"
                if (!sub.All(c => char.IsDigit(c))) continue;

                using var subKey = classKey.OpenSubKey(sub, false);
                var cfgId = subKey?.GetValue("NetCfgInstanceId")?.ToString() ?? "";
                if (string.Equals(cfgId, adapterGuid, StringComparison.OrdinalIgnoreCase))
                    return $@"{NicClassKey}\{sub}";
            }
        }
        catch { }
        return null;
    }

    private static string GetRegistryMac(string adapterGuid)
    {
        if (string.IsNullOrEmpty(adapterGuid)) return "";
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(NicClassKey, false);
            if (classKey == null) return "";

            foreach (var sub in classKey.GetSubKeyNames())
            {
                if (!sub.All(c => char.IsDigit(c))) continue;
                using var subKey = classKey.OpenSubKey(sub, false);
                var cfgId = subKey?.GetValue("NetCfgInstanceId")?.ToString() ?? "";
                if (!string.Equals(cfgId, adapterGuid, StringComparison.OrdinalIgnoreCase))
                    continue;
                return subKey?.GetValue("NetworkAddress")?.ToString() ?? "";
            }
        }
        catch { }
        return "";
    }

    private bool RestartAdapterByDeviceId(string wmiDeviceId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_NetworkAdapter WHERE DeviceID='{wmiDeviceId}'");

            foreach (ManagementObject obj in searcher.Get())
            {
                obj.InvokeMethod("Disable", null);
                Thread.Sleep(1_000);
                obj.InvokeMethod("Enable", null);
                return true;
            }
        }
        catch (Exception ex) { _log.Warn($"Adapter restart via WMI failed: {ex.Message}"); }
        return false;
    }

    private static string NormaliseMac(string mac)
        => mac.Replace(":", "").Replace("-", "").Replace(" ", "").ToUpper();
}
