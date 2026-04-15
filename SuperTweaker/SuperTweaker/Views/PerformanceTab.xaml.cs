using System.Windows;
using System.Windows.Controls;
using SuperTweaker.Core;
using SuperTweaker.Modules.GoldenSetup;

namespace SuperTweaker.Views;

public partial class PerformanceTab : UserControl
{
    private Logger?                  _log;
    private TweakApplier?            _applier;
    private GoldenProfile?           _profile;
    private CancellationTokenSource? _cts;
    private bool                     _initialized;
    private WindowsInfo?             _osInfo;
    private WingetHelper?            _wingetHelper;
    private readonly List<(CheckBox CheckBox, Tweak Tweak)> _tweakEntries = new();

    public PerformanceTab() => InitializeComponent();

    public void Initialize(WindowsInfo info)
    {
        if (_initialized) return;
        _initialized = true;
        _osInfo = info;

        _log = new Logger(DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-perf");
        _log.OnLine += AppendLog;

        _applier     = new TweakApplier(_log, info);
        _wingetHelper = new WingetHelper(_log);
        _applier.OnProgress += UpdateProgress;
        OsScopeText.Text = info.IsWindows11
            ? "OS scope: Windows 11 profile + Win11-only tweaks enabled"
            : info.IsWindows10
                ? "OS scope: Windows 10 profile + Win10-only tweaks enabled"
                : "OS scope: generic profile fallback";

        _profile = _applier.LoadProfile();

        if (_profile == null)
        {
            AppendLog("ERROR: Profile could not be loaded. Check Data/profiles directory.");
            SetBusy(false, profileMissing: true);
        }
        else
        {
            AppendLog($"Profile: '{_profile.Name}'  •  {_profile.Tweaks.Count} tweaks loaded.");
            BuildTweakSelection();
        }

        RefreshRestoreLabel();
    }

    private void RefreshRestoreLabel()
    {
        var pts = RestorePointManager.ListSuperTweakerPoints();
        LastRestoreText.Text = pts.Count > 0
            ? $"Latest backup: {pts[^1].Description}  •  {pts[^1].Date}"
            : "No SuperTweaker restore point found yet.";
    }

    // ──────────────── Button Handlers ────────────────

    private async void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_profile == null || _applier == null) return;
        var selectedProfile = BuildSelectedProfile();
        if (selectedProfile == null) return;

        // Hard safety gate: ask for a system image workflow before any tweak apply.
        if (!ConfirmImageBackupBeforeApply()) return;

        SetBusy(true);

        AppendLog("─── Creating restore point before apply... ───");
        bool rp = await RestorePointManager.CreateAsync("SuperTweaker — before Golden Setup", _log);
        if (!rp)
            AppendLog("⚠  Restore point may have been throttled (Windows limits to 1 per 24 h). " +
                      "Continuing without — open Restore Wizard manually if you need one.");
        RefreshRestoreLabel();

        _cts = new CancellationTokenSource();
        var results = await _applier.ApplyAsync(selectedProfile, dryRun: false, _cts.Token);

        int ok   = results.Count(r => r.Success);
        int fail = results.Count - ok;
        AppendLog($"─── Apply complete: {ok}/{results.Count} succeeded, {fail} failed. ───");

        if (_osInfo != null && _wingetHelper != null && _log != null)
        {
            await PostApplyDebloat.RunAsync(_log, _osInfo, _wingetHelper,
                RunDebloatAfterApplyCheck.IsChecked == true, _cts.Token);

            await PostApplyHellzerg.RunAsync(_log, _osInfo, _wingetHelper,
                RunHellzergAfterApplyCheck.IsChecked == true, _cts.Token);
        }

        SetBusy(false);
    }

    private bool ConfirmImageBackupBeforeApply()
    {
        var decision = MessageBox.Show(
            "Before applying Golden Setup, create a full system image backup.\n\n" +
            "YES = open Windows image backup now\n" +
            "NO  = continue without image (not recommended)\n" +
            "CANCEL = abort apply",
            "Pre-Apply Safety Check",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (decision == MessageBoxResult.Cancel) return false;

        if (decision == MessageBoxResult.Yes)
        {
            RestorePointManager.OpenBackupAndRestore();
            var done = MessageBox.Show(
                "After you finish creating the image backup, click YES to continue.\n" +
                "Click NO to cancel apply.",
                "Image Backup Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return done == MessageBoxResult.Yes;
        }

        // NO = user explicitly accepts risk and continues.
        var confirmRisk = MessageBox.Show(
            "Continue without system image backup?\n\n" +
            "A restore point will still be created automatically.",
            "Continue Without Image Backup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return confirmRisk == MessageBoxResult.Yes;
    }

    private async void DryRunBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_profile == null || _applier == null) return;
        var selectedProfile = BuildSelectedProfile();
        if (selectedProfile == null) return;
        SetBusy(true);
        AppendLog("─── Starting DRY RUN — no changes will be made ───");

        _cts = new CancellationTokenSource();
        var results = await _applier.ApplyAsync(selectedProfile, dryRun: true, _cts.Token);

        int ok   = results.Count(r => r.Success);
        int fail = results.Count - ok;
        AppendLog($"─── Dry run: {ok}/{results.Count} tweaks valid, {fail} issues. ───");
        if (fail == 0)
            AppendLog("✓ All tweaks are valid and ready to apply.");

        // Revert dry-run as well
        var revertResults = await _applier.RevertAsync(selectedProfile, dryRun: true, _cts.Token);
        int revOk = revertResults.Count(r => r.Success);
        AppendLog($"─── Revert dry run: {revOk}/{revertResults.Count} tweaks have complete undo data. ───");

        SetBusy(false);
    }

    private async void RevertManifestBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_profile == null || _applier == null) return;
        var selectedProfile = BuildSelectedProfile();
        if (selectedProfile == null) return;

        var result = MessageBox.Show(
            "This surgically reverts every registry key, service, and scheduled task that was changed " +
            "by this tool.\n\nFor a full system rollback, use the 'Open Restore Wizard' button instead.\n\n" +
            "Continue with manifest-based undo?",
            "Revert Golden Setup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        SetBusy(true);
        _cts = new CancellationTokenSource();

        var results = await _applier.RevertAsync(selectedProfile, dryRun: false, _cts.Token);
        int ok = results.Count(r => r.Success);
        AppendLog($"─── Revert complete: {ok}/{results.Count} succeeded. ───");

        SetBusy(false);
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        AppendLog("─── Cancellation requested... ───");
    }

    private void OpenImageBackup_Click(object sender, RoutedEventArgs e)
        => RestorePointManager.OpenBackupAndRestore();

    private void OpenRestoreWizard_Click(object sender, RoutedEventArgs e)
        => RestorePointManager.OpenRestoreWizard();

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    private void SelectAllTweaks_Click(object sender, RoutedEventArgs e)
    {
        foreach (var entry in _tweakEntries) entry.CheckBox.IsChecked = true;
    }

    private void DeselectAllTweaks_Click(object sender, RoutedEventArgs e)
    {
        foreach (var entry in _tweakEntries) entry.CheckBox.IsChecked = false;
    }

    // ──────────────── Helpers ────────────────

    private void BuildTweakSelection()
    {
        if (_profile == null || _osInfo == null) return;
        _tweakEntries.Clear();
        TweakSelectionLeftPanel.Children.Clear();
        TweakSelectionRightPanel.Children.Clear();
        TweakDetailPanel.Children.Clear();

        var osTweaks = _profile.Tweaks.Where(t =>
            t.Os == TweakOs.Both ||
            (t.Os == TweakOs.Win11Only && _osInfo.IsWindows11) ||
            (t.Os == TweakOs.Win10Only && _osInfo.IsWindows10))
            .OrderBy(t => t.Risk switch
            {
                TweakRisk.Safe => 0,
                TweakRisk.Moderate => 1,
                _ => 2
            })
            .ThenBy(t => t.Name)
            .ToList();

        for (int i = 0; i < osTweaks.Count; i++)
        {
            var tweak = osTweaks[i];
            var check = new CheckBox
            {
                IsChecked = true,
                Content = $"✓  {tweak.Name}",
                Style = (Style)Application.Current.Resources["ModernCheckBox"],
                ToolTip = $"{tweak.Description}  |  Risk: {tweak.Risk}",
                Margin = new Thickness(0, 2, 0, 2)
            };

            var detail = new TextBlock
            {
                Text = $"• {tweak.Name}  |  {tweak.Risk}  |  {tweak.Description}",
                FontSize = 11,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextSecondaryBrush"],
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            if (i % 2 == 0) TweakSelectionLeftPanel.Children.Add(check);
            else TweakSelectionRightPanel.Children.Add(check);

            TweakDetailPanel.Children.Add(detail);
            _tweakEntries.Add((check, tweak));
        }

        var osLabel = _osInfo.IsWindows11 ? "Windows 11" : _osInfo.IsWindows10 ? "Windows 10" : "Current OS";
        TweakListHeaderText.Text = $"Golden Setup Tweaks ({osLabel})";
        TweakListInfoText.Text = $"{osTweaks.Count} OS-compatible tweaks loaded. Sorted by risk and name. Select what you want, leave the rest unchecked.";
    }

    private GoldenProfile? BuildSelectedProfile()
    {
        if (_profile == null) return null;
        var selected = _tweakEntries
            .Where(x => x.CheckBox.IsChecked == true)
            .Select(x => x.Tweak)
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show("Select at least one tweak first.", "No Tweaks Selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        return new GoldenProfile
        {
            Name = $"{_profile.Name} (selected: {selected.Count})",
            OsTarget = _profile.OsTarget,
            Tweaks = selected
        };
    }

    private void UpdateProgress(int current, int total, string label)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressLabel.Text       = label;
            ProgressBar.Value        = total > 0 ? (double)current / total * 100 : 0;
        });
    }

    private void SetBusy(bool busy, bool profileMissing = false)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ApplyBtn.IsEnabled              = !busy && !profileMissing;
            DryRunBtn.IsEnabled             = !busy && !profileMissing;
            RevertManifestBtn.IsEnabled     = !busy && !profileMissing;
            RunDebloatAfterApplyCheck.IsEnabled   = !busy && !profileMissing;
            RunHellzergAfterApplyCheck.IsEnabled  = !busy && !profileMissing;
            CancelBtn.IsEnabled             = busy;
            ProgressPanel.Visibility    = busy ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void AppendLog(string line) => Dispatcher.InvokeAsync(() =>
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\n");
        LogBox.ScrollToEnd();
    });
}
