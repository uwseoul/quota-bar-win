using System.Windows;
using QuotaBar.Core.Models;
using QuotaBar.Core.Services;

namespace QuotaBar.Win;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings = new();

    public SettingsWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, __) => DragMove();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsService.Load();

        // GLM Platform
        GlmZaiRadio.IsChecked = _settings.GlmPlatform == GLMPlatform.Zai;
        GlmBigmodelRadio.IsChecked = _settings.GlmPlatform == GLMPlatform.Bigmodel;
        GlmApiKeyBox.Text = _settings.GlmApiKey;
        MiniMaxApiKeyBox.Text = _settings.MiniMaxApiKey;
        OpenCodeGoWorkspaceBox.Text = _settings.OpenCodeGoWorkspaceId;
        OpenCodeGoCookieBox.Text = _settings.OpenCodeGoAuthCookie;
        CodexTokenBox.Text = _settings.CodexAuthToken;
        CodexAccountIdBox.Text = _settings.CodexAccountId;

        // Platforms
        GlmCheck.IsChecked = _settings.GlmEnabled;
        MiniMaxCheck.IsChecked = _settings.MiniMaxEnabled;
        CodexCheck.IsChecked = _settings.CodexEnabled;
        OpenCodeGoCheck.IsChecked = _settings.OpenCodeGoEnabled;

        // Menu Bar Mode
        MenuBarModeCombo.ItemsSource = Enum.GetNames(typeof(MenuBarMode));
        MenuBarModeCombo.SelectedItem = _settings.MenuBarMode.ToString();

        // Display Style
        DisplayStyleCombo.ItemsSource = Enum.GetNames(typeof(DisplayStyle));
        DisplayStyleCombo.SelectedItem = _settings.DisplayStyle.ToString();

        // Always on Top
        AlwaysOnTopCheck.IsChecked = _settings.AlwaysOnTop;

        // Theme
        ThemeCombo.ItemsSource = new[] { "Auto", "Light", "Dark" };
        ThemeCombo.SelectedItem = _settings.Theme;

        // Refresh Interval
        RefreshIntervalBox.Text = _settings.RefreshIntervalSeconds.ToString();

        // Launch at Login
        LaunchAtLoginCheck.IsChecked = _settings.LaunchAtLogin;
    }

    private void GlmPlatform_Checked(object sender, RoutedEventArgs e)
    {
        if (GlmZaiRadio.IsChecked == true)
            _settings.GlmPlatform = GLMPlatform.Zai;
        else if (GlmBigmodelRadio.IsChecked == true)
            _settings.GlmPlatform = GLMPlatform.Bigmodel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.GlmApiKey = GlmApiKeyBox.Text;
        _settings.MiniMaxApiKey = MiniMaxApiKeyBox.Text;
        _settings.OpenCodeGoWorkspaceId = OpenCodeGoWorkspaceBox.Text;
        _settings.OpenCodeGoAuthCookie = OpenCodeGoCookieBox.Text;
        _settings.CodexAuthToken = CodexTokenBox.Text;
        _settings.CodexAccountId = CodexAccountIdBox.Text;
        _settings.GlmEnabled = GlmCheck.IsChecked == true;
        _settings.MiniMaxEnabled = MiniMaxCheck.IsChecked == true;
        _settings.CodexEnabled = CodexCheck.IsChecked == true;
        _settings.OpenCodeGoEnabled = OpenCodeGoCheck.IsChecked == true;

        if (MenuBarModeCombo.SelectedItem is string modeStr && Enum.TryParse<MenuBarMode>(modeStr, out var mode))
            _settings.MenuBarMode = mode;

        if (DisplayStyleCombo.SelectedItem is string styleStr && Enum.TryParse<DisplayStyle>(styleStr, out var style))
            _settings.DisplayStyle = style;

        _settings.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        _settings.Theme = ThemeCombo.SelectedItem?.ToString() ?? "Auto";
        _settings.LaunchAtLogin = LaunchAtLoginCheck.IsChecked == true;

        if (int.TryParse(RefreshIntervalBox.Text, out var interval) && interval >= 10)
            _settings.RefreshIntervalSeconds = interval;

        _settingsService.Save(_settings);
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
