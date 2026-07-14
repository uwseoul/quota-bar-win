namespace QuotaBar.Core.Models;

public class AppSettings
{
    public GLMPlatform GlmPlatform { get; set; } = GLMPlatform.Zai;
    public string GlmApiKey { get; set; } = string.Empty;
    public string MiniMaxApiKey { get; set; } = string.Empty;
    public string OpenCodeGoWorkspaceId { get; set; } = string.Empty;
    public string OpenCodeGoAuthCookie { get; set; } = string.Empty;
    public string CodexAuthToken { get; set; } = string.Empty;
    public string CodexAccountId { get; set; } = string.Empty;

    public bool GlmEnabled { get; set; } = true;
    public bool MiniMaxEnabled { get; set; } = true;
    public bool CodexEnabled { get; set; } = true;
    public bool OpenCodeGoEnabled { get; set; } = true;

    public ViewMode ViewMode { get; set; } = ViewMode.Detail;
    public DisplayStyle DisplayStyle { get; set; } = DisplayStyle.Percent;
    public MenuBarMode MenuBarMode { get; set; } = MenuBarMode.Highest;

    public List<string> ManualSelectedIds { get; set; } = new();

    public bool AlwaysOnTop { get; set; } = true;
    public string Theme { get; set; } = "Auto"; // Auto, Light, Dark
    public bool LaunchAtLogin { get; set; } = false;

    public int RefreshIntervalSeconds { get; set; } = 300;
}
