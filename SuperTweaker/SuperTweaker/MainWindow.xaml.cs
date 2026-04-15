using System.Windows;
using System.Windows.Input;
using SuperTweaker.Core;

namespace SuperTweaker;

public partial class MainWindow : Window
{
    private WindowsInfo? _info;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _info = WindowsInfo.Get();
        OsBadgeText.Text = _info.IsWindows11 ? "Windows 11"
                         : _info.IsWindows10 ? "Windows 10"
                         : "Windows";

        TabDashboard.Initialize(_info);
        TabPerformance.Initialize(_info);
        TabApps.Initialize(_info);
        TabUpdates.Initialize(_info);
        TabSpoofer.Initialize(_info);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
        // WPF may fire Checked while InitializeComponent is still wiring fields.
        if (!IsLoaded) return;
        if (sender is not System.Windows.Controls.RadioButton rb) return;
        if (TabDashboard == null || TabPerformance == null || TabApps == null || TabUpdates == null || TabSpoofer == null)
            return;

        var tag = rb.Tag?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(tag)) return;

        try
        {
            TabDashboard.Visibility   = tag == "Dashboard"   ? Visibility.Visible : Visibility.Collapsed;
            TabPerformance.Visibility = tag == "Performance" ? Visibility.Visible : Visibility.Collapsed;
            TabApps.Visibility        = tag == "Apps"        ? Visibility.Visible : Visibility.Collapsed;
            TabUpdates.Visibility     = tag == "Updates"     ? Visibility.Visible : Visibility.Collapsed;
            TabSpoofer.Visibility     = tag == "Spoofer"     ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            // Defensive: never crash the whole app from nav state during initialization races.
        }
    }
}
