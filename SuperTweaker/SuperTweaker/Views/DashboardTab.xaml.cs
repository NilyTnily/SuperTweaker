using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SuperTweaker.Core;

namespace SuperTweaker.Views;

public partial class DashboardTab : UserControl
{
    private WindowsInfo? _info;

    public DashboardTab() => InitializeComponent();

    public void Initialize(WindowsInfo info)
    {
        _info = info;
        PopulateSystemInfo(info);
        LoadRestorePoints();
    }

    private void PopulateSystemInfo(WindowsInfo info)
    {
        OsCaption.Text   = info.Caption;
        OsBuild.Text     = $"Build {info.Build}  •  Version {info.Version}";
        CpuName.Text     = info.CpuName;
        RamInfo.Text     = $"RAM: {info.TotalRamMb:N0} MB  ({info.TotalRamMb / 1024.0:F1} GB)";
        ArchInfo.Text    = info.Architecture;
        CpuCoreInfo.Text = $"Cores: {info.CpuPhysicalCores} physical / {info.CpuLogicalCores} logical";
        CpuClockInfo.Text = info.CpuMaxClockMhz > 0
            ? $"Max Clock: {info.CpuMaxClockMhz:N0} MHz"
            : "Max Clock: unavailable";
        DiskInfo.Text    = string.IsNullOrEmpty(info.DiskInfo) ? "No drives detected" : info.DiskInfo;
        GpuName.Text     = string.IsNullOrWhiteSpace(info.GpuName) ? "Unknown GPU" : info.GpuName;
        GpuVramInfo.Text = info.GpuVramMb > 0 ? $"VRAM: {info.GpuVramMb:N0} MB" : "VRAM: unavailable";
        GpuDriverInfo.Text = string.IsNullOrWhiteSpace(info.GpuDriverVersion)
            ? "Driver: unavailable"
            : $"Driver: {info.GpuDriverVersion}";
        WindowsFamilyInfo.Text = $"Windows family: {(info.IsWindows11 ? "11" : info.IsWindows10 ? "10" : "Unknown")}";
        EditionInfo.Text = $"Edition: {info.Edition}";

        var kind = string.IsNullOrWhiteSpace(info.MotherboardKind) ? "unavailable" : info.MotherboardKind;
        MotherboardKindText.Text = $"Kind: {kind}";
        var board = $"{info.MotherboardVendor} {info.MotherboardModel}".Trim();
        MotherboardProductText.Text = string.IsNullOrWhiteSpace(board) ? "unavailable" : board;
        MotherboardVersionLine.Text = FormatMotherboardDetail("Revision", info.MotherboardVersion);
        MotherboardSerialLine.Text = FormatMotherboardDetail("Serial", info.MotherboardSerialNumber);
        MotherboardPartLine.Text = FormatMotherboardDetail("Part number", info.MotherboardPartNumber);

        OsVersionText.Text = info.IsWindows11 ? "Windows 11" : (info.IsWindows10 ? "Windows 10" : "Unknown");
        OsEditionText.Text = info.Edition;

        SetBadge(AdminBadge,      AdminText,      info.IsElevated,
            "✓  Elevated", "✗  Not Admin");

        SetBadge(SecureBootBadge, SecureBootText, info.SecureBootEnabled,
            "✓  Enabled", "✗  Disabled", invert: false);

        // VBS ON is bad for performance → treat as "warning"
        SetVbsBadge(info.VbsEnabled);

        // Tamper Protection ON blocks registry edits → warn when enabled
        SetBadge(TamperBadge, TamperText,
            condition:   !info.TamperProtectionEnabled,  // good = NOT enabled
            trueMsg:     "✓  Off (OK)",
            falseMsg:    "⚠  On — may block tweaks",
            warnIfFalse: true);

        Log($"OS: {info.Caption} (Build {info.Build}) | {info.Edition} | {info.Architecture}");
        Log($"CPU: {info.CpuName}");
        Log($"CPU Cores: {info.CpuPhysicalCores}P / {info.CpuLogicalCores}L @ {info.CpuMaxClockMhz:N0} MHz");
        Log($"GPU: {GpuName.Text} ({GpuVramInfo.Text})");
        Log($"RAM: {info.TotalRamMb} MB");
        Log($"Motherboard: {kind} | {MotherboardProductText.Text}");
        if (!info.IsElevated)
            Log("WARNING: Not running as Administrator. Most tweaks will fail.");
    }

    private static string FormatMotherboardDetail(string label, string value)
    {
        var v = value?.Trim() ?? "";
        return string.IsNullOrEmpty(v) ? $"{label}: unavailable" : $"{label}: {v}";
    }

    private static void SetBadge(Border badge, TextBlock text, bool condition,
        string trueMsg, string falseMsg, bool invert = false, bool warnIfFalse = false)
    {
        var res = Application.Current.Resources;
        bool good = invert ? !condition : condition;

        badge.Style = (Style)res[good ? "BadgeActive" : (warnIfFalse ? "BadgeWarning" : "BadgeInactive")];

        text.Text = good ? trueMsg : falseMsg;
        text.Foreground = good
            ? (SolidColorBrush)res["AccentGreenBrush"]
            : warnIfFalse
                ? (SolidColorBrush)res["AccentOrangeBrush"]
                : (SolidColorBrush)res["AccentRedBrush"];
    }

    private void SetVbsBadge(bool vbsOn)
    {
        var res = Application.Current.Resources;
        if (vbsOn)
        {
            VbsBadge.Style = (Style)res["BadgeWarning"];
            VbsText.Text = "⚠  On — disabling boosts perf";
            VbsText.Foreground = (SolidColorBrush)res["AccentOrangeBrush"];
        }
        else
        {
            VbsBadge.Style = (Style)res["BadgeActive"];
            VbsText.Text = "✓  Off (optimal)";
            VbsText.Foreground = (SolidColorBrush)res["AccentGreenBrush"];
        }
    }

    private void LoadRestorePoints()
    {
        var pts = RestorePointManager.ListSuperTweakerPoints();
        if (pts.Count == 0)
        {
            RestorePointList.Text = "No SuperTweaker restore points found yet.";
            return;
        }
        var lines = pts.Select(p => $"• {p.Description}  —  {p.Date}");
        RestorePointList.Text = string.Join("\n", lines);
    }

    public void Log(string line)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ConsoleBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\n");
            ConsoleBox.ScrollToEnd();
        });
    }

    private void OpenBackupCenter_Click(object sender, RoutedEventArgs e)
        => RestorePointManager.OpenBackupAndRestore();

    private void OpenRestoreWizard_Click(object sender, RoutedEventArgs e)
        => RestorePointManager.OpenRestoreWizard();
}
