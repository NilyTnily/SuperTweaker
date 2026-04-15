using System.IO;
using System.Text.Json;
using SuperTweaker.Core;

namespace SuperTweaker.Modules.Spoofer;

public sealed class SpooferBackupManager
{
    private readonly Logger _log;
    private readonly MacSpoofer _macSpoofer;
    private readonly HwidSpoofer _hwidSpoofer;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string BackupDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SuperTweaker", "SpooferBackups");

    public SpooferBackupManager(Logger log, MacSpoofer macSpoofer, HwidSpoofer hwidSpoofer)
    {
        _log = log;
        _macSpoofer = macSpoofer;
        _hwidSpoofer = hwidSpoofer;
    }

    public string CreateSnapshot()
    {
        Directory.CreateDirectory(BackupDirectory);
        var snapshot = new SpooferSnapshot
        {
            CreatedAtUtc = DateTime.UtcNow,
            Machine = Environment.MachineName,
            Hwid = _hwidSpoofer.GetCurrentIds().ToDictionary(k => k.Key, v => v.Value),
            NicRegistry = _macSpoofer.GetAdapters()
                .Select(n => new NicSnapshot
                {
                    AdapterGuid = n.AdapterGuid,
                    DeviceId = n.DeviceId,
                    Name = n.Name,
                    RegistryMac = n.RegistryMac
                })
                .ToList()
        };

        var fileName = $"spoofer-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var fullPath = Path.Combine(BackupDirectory, fileName);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(snapshot, JsonOptions));
        _log.Success($"Custom spoofer backup created: {fullPath}");
        return fullPath;
    }

    public bool HasSnapshots() => Directory.Exists(BackupDirectory) &&
                                  Directory.EnumerateFiles(BackupDirectory, "*.json").Any();

    public string? GetLatestSnapshotPath()
    {
        if (!HasSnapshots()) return null;
        return Directory.EnumerateFiles(BackupDirectory, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault() ?? string.Empty;
    }

    public async Task<bool> RestoreLatestSnapshotAsync(CancellationToken ct = default)
    {
        var latest = GetLatestSnapshotPath();
        if (latest == null)
        {
            _log.Warn("No custom spoofer backup snapshot found.");
            return false;
        }

        SpooferSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<SpooferSnapshot>(File.ReadAllText(latest), JsonOptions);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to read snapshot file: {ex.Message}");
            return false;
        }

        if (snapshot == null)
        {
            _log.Error("Snapshot is invalid.");
            return false;
        }

        _log.Info($"Restoring from custom snapshot: {latest}");
        await _hwidSpoofer.RestoreFromSnapshotAsync(snapshot.Hwid, ct);

        foreach (var nic in snapshot.NicRegistry)
            await _macSpoofer.ApplyRegistryMacAsync(nic.AdapterGuid, nic.DeviceId, nic.RegistryMac, ct);

        _log.Success("Custom spoofer snapshot restore completed.");
        return true;
    }
}

public sealed class SpooferSnapshot
{
    public DateTime CreatedAtUtc { get; set; }
    public string Machine { get; set; } = "";
    public Dictionary<string, string> Hwid { get; set; } = new();
    public List<NicSnapshot> NicRegistry { get; set; } = new();
}

public sealed class NicSnapshot
{
    public string AdapterGuid { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string RegistryMac { get; set; } = "";
}
