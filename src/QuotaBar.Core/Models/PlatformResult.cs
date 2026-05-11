namespace QuotaBar.Core.Models;

public class PlatformResult
{
    public List<QuotaEntry> Entries { get; set; } = new();
    public string? Error { get; set; }
    public bool IsLoading { get; set; }
}
