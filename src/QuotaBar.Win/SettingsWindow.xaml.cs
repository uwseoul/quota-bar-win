using System.Windows;
using QuotaBar.Core.Models;
using QuotaBar.Core.Services;
using QuotaBar.Win.Converters;

namespace QuotaBar.Win;

public partial class SettingsWindow : Window
{
    public static readonly DependencyProperty FontScaleProperty = DependencyProperty.Register(
        nameof(FontScale),
        typeof(double),
        typeof(SettingsWindow),
        new PropertyMetadata(1d));

    private readonly SettingsService _settingsService = new();
    private AppSettings _settings = new();

    public double FontScale
    {
        get => (double)GetValue(FontScaleProperty);
        private set => SetValue(FontScaleProperty, value);
    }

    public SettingsWindow()
    {
        _settings = _settingsService.Load();
        FontScale = FontScaleConverter.Normalize(_settings.FontScale);
        InitializeComponent();
        MouseLeftButtonDown += (_, __) => DragMove();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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

        // View Mode
        ViewModeCombo.ItemsSource = Enum.GetNames(typeof(ViewMode));
        ViewModeCombo.SelectedItem = _settings.ViewMode.ToString();

        // Theme
        ThemeCombo.ItemsSource = new[] { "Auto", "Light", "Dark" };
        ThemeCombo.SelectedItem = _settings.Theme;

        FontScaleCombo.SelectedIndex = _settings.FontScale switch
        {
            1.25 => 1,
            1.5 => 2,
            _ => 0
        };

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

        if (ViewModeCombo.SelectedItem is string vmStr && Enum.TryParse<ViewMode>(vmStr, out var vm))
            _settings.ViewMode = vm;

        _settings.Theme = ThemeCombo.SelectedItem?.ToString() ?? "Auto";
        _settings.FontScale = FontScaleCombo.SelectedIndex switch
        {
            1 => 1.25,
            2 => 1.5,
            _ => 1.0
        };

        // Launch at Login
        var launchChanged = _settings.LaunchAtLogin != (LaunchAtLoginCheck.IsChecked == true);
        _settings.LaunchAtLogin = LaunchAtLoginCheck.IsChecked == true;
        if (launchChanged)
            UpdateStartup(_settings.LaunchAtLogin);

        if (int.TryParse(RefreshIntervalBox.Text, out var interval) && interval >= 10)
            _settings.RefreshIntervalSeconds = interval;

        _settingsService.Save(_settings);
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static void UpdateStartup(bool enable)
    {
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcutPath = System.IO.Path.Combine(startupFolder, "QuotaBar.lnk");

        if (enable)
        {
            try
            {
                // Use PowerShell to create a shortcut (.lnk file)
                var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{shortcutPath}'); $s.TargetPath = '{exePath}'; $s.Save()\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                });
                ps?.WaitForExit(5000);
            }
            catch { /* ignore */ }
        }
        else
        {
            try
            {
                if (System.IO.File.Exists(shortcutPath))
                    System.IO.File.Delete(shortcutPath);
            }
            catch { /* ignore */ }
        }
    }
}
