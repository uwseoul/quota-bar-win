using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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

    public MainWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, __) => DragMove();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(300)
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
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
}
