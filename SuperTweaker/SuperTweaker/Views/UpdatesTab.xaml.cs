using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SuperTweaker.Core;
using SuperTweaker.Modules.UpdateControl;

namespace SuperTweaker.Views;

public partial class UpdatesTab : UserControl
{
    private Logger?        _log;
    private UpdateManager? _manager;
    private bool           _initialized;

    public UpdatesTab() => InitializeComponent();

    public void Initialize(WindowsInfo info)
    {
        if (_initialized) return;
        _initialized = true;
        _log     = new Logger("updates-" + DateTime.Now.ToString("yyyyMMdd"));
        _log.OnLine += AppendLog;
        _manager = new UpdateManager(_log, info);
        OsScopeText.Text = info.IsWindows11
            ? "OS scope: Windows 11 update orchestration"
            : info.IsWindows10
                ? "OS scope: Windows 10 update orchestration"
                : "OS scope: generic Windows orchestration";
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        try
        {
            var status = _manager!.GetStatus();
            var res    = Application.Current.Resources;

            ServiceStatusBadge.Style = (Style)res[status.ServicesDisabled ? "BadgeInactive" : "BadgeActive"];
            ServiceStatusText.Foreground = status.ServicesDisabled
                ? (SolidColorBrush)res["AccentRedBrush"]
                : (SolidColorBrush)res["AccentGreenBrush"];
            ServiceStatusText.Text = status.ServicesDisabled ? "Disabled" : "Running";

            PolicyStatusBadge.Style = (Style)res[status.PolicyDisabled ? "BadgeInactive" : "BadgeActive"];
            PolicyStatusText.Foreground = status.PolicyDisabled
                ? (SolidColorBrush)res["AccentRedBrush"]
                : (SolidColorBrush)res["AccentGreenBrush"];
            PolicyStatusText.Text = status.PolicyDisabled ? "Blocking updates" : "Allowing updates";
        }
        catch (Exception ex)
        {
            AppendLog($"Status check error: {ex.Message}");
        }
    }

    private async void DisableBtn_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "This will disable Windows Update services, scheduled tasks, and registry policy.\n\n" +
            "Your system will NOT receive security patches while disabled.\n\nContinue?",
            "Disable Windows Update", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        SetBusy(true);
        try
        {
            await _manager!.DisableUpdatesAsync();
            AppendLog("✓ Windows Update disabled.");
        }
        catch (Exception ex) { AppendLog($"Error: {ex.Message}"); }
        finally
        {
            SetBusy(false);
            RefreshStatus();
        }
    }

    private async void EnableBtn_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            await _manager!.EnableUpdatesAsync();
            AppendLog("✓ Windows Update re-enabled.");
        }
        catch (Exception ex) { AppendLog($"Error: {ex.Message}"); }
        finally
        {
            SetBusy(false);
            RefreshStatus();
        }
    }

    private void RefreshStatus_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private void SetBusy(bool busy) => Dispatcher.InvokeAsync(() =>
    {
        DisableBtn.IsEnabled = !busy;
        EnableBtn.IsEnabled  = !busy;
    });

    private void AppendLog(string line) => Dispatcher.InvokeAsync(() =>
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\n");
        LogBox.ScrollToEnd();
    });
}
