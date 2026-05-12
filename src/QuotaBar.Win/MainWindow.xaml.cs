using System.Collections.Generic;
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

    public MainWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, __) => DragMove();

        var settings = _settingsService.Load();
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(settings.RefreshIntervalSeconds)
        };
        _refreshTimer.Tick += async (_, __) => await LoadDataAsync();
        _refreshTimer.Start();

        Loaded += async (_, __) => await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var results = await _usageService.FetchAllAsync();
            _lastResults = results;

            if (SimpleModeToggle.IsChecked == true)
            {
                RefreshSimpleView(results);
            }
            else
            {
                RefreshDetailView(results);
            }
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
        var simpleItems = new List<SimpleItem>();

        foreach (var kvp in results)
        {
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
                    Color = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x33, 0x33))
                });
                continue;
            }

            if (kvp.Value.Entries.Count == 0)
            {
                simpleItems.Add(new SimpleItem
                {
                    Platform = platformName,
                    PercentText = "N/A",
                    Color = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80))
                });
                continue;
            }

            // Pick the entry with highest usage percent as representative
            var topEntry = kvp.Value.Entries[0];
            foreach (var entry in kvp.Value.Entries)
            {
                if (entry.UsagePercent > topEntry.UsagePercent)
                    topEntry = entry;
            }

            var color = topEntry.SpeedStatus switch
            {
                SpeedStatus.Fast => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x33, 0x33)),
                SpeedStatus.Normal => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xBF, 0x00)),
                SpeedStatus.Slow => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xCC, 0x66)),
                _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80))
            };

            simpleItems.Add(new SimpleItem
            {
                Platform = platformName,
                PercentText = $"{topEntry.DisplayPercent}%",
                Color = color
            });
        }

        SimpleItems.ItemsSource = simpleItems;
    }

    private void ToggleMode_Click(object sender, RoutedEventArgs e)
    {
        DetailScroll.Visibility = SimpleModeToggle.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        SimplePanel.Visibility = SimpleModeToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (_lastResults != null)
        {
            if (SimpleModeToggle.IsChecked == true)
                RefreshSimpleView(_lastResults);
            else
                RefreshDetailView(_lastResults);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        settingsWindow.Owner = this;
        settingsWindow.Closed += (_, __) =>
        {
            var settings = _settingsService.Load();
            _refreshTimer.Interval = TimeSpan.FromSeconds(settings.RefreshIntervalSeconds);
        };
        settingsWindow.Show();
    }

    private void Updates_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("You are on the latest version.", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
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
        public System.Windows.Media.Brush Color { get; set; } = System.Windows.Media.Brushes.Gray;
    }
}
