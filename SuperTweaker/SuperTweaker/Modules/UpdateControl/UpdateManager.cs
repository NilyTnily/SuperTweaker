using System.ServiceProcess;
using SuperTweaker.Core;

namespace SuperTweaker.Modules.UpdateControl;

public class UpdateManager
{
    private readonly Logger _log;
    private readonly WindowsInfo _osInfo;

    // Services to disable for "updates off"
    private static readonly (string Name, string DisabledMode, string OnMode)[] UpdateServices =
    {
        ("wuauserv",      "disabled", "manual"),   // Windows Update
        ("UsoSvc",        "disabled", "manual"),   // Update Orchestrator
        ("WaaSMedicSvc",  "disabled", "manual"),   // Windows Update Medic (self-healer)
        ("bits",          "disabled", "auto"),     // BITS
        ("dosvc",         "disabled", "auto"),     // Delivery Optimization
    };

    // Scheduled tasks for Windows 10 and 11 differ slightly.
    private static readonly string[] Win10UpdateTasks =
    {
        @"\Microsoft\Windows\WindowsUpdate\Automatic App Update",
        @"\Microsoft\Windows\WindowsUpdate\Scheduled Start",
        @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan",
        @"\Microsoft\Windows\UpdateOrchestrator\USO_UxBroker",
        @"\Microsoft\Windows\UpdateOrchestrator\Report policies",
    };
    private static readonly string[] Win11UpdateTasks =
    {
        @"\Microsoft\Windows\WindowsUpdate\Automatic App Update",
        @"\Microsoft\Windows\WindowsUpdate\Scheduled Start",
        @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan",
        @"\Microsoft\Windows\UpdateOrchestrator\USO_UxBroker",
        @"\Microsoft\Windows\UpdateOrchestrator\UpdateModelTask",
        @"\Microsoft\Windows\WaaSMedic\PerformRemediation",
    };

    // Registry: pause updates via policy
    private const string WuPolicyPath =
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string WuAuPolicyPath =
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";

    public UpdateManager(Logger log, WindowsInfo osInfo)
    {
        _log = log;
        _osInfo = osInfo;
    }

    public async Task DisableUpdatesAsync(CancellationToken ct = default)
    {
        _log.Info("=== Disabling Windows Update ===");

        // 1. Registry policy
        _log.Info("Applying registry policies...");
        RegistryHelper.EnsureKeyExists(WuPolicyPath);
        RegistryHelper.EnsureKeyExists(WuAuPolicyPath);
        RegistryHelper.SetValue(WuPolicyPath,   "DisableWindowsUpdateAccess", 1, Microsoft.Win32.RegistryValueKind.DWord);
        RegistryHelper.SetValue(WuAuPolicyPath, "NoAutoUpdate",               1, Microsoft.Win32.RegistryValueKind.DWord);
        RegistryHelper.SetValue(WuAuPolicyPath, "AUOptions",                  1, Microsoft.Win32.RegistryValueKind.DWord);

        // 2. Stop + disable services
        _log.Info("Stopping and disabling update services...");
        foreach (var (name, mode, _) in UpdateServices)
        {
            if (ct.IsCancellationRequested) break;
            ServiceManager.Stop(name, _log);
            await SetServiceStartAsync(name, mode, ct);
        }

        // 3. WaaSMedicSvc — protected service, use registry override
        _log.Info("Locking WaaSMedicSvc via registry...");
        LockWaasMedic(disable: true);

        // 4. Scheduled tasks
        var tasks = GetUpdateTasksForOs();
        _log.Info($"Disabling update scheduled tasks ({(_osInfo.IsWindows11 ? "Windows 11" : _osInfo.IsWindows10 ? "Windows 10" : "generic")} profile, {tasks.Length} tasks)...");
        foreach (var task in tasks)
        {
            if (ct.IsCancellationRequested) break;
            await DisableTaskAsync(task, ct);
        }

        _log.Success("Windows Update disabled.");
    }

    public async Task EnableUpdatesAsync(CancellationToken ct = default)
    {
        _log.Info("=== Re-enabling Windows Update ===");

        // 1. Remove registry policy
        RegistryHelper.DeleteValue(WuPolicyPath,   "DisableWindowsUpdateAccess");
        RegistryHelper.DeleteValue(WuAuPolicyPath, "NoAutoUpdate");
        RegistryHelper.DeleteValue(WuAuPolicyPath, "AUOptions");

        // 2. Restore services
        foreach (var (name, _, onMode) in UpdateServices)
        {
            if (ct.IsCancellationRequested) break;
            await SetServiceStartAsync(name, onMode, ct);
        }

        // 3. Unlock WaaSMedicSvc
        LockWaasMedic(disable: false);

        // 4. Re-enable tasks
        var tasks = GetUpdateTasksForOs();
        _log.Info($"Re-enabling update scheduled tasks ({(_osInfo.IsWindows11 ? "Windows 11" : _osInfo.IsWindows10 ? "Windows 10" : "generic")} profile, {tasks.Length} tasks)...");
        foreach (var task in tasks)
        {
            if (ct.IsCancellationRequested) break;
            await EnableTaskAsync(task, ct);
        }

        // 5. Start the update service again
        ServiceManager.Start("wuauserv", _log);

        _log.Success("Windows Update re-enabled.");
    }

    /// <summary>Reads current update service status without changing anything.</summary>
    public UpdateStatus GetStatus()
    {
        var wuStatus = ServiceManager.GetStartMode("wuauserv");
        bool disabled = wuStatus == ServiceStartMode.Disabled;
        var policyVal = RegistryHelper.GetValue(WuAuPolicyPath, "NoAutoUpdate");
        bool policyDisabled = policyVal is int v && v == 1;
        return new UpdateStatus(disabled, policyDisabled);
    }

    // ──────── Helpers ────────

    private static async Task SetServiceStartAsync(string name, string mode, CancellationToken ct)
    {
        await Task.Run(() => ServiceManager.SetStartMode(name, mode), ct);
    }

    private string[] GetUpdateTasksForOs()
    {
        if (_osInfo.IsWindows11) return Win11UpdateTasks;
        if (_osInfo.IsWindows10) return Win10UpdateTasks;
        return Win11UpdateTasks;
    }

    private static async Task DisableTaskAsync(string path, CancellationToken ct) =>
        await Task.Run(() =>
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                        $"/Change /TN \"{path}\" /Disable")
                    { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                p?.WaitForExit(3000);
            }
            catch { }
        }, ct);

    private static async Task EnableTaskAsync(string path, CancellationToken ct) =>
        await Task.Run(() =>
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                        $"/Change /TN \"{path}\" /Enable")
                    { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                p?.WaitForExit(3000);
            }
            catch { }
        }, ct);

    /// <summary>
    /// WaaSMedicSvc protects itself. The most compatible approach (no driver needed)
    /// is to set its "Start" registry value. Windows may restore it, but this works
    /// for most consumer builds without needing unsigned drivers.
    /// </summary>
    private static void LockWaasMedic(bool disable)
    {
        const string path = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WaaSMedicSvc";
        RegistryHelper.SetValue(path, "Start", disable ? 4 : 3,
            Microsoft.Win32.RegistryValueKind.DWord);
    }
}

public record UpdateStatus(bool ServicesDisabled, bool PolicyDisabled)
{
    public bool IsFullyDisabled => ServicesDisabled && PolicyDisabled;
}
