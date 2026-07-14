using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using QuotaBar.Core.Models;
using QuotaBar.Core.Services;
using QuotaBar.Win.Views;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace QuotaBar.Win;

public partial class MainWindow : Window
{
    private readonly UsageService _usageService = new();
    private readonly SettingsService _settingsService = new();
    private readonly DispatcherTimer _refreshTimer;
    private Dictionary<string, PlatformResult>? _lastResults;
    private ViewMode _currentViewMode = ViewMode.Detail;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"Quota Bar v{Assembly.GetExecutingAssembly().GetName().Version}";
        MouseLeftButtonDown += (_, __) => DragMove();
        StateChanged += MainWindow_StateChanged;

        var settings = _settingsService.Load();
        ApplySettings(settings);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(settings.RefreshIntervalSeconds)
        };
        _refreshTimer.Tick += async (_, __) => await LoadDataAsync();
        _refreshTimer.Start();

        Loaded += async (_, __) => await LoadDataAsync();
    }

    private void ApplySettings(AppSettings settings)
    {
        Topmost = settings.AlwaysOnTop;
        AlwaysOnTopToggle.IsChecked = settings.AlwaysOnTop;
        _currentViewMode = settings.ViewMode;
        ApplyViewMode(_currentViewMode);
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var results = await _usageService.FetchAllAsync();
            _lastResults = results;

            if (_currentViewMode == ViewMode.Simple)
                RefreshSimpleView(results);
            else if (_currentViewMode == ViewMode.Compact)
                RefreshCompactView(results);
            else
                RefreshDetailView(results);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshDetailView(Dictionary<string, PlatformResult> results)
    {
        CardsPanel.Children.Clear();

        foreach (var kvp in results)
        {
            // Skip disabled platforms
            if (kvp.Value.Error == "Disabled")
                continue;

            var card = new UsageCardView
            {
                PlatformName = kvp.Key.ToUpperInvariant(),
                Entries = kvp.Value.Entries
            };

            if (!string.IsNullOrEmpty(kvp.Value.Error))
            {
                card.Entries = new List<QuotaEntry>
                {
                    new()
                    {
                        Id = $"{kvp.Key}-error",
                        PlatformId = kvp.Key,
                        Name = kvp.Value.Error,
                        UsagePercent = 0,
                        Usage = null,
                        Total = null,
                        ResetSeconds = null,
                        TotalDurationSeconds = null
                    }
                };
            }

            CardsPanel.Children.Add(card);
        }
    }

    private void RefreshSimpleView(Dictionary<string, PlatformResult> results)
    {
        var settings = _settingsService.Load();
        var simpleItems = new List<SimpleItem>();

        foreach (var kvp in results)
        {
            // Skip disabled platforms
            if (kvp.Value.Error == "Disabled")
                continue;

            var platformName = kvp.Key switch
            {
                "glm" => "GLM",
                "minimax" => "MiniMax",
                "codex" => "Codex",
                "opencodego" => "OpenCode Go",
                _ => kvp.Key
            };

            if (!string.IsNullOrEmpty(kvp.Value.Error))
            {
                simpleItems.Add(new SimpleItem
                {
                    Platform = platformName,
                    PercentText = "Error",
                    Color = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x33, 0x33)),
                    DisplayStyle = settings.DisplayStyle
                });
                continue;
            }

            if (kvp.Value.Entries.Count == 0)
            {
                simpleItems.Add(new SimpleItem
                {
                    Platform = platformName,
                    PercentText = "N/A",
                    Color = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80)),
                    DisplayStyle = settings.DisplayStyle
                });
                continue;
            }

            // Apply MenuBarMode
            var visibleEntries = GetVisibleEntries(kvp.Value.Entries, kvp.Key, settings);

            foreach (var entry in visibleEntries)
            {
                simpleItems.Add(new SimpleItem
                {
                    Platform = platformName,
                    PercentText = $"{entry.DisplayPercent}%",
                    PercentValue = entry.DisplayPercent,
                    Color = SpeedStatusToBrush(entry.SpeedStatus),
                    DisplayStyle = settings.DisplayStyle
                });
            }
        }

        SimpleItems.ItemsSource = simpleItems;
    }

    private void RefreshCompactView(Dictionary<string, PlatformResult> results)
    {
        var settings = _settingsService.Load();
        var items = new List<CompactItem>();

        foreach (var kvp in results)
        {
            // Skip disabled platforms
            if (kvp.Value.Error == "Disabled")
                continue;

            if (!string.IsNullOrEmpty(kvp.Value.Error))
            {
                items.Add(new CompactItem
                {
                    ShortLabel = GetShortLabel(kvp.Key),
                    PercentText = "Err",
                    Color = SpeedStatusToBrush(null),
                    DisplayStyle = settings.DisplayStyle
                });
                continue;
            }

            if (kvp.Value.Entries.Count == 0)
            {
                items.Add(new CompactItem
                {
                    ShortLabel = GetShortLabel(kvp.Key),
                    PercentText = "N/A",
                    Color = SpeedStatusToBrush(null),
                    DisplayStyle = settings.DisplayStyle
                });
                continue;
            }

            var visibleEntries = GetVisibleEntries(kvp.Value.Entries, kvp.Key, settings);
            foreach (var entry in visibleEntries)
            {
                items.Add(new CompactItem
                {
                    ShortLabel = GetShortLabel(entry.Name),
                    PercentText = $"{entry.DisplayPercent}%",
                    PercentValue = entry.DisplayPercent,
                    Color = SpeedStatusToBrush(entry.SpeedStatus),
                    DisplayStyle = settings.DisplayStyle
                });
            }
        }

        CompactItems.ItemsSource = items;
    }

    /// <summary>
    /// Maps a quota/window name to the compact short label used by the macOS
    /// menu-bar rendering: 5 Hours→5H, Weekly→WK, Monthly→MO, 7D→7D,
    /// Review→RV, Rolling→RL, fallback→QT.
    /// </summary>
    private static string GetShortLabel(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "QT";

        var lower = name.ToLowerInvariant();
        if (lower.Contains("5") && (lower.Contains("hour") || lower.Contains("5h")))
            return "5H";
        if (lower.Contains("weekly") || lower.Contains("week"))
            return "WK";
        if (lower.Contains("monthly") || lower.Contains("month"))
            return "MO";
        if (lower.Contains("7d"))
            return "7D";
        if (lower.Contains("review"))
            return "RV";
        if (lower.Contains("rolling"))
            return "RL";
        return "QT";
    }

    private static System.Windows.Media.Brush SpeedStatusToBrush(SpeedStatus? status) => status switch
    {
        SpeedStatus.Fast => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x33, 0x33)),
        SpeedStatus.Normal => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xBF, 0x00)),
        SpeedStatus.Slow => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xCC, 0x66)),
        null => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80)),
        _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80))
    };

    private List<QuotaEntry> GetVisibleEntries(List<QuotaEntry> entries, string platformId, AppSettings settings)
    {
        if (entries.Count == 0) return entries;

        return settings.MenuBarMode switch
        {
            MenuBarMode.Highest => new List<QuotaEntry> { entries.MaxBy(e => e.UsagePercent) ?? entries[0] },
            MenuBarMode.First => new List<QuotaEntry> { entries[0] },
            MenuBarMode.OnePerPlatform => new List<QuotaEntry> { entries.MaxBy(e => e.UsagePercent) ?? entries[0] },
            MenuBarMode.Manual => entries.Where(e => settings.ManualSelectedIds.Contains(e.Id)).ToList() is { Count: > 0 } selected ? selected : new List<QuotaEntry> { entries[0] },
            _ => new List<QuotaEntry> { entries.MaxBy(e => e.UsagePercent) ?? entries[0] }
        };
    }

    private void ToggleMode_Click(object sender, RoutedEventArgs e)
    {
        _currentViewMode = _currentViewMode switch
        {
            ViewMode.Detail => ViewMode.Simple,
            ViewMode.Simple => ViewMode.Compact,
            _ => ViewMode.Detail
        };

        var settings = _settingsService.Load();
        settings.ViewMode = _currentViewMode;
        _settingsService.Save(settings);

        ApplyViewMode(_currentViewMode);

        if (_lastResults == null)
            return;

        if (_currentViewMode == ViewMode.Simple)
            RefreshSimpleView(_lastResults);
        else if (_currentViewMode == ViewMode.Compact)
            RefreshCompactView(_lastResults);
        else
            RefreshDetailView(_lastResults);
    }

    private void ApplyViewMode(ViewMode mode)
    {
        _currentViewMode = mode;

        var isCompact = mode == ViewMode.Compact;

        CloseButton.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        HeaderGrid.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        DetailScroll.Visibility = mode == ViewMode.Detail ? Visibility.Visible : Visibility.Collapsed;
        SimplePanel.Visibility = mode == ViewMode.Simple ? Visibility.Visible : Visibility.Collapsed;
        CompactPanel.Visibility = isCompact ? Visibility.Visible : Visibility.Collapsed;
        FooterPanel.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;

        if (isCompact)
        {
            MainBorder.Padding = new Thickness(2);
            MainBorder.CornerRadius = new CornerRadius(4);
            SizeToContent = SizeToContent.WidthAndHeight;
            Width = double.NaN;
        }
        else
        {
            MainBorder.Padding = new Thickness(16);
            MainBorder.CornerRadius = new CornerRadius(12);
            SizeToContent = SizeToContent.Height;
            Width = 320;
        }

        UpdateWindowOptionsVisibility();
        UpdateModeToggleToolTip();
    }

    private void UpdateWindowOptionsVisibility()
    {
        var showOptions = _currentViewMode == ViewMode.Detail && WindowState != WindowState.Minimized;
        AlwaysOnTopToggle.Visibility = showOptions ? Visibility.Visible : Visibility.Collapsed;
        MinimizeButton.Visibility = showOptions ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateWindowOptionsVisibility();
    }

    private void AlwaysOnTopToggle_Click(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Load();
        settings.AlwaysOnTop = AlwaysOnTopToggle.IsChecked == true;
        _settingsService.Save(settings);
        Topmost = settings.AlwaysOnTop;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void UpdateModeToggleToolTip()
    {
        var label = _currentViewMode switch
        {
            ViewMode.Detail => "Detail — click → Simple",
            ViewMode.Simple => "Simple — click → Compact",
            ViewMode.Compact => "Compact — click → Detail",
            _ => "Toggle View Mode"
        };
        ModeToggleButton.ToolTip = label;
        CompactToggle.ToolTip = label;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        settingsWindow.Owner = this;
        settingsWindow.Closed += async (_, __) =>
        {
            var settings = _settingsService.Load();
            ApplySettings(settings);
            _refreshTimer.Interval = TimeSpan.FromSeconds(settings.RefreshIntervalSeconds);
            await LoadDataAsync();
        };
        settingsWindow.Show();
    }

    private async void Updates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var currentVersion = GetVersion();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "QuotaBar-Win");
            var json = await http.GetStringAsync("https://api.github.com/repos/uwseoul/quota-bar-win/releases/latest");
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "v0.0.0";
            var latestVersion = tagName.TrimStart('v');

            if (IsNewerVersion(latestVersion, currentVersion))
            {
                var result = MessageBox.Show(
                    $"새 버전 {tagName}을(를) 사용할 수 있습니다.\n다운로드 페이지를 여시겠습니까?",
                    "업데이트 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo("https://github.com/uwseoul/quota-bar-win/releases/latest") { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show($"최신 버전입니다 (v{currentVersion}).", "업데이트 확인", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch
        {
            MessageBox.Show("업데이트 확인에 실패했습니다.", "업데이트 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string GetVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.1.0";
    }

    private static bool IsNewerVersion(string newer, string current)
    {
        var newParts = newer.Split('.').Select(p => int.TryParse(p, out var v) ? v : 0).ToArray();
        var curParts = current.Split('.').Select(p => int.TryParse(p, out var v) ? v : 0).ToArray();
        var maxLen = Math.Max(newParts.Length, curParts.Length);

        for (var i = 0; i < maxLen; i++)
        {
            var np = i < newParts.Length ? newParts[i] : 0;
            var cp = i < curParts.Length ? curParts[i] : 0;
            if (np > cp) return true;
            if (np < cp) return false;
        }
        return false;
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private class SimpleItem
    {
        public string Platform { get; set; } = "";
        public string PercentText { get; set; } = "";
        public int PercentValue { get; set; }
        public System.Windows.Media.Brush Color { get; set; } = System.Windows.Media.Brushes.Gray;
        public DisplayStyle DisplayStyle { get; set; } = DisplayStyle.Percent;

        public Visibility PercentVisible => DisplayStyle != DisplayStyle.Speed ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GraphVisible => DisplayStyle == DisplayStyle.Graph ? Visibility.Visible : Visibility.Collapsed;
    }

    private class CompactItem
    {
        public string ShortLabel { get; set; } = "QT";
        public string PercentText { get; set; } = "";
        public int PercentValue { get; set; }
        public System.Windows.Media.Brush Color { get; set; } = System.Windows.Media.Brushes.Gray;
        public DisplayStyle DisplayStyle { get; set; } = DisplayStyle.Percent;

        public int BlockWidth => DisplayStyle == DisplayStyle.Speed ? 18 : 28;

        public Visibility LabelVisible => DisplayStyle != DisplayStyle.Speed ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PercentVisible => DisplayStyle == DisplayStyle.Percent ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GraphVisible => DisplayStyle == DisplayStyle.Graph ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DotVisible => DisplayStyle == DisplayStyle.Speed ? Visibility.Visible : Visibility.Collapsed;
    }
}
