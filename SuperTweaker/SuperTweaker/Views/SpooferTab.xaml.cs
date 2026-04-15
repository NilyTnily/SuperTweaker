using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SuperTweaker.Core;
using SuperTweaker.Modules.Spoofer;

namespace SuperTweaker.Views;

public partial class SpooferTab : UserControl
{
    private Logger?      _log;
    private MacSpoofer?  _mac;
    private HwidSpoofer? _hwid;
    private SpooferBackupManager? _backupManager;
    private List<NicInfo> _nics = new();
    private bool _initialized;

    public SpooferTab() => InitializeComponent();

    public void Initialize(WindowsInfo info)
    {
        if (_initialized) return;
        _initialized = true;
        _log  = new Logger("spoofer-" + DateTime.Now.ToString("yyyyMMdd"));
        _log.OnLine += AppendLog;
        _mac  = new MacSpoofer(_log);
        _hwid = new HwidSpoofer(_log);
        _backupManager = new SpooferBackupManager(_log, _mac, _hwid);
        OsScopeText.Text = info.IsWindows11
            ? "OS scope: Windows 11 registry/HWID compatibility mode"
            : info.IsWindows10
                ? "OS scope: Windows 10 registry/HWID compatibility mode"
                : "OS scope: generic Windows compatibility mode";
        RefreshNics();
        RefreshHwidDisplay();
        RevertSpooferBackupBtn.IsEnabled = _backupManager.HasSnapshots();
    }

    // ──────────────── MAC ────────────────

    private void RefreshNics()
    {
        _nics = _mac!.GetAdapters();
        NicListPanel.Children.Clear();
        NicCombo.Items.Clear();

        if (_nics.Count == 0)
        {
            NicListPanel.Children.Add(MakeLabel("No physical adapters found.", "#FF4D6D"));
            return;
        }

        foreach (var nic in _nics)
        {
            var row = BuildNicRow(nic);
            NicListPanel.Children.Add(row);

            NicCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{nic.Name}  [{nic.CurrentMac}]",
                Tag     = nic   // store whole NicInfo
            });
        }

        if (NicCombo.Items.Count > 0) NicCombo.SelectedIndex = 0;
    }

    private UIElement BuildNicRow(NicInfo nic)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        for (int i = 0; i < 4; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition
                { Width = i == 0 ? new GridLength(2, GridUnitType.Star)
                        : i < 3  ? new GridLength(1, GridUnitType.Star)
                        : GridLength.Auto });

        var nameBlock = MakeText(nic.Name, 0, 11, wrap: true);
        var currBlock = MakeText(string.IsNullOrEmpty(nic.CurrentMac) ? "—" : nic.CurrentMac, 1, 11, "#7EE8A2");
        var regBlock  = MakeText(string.IsNullOrEmpty(nic.RegistryMac) ? "(hardware)" : nic.RegistryMac,
            2, 11, string.IsNullOrEmpty(nic.RegistryMac) ? null : "#FFD166");

        var badge = new Border
        {
            CornerRadius = new CornerRadius(8), Width = 60,
            Padding  = new Thickness(4, 2, 4, 2),
            Background = nic.IsActive
                ? new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x2A))
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x1A, 0x1A))
        };
        badge.Child = new TextBlock
        {
            Text = nic.IsActive ? "Active" : "Idle",
            FontSize = 10, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = nic.IsActive
                ? new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x97))
                : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xAA))
        };

        Grid.SetColumn(nameBlock, 0);
        Grid.SetColumn(currBlock, 1);
        Grid.SetColumn(regBlock,  2);
        Grid.SetColumn(badge,     3);
        row.Children.Add(nameBlock);
        row.Children.Add(currBlock);
        row.Children.Add(regBlock);
        row.Children.Add(badge);
        return row;
    }

    private void RandomMac_Click(object sender, RoutedEventArgs e)
        => NewMacBox.Text = MacSpoofer.GenerateRandomMac();

    private async void ApplyMac_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedNic(out var nic)) return;

        if (!MacSpoofer.IsValidMac(NewMacBox.Text))
        {
            AppendLog("Invalid MAC address format. Use XX:XX:XX:XX:XX:XX");
            return;
        }

        var ok = await _mac!.SpoofAsync(nic.AdapterGuid, nic.DeviceId, NewMacBox.Text);
        if (ok) RefreshNics();
    }

    private async void RestoreMac_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedNic(out var nic)) return;
        var ok = await _mac!.RestoreAsync(nic.AdapterGuid, nic.DeviceId);
        if (ok) RefreshNics();
    }

    private void RefreshNics_Click(object sender, RoutedEventArgs e) => RefreshNics();

    private void CreateSpooferBackup_Click(object sender, RoutedEventArgs e)
    {
        var path = _backupManager!.CreateSnapshot();
        AppendLog($"Backup snapshot saved: {path}");
        RevertSpooferBackupBtn.IsEnabled = _backupManager.HasSnapshots();
        MessageBox.Show("Custom spoofer snapshot created successfully.",
            "Backup Created", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RevertSpooferBackup_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Restore MAC + HWID values from latest custom spoofer snapshot?\nA reboot is recommended after restore.",
                "Restore Custom Snapshot",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        CreateSpooferBackupBtn.IsEnabled = false;
        RevertSpooferBackupBtn.IsEnabled = false;

        await _backupManager!.RestoreLatestSnapshotAsync();
        RefreshNics();
        RefreshHwidDisplay();
        CreateSpooferBackupBtn.IsEnabled = true;
        RevertSpooferBackupBtn.IsEnabled = _backupManager.HasSnapshots();
    }

    private bool TryGetSelectedNic(out NicInfo nic)
    {
        nic = null!;
        if (NicCombo.SelectedItem is ComboBoxItem { Tag: NicInfo n })
        {
            nic = n;
            return true;
        }
        AppendLog("No adapter selected.");
        return false;
    }

    // ──────────────── HWID ────────────────

    private void RefreshHwidDisplay()
    {
        var ids = _hwid!.GetCurrentIds();
        HwidGrid.Children.Clear();
        HwidGrid.RowDefinitions.Clear();
        HwidGrid.ColumnDefinitions.Clear();
        HwidGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        HwidGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        bool hasBackup = _hwid.HasBackup;
        RestoreHwidBtn.IsEnabled = hasBackup;

        int row = 0;
        foreach (var (key, val) in ids)
        {
            HwidGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = key, FontSize = 11, Margin = new Thickness(0, 3, 0, 3),
                Foreground = (SolidColorBrush)Application.Current.Resources["TextSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
            HwidGrid.Children.Add(lbl);

            var valBlock = new TextBlock
            {
                Text = val, FontSize = 11,
                FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
                Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"],
                Margin = new Thickness(0, 3, 0, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(valBlock, row); Grid.SetColumn(valBlock, 1);
            HwidGrid.Children.Add(valBlock);
            row++;
        }

        // Backup status indicator
        HwidGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var backupNote = new TextBlock
        {
            Text = hasBackup
                ? "✓  Original values backed up — restore available."
                : "No backup stored yet — will be created automatically on first spoof.",
            FontSize = 11,
            Foreground = hasBackup
                ? (SolidColorBrush)Application.Current.Resources["AccentGreenBrush"]
                : (SolidColorBrush)Application.Current.Resources["AccentOrangeBrush"],
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(backupNote, row);
        Grid.SetColumnSpan(backupNote, 2);
        HwidGrid.Children.Add(backupNote);
    }

    private async void SpoofHwid_Click(object sender, RoutedEventArgs e)
    {
        bool withName = RandomNameChk.IsChecked == true;
        var msg = "This will replace:\n  • MachineGuid\n  • HwProfileGuid\n  • SqmMachineId\n  • ProductId\n  • SusClientId"
                + (withName ? "\n  • Computer hostname" : "")
                + "\n\nOriginals are backed up. Reboot recommended.\n\nProceed?";

        if (MessageBox.Show(msg, "Spoof HWID", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        SpoofHwidBtn.IsEnabled   = false;
        RestoreHwidBtn.IsEnabled = false;
        var ok = await _hwid!.SpoofAsync(withName);
        RefreshHwidDisplay();
        SpoofHwidBtn.IsEnabled = true;
        if (!ok) AppendLog("Spoof may have partially failed — check log.");
    }

    private async void RestoreHwid_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Restore original HWID identifiers from backup?\nReboot required.",
                "Restore HWID", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        SpoofHwidBtn.IsEnabled   = false;
        RestoreHwidBtn.IsEnabled = false;
        await _hwid!.RestoreAsync();
        RefreshHwidDisplay();
        SpoofHwidBtn.IsEnabled = true;
    }

    // ──────────────── Helpers ────────────────

    private static UIElement MakeText(string text, int col, double size,
        string? hex = null, bool wrap = false)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = size,
            Foreground = hex != null
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
                : (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        if (wrap) tb.TextWrapping = TextWrapping.Wrap;
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static TextBlock MakeLabel(string text, string hex) => new()
    {
        Text       = text, FontSize = 12,
        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
    };

    private void AppendLog(string line) => Dispatcher.InvokeAsync(() =>
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\n");
        LogBox.ScrollToEnd();
    });
}
