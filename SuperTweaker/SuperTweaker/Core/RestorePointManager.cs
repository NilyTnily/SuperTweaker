using System.Management;

namespace SuperTweaker.Core;

public static class RestorePointManager
{
    /// <summary>Creates a Windows System Restore point. Returns true if succeeded.</summary>
    public static async Task<bool> CreateAsync(string description, Logger? log = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                log?.Info($"Creating restore point: \"{description}\"...");

                // Enable SR on system drive if disabled
                var enablePs = new PowerShellRunner(log);
                enablePs.Run("Enable-ComputerRestore -Drive 'C:\\'");

                var scope = new ManagementScope(@"\\localhost\root\default");
                using var cls   = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);

                var inParams = cls.GetMethodParameters("CreateRestorePoint");
                inParams["Description"]      = description;
                inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
                inParams["EventType"]        = 100; // BEGIN_SYSTEM_CHANGE

                var outParams = cls.InvokeMethod("CreateRestorePoint", inParams, null);
                var returnVal = Convert.ToInt32(outParams["ReturnValue"]);

                if (returnVal == 0)
                {
                    log?.Success($"Restore point created: \"{description}\"");
                    return true;
                }
                else
                {
                    log?.Warn($"Restore point WMI returned {returnVal} — may already exist or SR is throttled.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                log?.Error($"Restore point creation failed: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Opens the built-in Windows System Restore wizard so the user can pick
    /// the restore point created by this tool and execute it natively.
    /// </summary>
    public static void OpenRestoreWizard()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName  = "rstrui.exe",
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Opens the built-in "Backup and Restore (Windows 7)" panel for creating
    /// a full system image backup — the user drives this natively.
    /// </summary>
    public static void OpenBackupAndRestore()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName  = "control.exe",
            Arguments = "/name Microsoft.BackupAndRestoreCenter",
            UseShellExecute = true
        });
    }

    /// <summary>Lists existing restore points created by SuperTweaker.</summary>
    public static List<(string Description, string Date, int SequenceNumber)> ListSuperTweakerPoints()
    {
        var points = new List<(string, string, int)>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\default", "SELECT * FROM SystemRestore");

            foreach (ManagementObject o in searcher.Get())
            {
                var desc = o["Description"]?.ToString() ?? "";
                if (desc.StartsWith("SuperTweaker", StringComparison.OrdinalIgnoreCase))
                {
                    var raw  = o["CreationTime"]?.ToString() ?? "";
                    var seq  = Convert.ToInt32(o["SequenceNumber"]);
                    points.Add((desc, raw, seq));
                }
            }
        }
        catch { }
        return points;
    }
}
