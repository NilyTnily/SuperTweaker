using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;
using Svg.Skia;
using SuperTweaker.Core;

namespace SuperTweaker.Views;

public partial class AppsTab : UserControl
{
    private enum IconSourceType { Raster, Svg }
    private enum AppOsTarget { Both, Win10Only, Win11Only }
    private record AppItem(
        string Name,
        string WingetId,
        string Category,
        string Icon = "•",
        bool DefaultChecked = false,
        AppOsTarget Os = AppOsTarget.Both,
        string? IconUrl = null);
    private record AppEntry(CheckBox Checkbox, AppItem App);

    private List<AppEntry> _entries = new();
    private static readonly HttpClient IconHttp = new();
    private static readonly SemaphoreSlim IconFetchGate = new(8, 8);
    private static readonly string IconCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SuperTweaker",
        "IconCache");
    private static readonly string DefaultFallbackIconPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Assets",
        "Icons",
        "default-app-icon.png");

    private WingetHelper?  _winget;
    private Logger?        _log;
    private WindowsInfo?   _osInfo;
    private bool           _initialized;

    public AppsTab() => InitializeComponent();

    public void Initialize(WindowsInfo info)
    {
        if (_initialized) return;
        _initialized = true;
        _osInfo = info;

        _log    = new Logger("apps-" + DateTime.Now.ToString("yyyyMMdd"));
        _log.OnLine += AppendLog;
        _winget = new WingetHelper(_log);
        CheckWinget();
        OsScopeText.Text = "Catalog scope: Full catalog (all apps shown)";
        LoadApps();
    }

    private void CheckWinget()
    {
        bool ok = WingetHelper.IsWingetAvailable();
        var res = Application.Current.Resources;
        WingetStatusBadge.Style = (Style)res[ok ? "BadgeActive" : "BadgeInactive"];
        WingetStatusText.Foreground = ok
            ? (SolidColorBrush)res["AccentGreenBrush"]
            : (SolidColorBrush)res["AccentRedBrush"];
        WingetStatusText.Text = ok ? "winget: ready" : "winget: not found";
        InstallBtn.IsEnabled  = ok;
    }

    private void LoadApps()
    {
        var catalogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "Data", "apps", "apps-catalog.json");

        List<AppItem>? apps = null;

        if (File.Exists(catalogPath))
        {
            try
            {
                var json = File.ReadAllText(catalogPath);
                apps = JsonSerializer.Deserialize<List<AppItem>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex) { AppendLog($"Catalog load error: {ex.Message}"); }
        }

        apps ??= GetBuiltInApps();
        var filtered = FilterByOs(apps);
        BuildUI(filtered);
        AppendLog($"Loaded {filtered.Count} catalog apps for current OS scope.");
    }

    private List<AppItem> FilterByOs(List<AppItem> apps)
    {
        // User requested full catalog visibility in Apps tab.
        return apps;
    }

    private void BuildUI(List<AppItem> apps)
    {
        CategoryPanel.Children.Clear();
        _entries.Clear();

        var categories = apps
            .OrderBy(a => a.Category)
            .ThenBy(a => a.Name)
            .GroupBy(a => a.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in categories)
        {
            var catCard = new Border
            {
                Style  = (Style)Application.Current.Resources["CardStyle"],
                Margin = new Thickness(0, 0, 0, 10)
            };
            var catStack = new StackPanel();

            var header = new TextBlock
            {
                Text  = $"{group.Key} ({group.Count()})",
                Style = (Style)Application.Current.Resources["SectionHeader"]
            };
            catStack.Children.Add(header);

            var appGrid = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var app in group.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var tile = CreateAppTile(app);
                var cb = (CheckBox)tile.Tag;
                appGrid.Children.Add(tile);
                _entries.Add(new AppEntry(cb, app));
            }

            catStack.Children.Add(appGrid);
            catCard.Child = catStack;
            CategoryPanel.Children.Add(catCard);
        }

        RefreshTileVisualState();
        UpdateSelectionSummary();
    }

    private Border CreateAppTile(AppItem app)
    {
        var check = new CheckBox
        {
            Visibility = Visibility.Collapsed,
            IsChecked = app.DefaultChecked
        };
        check.Checked += (_, _) => UpdateSelectionSummary();
        check.Unchecked += (_, _) => UpdateSelectionSummary();

        var iconImage = new Image
        {
            Width = 48,
            Height = 48,
            Margin = new Thickness(0, 2, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _ = LoadRealIconAsync(iconImage, app);
        var name = new TextBlock
        {
            Text = app.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"]
        };

        var selectedBadge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["AccentGreenBrush"],
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = "✓",
                FontSize = 10,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold
            },
            Visibility = app.DefaultChecked ? Visibility.Visible : Visibility.Collapsed
        };

        var grid = new Grid();
        grid.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { iconImage, name, check }
        });
        grid.Children.Add(selectedBadge);

        var tile = new Border
        {
            Width = 220,
            Height = 170,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            Background = (Brush)Application.Current.Resources["BgCardBrush"],
            Child = grid,
            Cursor = Cursors.Hand,
            ToolTip = $"{app.Name} — {app.Category}",
            Tag = check
        };

        tile.MouseLeftButtonUp += (_, _) =>
        {
            check.IsChecked = check.IsChecked != true;
            selectedBadge.Visibility = check.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            tile.BorderBrush = check.IsChecked == true
                ? (Brush)Application.Current.Resources["AccentBrush"]
                : (Brush)Application.Current.Resources["BorderBrush"];
            tile.Background = check.IsChecked == true
                ? (Brush)Application.Current.Resources["BgCardHoverBrush"]
                : (Brush)Application.Current.Resources["BgCardBrush"];
        };

        return tile;
    }

    private async Task LoadRealIconAsync(Image iconImage, AppItem app)
    {
        try
        {
            Directory.CreateDirectory(IconCacheDir);
            var cachedPath = GetCachedIconPath(app);
            if (File.Exists(cachedPath))
            {
                SetImageSourceFromFile(iconImage, cachedPath);
                return;
            }

            var candidates = ResolveIconCandidates(app);
            if (candidates.Count == 0) return;

            await IconFetchGate.WaitAsync();
            try
            {
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var bytes = await IconHttp.GetByteArrayAsync(candidate.Url);
                        if (bytes.Length == 0)
                            continue;

                        if (candidate.SourceType == IconSourceType.Svg)
                        {
                            bytes = RenderSvgToPng(bytes);
                            if (bytes.Length == 0)
                                continue;
                        }

                        await File.WriteAllBytesAsync(cachedPath, bytes);
                        SetImageSourceFromFile(iconImage, cachedPath);
                        return;
                    }
                    catch
                    {
                        // Try next candidate URL.
                    }
                }

                // Hard guarantee: every tile gets a valid icon even when vendor logos are unavailable.
                if (File.Exists(DefaultFallbackIconPath))
                    SetImageSourceFromFile(iconImage, DefaultFallbackIconPath);
            }
            finally
            {
                IconFetchGate.Release();
            }
        }
        catch
        {
            if (File.Exists(DefaultFallbackIconPath))
                SetImageSourceFromFile(iconImage, DefaultFallbackIconPath);
        }
    }

    private static void SetImageSourceFromFile(Image iconImage, string filePath)
    {
        iconImage.Dispatcher.Invoke(() =>
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(filePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                iconImage.Source = bmp;
                iconImage.Visibility = Visibility.Visible;
            }
            catch
            {
                iconImage.Visibility = Visibility.Collapsed;
            }
        });
    }

    private static string GetCachedIconPath(AppItem app)
    {
        var safe = app.WingetId.Replace(".", "_").Replace(" ", "_");
        return Path.Combine(IconCacheDir, $"{safe}.png");
    }

    private sealed record IconCandidate(string Url, IconSourceType SourceType);

    private static List<IconCandidate> ResolveIconCandidates(AppItem app)
    {
        var urls = new List<IconCandidate>();
        if (!string.IsNullOrWhiteSpace(app.IconUrl) && IsSupportedRasterIcon(app.IconUrl))
            urls.Add(new IconCandidate(app.IconUrl, IconSourceType.Raster));

        foreach (var slug in BuildIconSlugs(app))
        {
            // Preferred raster providers.
            urls.Add(new IconCandidate($"https://raw.githubusercontent.com/walkxcode/dashboard-icons/main/png/{slug}.png", IconSourceType.Raster));
            urls.Add(new IconCandidate($"https://cdn.jsdelivr.net/gh/walkxcode/dashboard-icons/png/{slug}.png", IconSourceType.Raster));

            // SVG brand providers, converted to PNG locally.
            urls.Add(new IconCandidate($"https://cdn.simpleicons.org/{slug}", IconSourceType.Svg));
            urls.Add(new IconCandidate($"https://cdn.simpleicons.org/{slug}/000000", IconSourceType.Svg));
        }

        // Domain-based logo providers to maximize per-app coverage.
        foreach (var domain in BuildDomainCandidates(app))
        {
            urls.Add(new IconCandidate($"https://logo.clearbit.com/{domain}", IconSourceType.Raster));
            urls.Add(new IconCandidate($"https://icon.horse/icon/{domain}", IconSourceType.Raster));
            urls.Add(new IconCandidate($"https://www.google.com/s2/favicons?domain={domain}&sz=128", IconSourceType.Raster));
        }

        return urls
            .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static byte[] RenderSvgToPng(byte[] svgBytes)
    {
        try
        {
            var svgText = System.Text.Encoding.UTF8.GetString(svgBytes);
            var svg = new SKSvg();
            var picture = svg.FromSvg(svgText);
            if (picture == null)
                return Array.Empty<byte>();

            var bounds = picture.CullRect;
            var targetSize = 96;
            var width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
            var height = Math.Max(1, (int)Math.Ceiling(bounds.Height));
            var scale = Math.Min((float)targetSize / width, (float)targetSize / height);

            var info = new SKImageInfo(targetSize, targetSize, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface == null)
                return Array.Empty<byte>();

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(
                (targetSize - width * scale) / 2f,
                (targetSize - height * scale) / 2f);
            canvas.Scale(scale, scale);
            canvas.DrawPicture(picture);
            canvas.Flush();

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray() ?? Array.Empty<byte>();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private static IEnumerable<string> BuildIconSlugs(AppItem app)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var s = value.Trim().ToLowerInvariant();
            s = s.Replace("&", "and");
            s = s.Replace("++", "plusplus");
            s = Regex.Replace(s, @"[\(\)\[\]\.,'/]", " ");
            s = Regex.Replace(s, @"\s+", "-");
            s = Regex.Replace(s, @"-+", "-").Trim('-');
            if (!string.IsNullOrWhiteSpace(s)) set.Add(s);
        }

        Add(app.Name);
        Add(app.WingetId.Split('.').LastOrDefault());
        Add(app.WingetId.Replace('.', '-'));

        // Known aliases where brand slugs differ from app names.
        foreach (var alias in app.WingetId switch
        {
            "Microsoft.VisualStudioCode" => new[] { "visual-studio-code", "vscode" },
            "OpenJS.NodeJS.LTS" => new[] { "nodejs", "node-js" },
            "Google.AndroidStudio" => new[] { "android-studio" },
            "Google.Drive" => new[] { "google-drive", "googledrive" },
            "Nvidia.GeForceExperience" => new[] { "nvidia", "geforce-experience" },
            "Microsoft.WindowsTerminal" => new[] { "windows-terminal" },
            "Microsoft.PowerShell" => new[] { "powershell" },
            "EpicGames.EpicGamesLauncher" => new[] { "epic-games", "epic-games-launcher" },
            "RedHat.Podman-Desktop" => new[] { "podman-desktop", "podman" },
            "Cloudflare.cloudflared" => new[] { "cloudflare", "cloudflared" },
            _ => Array.Empty<string>()
        })
        {
            Add(alias);
        }

        return set;
    }

    private static IEnumerable<string> BuildDomainCandidates(AppItem app)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wing = app.WingetId ?? string.Empty;
        var vendor = wing.Split('.').FirstOrDefault()?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(vendor))
            domains.Add($"{vendor}.com");

        // High-confidence domain overrides for better real-logo matching.
        foreach (var domain in wing switch
        {
            "Microsoft.VisualStudioCode" => new[] { "code.visualstudio.com", "microsoft.com" },
            "Microsoft.WindowsTerminal" => new[] { "microsoft.com" },
            "Microsoft.PowerShell" => new[] { "microsoft.com" },
            "Git.Git" => new[] { "git-scm.com" },
            "OpenJS.NodeJS.LTS" => new[] { "nodejs.org" },
            "Python.Python.3.12" => new[] { "python.org" },
            "7zip.7zip" => new[] { "7-zip.org" },
            "RARLab.WinRAR" => new[] { "rarlab.com" },
            "voidtools.Everything" => new[] { "voidtools.com" },
            "OBSProject.OBSStudio" => new[] { "obsproject.com" },
            "VideoLAN.VLC" => new[] { "videolan.org" },
            "OpenWhisperSystems.Signal" => new[] { "signal.org" },
            "Nvidia.GeForceExperience" => new[] { "nvidia.com" },
            "GOG.Galaxy" => new[] { "gog.com" },
            "ElectronicArts.EADesktop" => new[] { "ea.com" },
            "RiotGames.RiotClient" => new[] { "riotgames.com" },
            "CPUID.CPU-Z" => new[] { "cpuid.com" },
            "TechPowerUp.GPU-Z" => new[] { "techpowerup.com" },
            "REALiX.HWiNFO" => new[] { "hwinfo.com" },
            "CrystalDewWorld.CrystalDiskInfo" => new[] { "crystalmark.info" },
            "CrystalDewWorld.CrystalDiskMark" => new[] { "crystalmark.info" },
            "Resplendence.LatencyMon" => new[] { "resplendence.com" },
            "JetBrains.IntelliJIDEA.Community" => new[] { "jetbrains.com" },
            "JetBrains.PyCharm.Community" => new[] { "jetbrains.com" },
            "JetBrains.Rider" => new[] { "jetbrains.com" },
            "JetBrains.DataGrip" => new[] { "jetbrains.com" },
            "EclipseAdoptium.Temurin.21.JDK" => new[] { "adoptium.net" },
            "SublimeHQ.SublimeText.4" => new[] { "sublimetext.com" },
            "CIRT.net.Nikto" => new[] { "cirt.net" },
            "OWASP.ZAP" => new[] { "owasp.org" },
            "OWASP.DependencyCheck" => new[] { "owasp.org" },
            "PortSwigger.BurpSuite.Community" => new[] { "portswigger.net" },
            "Progress.Fiddler.Classic" => new[] { "telerik.com" },
            "Cloudflare.cloudflared" => new[] { "cloudflare.com" },
            "Cloudflare.Warp" => new[] { "cloudflare.com" },
            "ZeroTier.ZeroTierOne" => new[] { "zerotier.com" },
            "Henry++.simplewall" => new[] { "github.com" },
            "Openwall.JohnTheRipper" => new[] { "openwall.com" },
            "Hashcat.Hashcat" => new[] { "hashcat.net" },
            "Open-Shell.Open-Shell-Menu" => new[] { "github.com" },
            "valinet.ExplorerPatcher" => new[] { "github.com" },
            "cutter.re.cutter" => new[] { "cutter.re" },
            "WerWolv.ImHex" => new[] { "imhex.werwolv.net" },
            "VolatilityFoundation.Volatility3" => new[] { "volatilityfoundation.org" },
            "SleuthKit.Autopsy" => new[] { "sleuthkit.org" },
            "Microsoft.Sysinternals" => new[] { "learn.microsoft.com", "microsoft.com" },
            "Microsoft.Sysinternals.Autoruns" => new[] { "learn.microsoft.com", "microsoft.com" },
            "Microsoft.Sysinternals.ProcessMonitor" => new[] { "learn.microsoft.com", "microsoft.com" },
            "Microsoft.Sysinternals.ProcDump" => new[] { "learn.microsoft.com", "microsoft.com" },
            "Microsoft.Sysinternals.Sysmon" => new[] { "learn.microsoft.com", "microsoft.com" },
            _ => Array.Empty<string>()
        })
        {
            domains.Add(domain);
        }

        return domains;
    }

    private static bool IsSupportedRasterIcon(string url)
    {
        var lower = url.ToLowerInvariant();
        return lower.EndsWith(".png") ||
               lower.EndsWith(".jpg") ||
               lower.EndsWith(".jpeg") ||
               lower.EndsWith(".bmp") ||
               lower.EndsWith(".webp");
    }

    private async void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _entries.Where(x => x.Checkbox.IsChecked == true).ToList();
        if (selected.Count == 0) { MessageBox.Show("No apps selected."); return; }

        InstallBtn.IsEnabled = false;
        InstallProgress.Visibility = Visibility.Visible;
        InstallProgress.Maximum    = selected.Count;
        InstallProgress.Value      = 0;

        int done = 0;
        foreach (var entry in selected)
        {
            AppendLog($"Installing {entry.App.Name} ({entry.App.WingetId})...");
            bool ok = await _winget!.InstallAsync(entry.App.WingetId,
                line => AppendLog("  " + line));

            AppendLog(ok
                ? $"✓ {entry.App.Name} installed."
                : $"✗ {entry.App.Name} failed (may already be installed).");

            InstallProgress.Value = ++done;
        }

        AppendLog($"Done. {done}/{selected.Count} packages processed.");
        InstallBtn.IsEnabled = true;
        InstallProgress.Visibility = Visibility.Collapsed;
    }

    private async void UpgradeAll_Click(object sender, RoutedEventArgs e)
    {
        InstallBtn.IsEnabled = false;
        AppendLog("Upgrading all installed winget packages...");
        await _winget!.UpgradeAllAsync(line => AppendLog("  " + line));
        AppendLog("Upgrade complete.");
        InstallBtn.IsEnabled = true;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _entries.ForEach(x => x.Checkbox.IsChecked = true);
        RefreshTileVisualState();
        UpdateSelectionSummary();
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        _entries.ForEach(x => x.Checkbox.IsChecked = false);
        RefreshTileVisualState();
        UpdateSelectionSummary();
    }

    private void RefreshTileVisualState()
    {
        foreach (var child in CategoryPanel.Children.OfType<Border>())
        {
            if (child.Child is not StackPanel catStack) continue;
            var wrap = catStack.Children.OfType<WrapPanel>().FirstOrDefault();
            if (wrap == null) continue;
            foreach (var tile in wrap.Children.OfType<Border>())
            {
                if (tile.Tag is not CheckBox cb) continue;
                tile.BorderBrush = cb.IsChecked == true
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["BorderBrush"];
                tile.Background = cb.IsChecked == true
                    ? (Brush)Application.Current.Resources["BgCardHoverBrush"]
                    : (Brush)Application.Current.Resources["BgCardBrush"];

                if (tile.Child is Grid g && g.Children.Count > 1 && g.Children[1] is Border badge)
                    badge.Visibility = cb.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void UpdateSelectionSummary()
    {
        var selected = _entries.Count(x => x.Checkbox.IsChecked == true);
        SelectionSummaryText.Text = $"Selected: {selected} app{(selected == 1 ? "" : "s")}";
    }

    private void AppendLog(string line) => Dispatcher.InvokeAsync(() =>
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\n");
        LogBox.ScrollToEnd();
    });

    // Fallback if catalog json not found
    private static List<AppItem> GetBuiltInApps() => new()
    {
        new("Chrome", "Google.Chrome", "Browsers", "🌐", false),
        new("Firefox", "Mozilla.Firefox", "Browsers", "🦊"),
        new("Brave", "Brave.Brave", "Browsers", "🛡"),
        new("VS Code", "Microsoft.VisualStudioCode", "Dev Tools", "🧩", false),
        new("Git", "Git.Git", "Dev Tools", "🔧", false),
        new("7-Zip", "7zip.7zip", "Utilities", "📦", false),
        new("VLC", "VideoLAN.VLC", "Media", "🎬"),
        new("Discord", "Discord.Discord", "Communication", "💬"),
        new("OBS Studio", "OBSProject.OBSStudio", "Media", "📹"),
        new("Spotify", "Spotify.Spotify", "Media", "🎵"),
        new("Notepad++", "Notepad++.Notepad++", "Utilities", "📝"),
        new("CPU-Z", "CPUID.CPU-Z", "System Info", "🖥"),
        new("HWiNFO", "REALiX.HWiNFO", "System Info", "📊"),
        new("MSI Afterburner", "Guru3D.MSIAfterburner", "System Info", "🔥"),
        new("qBittorrent", "qBittorrent.qBittorrent", "Utilities", "⬇"),
        new("ShareX", "ShareX.ShareX", "Utilities", "📸"),
        new("Python 3", "Python.Python.3.12", "Dev Tools", "🐍"),
        new("Node.js LTS", "OpenJS.NodeJS.LTS", "Dev Tools", "🟢"),
        new("WinRAR", "RARLab.WinRAR", "Utilities", "🗜"),
        new("Steam", "Valve.Steam", "Gaming", "🎮"),
        new("GeForce Experience", "Nvidia.GeForceExperience", "Gaming", "🟩"),
    };
}
