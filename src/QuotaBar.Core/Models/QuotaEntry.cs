namespace QuotaBar.Core.Models;

public class QuotaEntry
{
    public string Id { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public double UsagePercent { get; set; } // 0.0 - 1.0
    public long? Usage { get; set; }
    public long? Total { get; set; }
    public int? ResetSeconds { get; set; }
    public int? TotalDurationSeconds { get; set; }

    public int DisplayPercent => (int)(UsagePercent * 100);
    public string PercentDisplay => UsagePercent < 0 ? "N/A" : $"{DisplayPercent}%";

    public SpeedStatus SpeedStatus
    {
        get
        {
            if (UsagePercent < 0)
                return SpeedStatus.Normal;

            if (TotalDurationSeconds == null || TotalDurationSeconds.Value <= 0)
                return SpeedStatus.Normal;

            var elapsedPercent = 1.0 - (ResetSeconds ?? 0) / (double)TotalDurationSeconds.Value;

            // Window just started (< 5% elapsed) — judge by usage alone
            if (elapsedPercent <= 0.05)
            {
                return UsagePercent switch
                {
                    < 0.5 => SpeedStatus.Slow,
                    > 0.8 => SpeedStatus.Fast,
                    _ => SpeedStatus.Normal
                };
            }

            var ratio = UsagePercent / elapsedPercent;
            return ratio switch
            {
                > 1.2 => SpeedStatus.Fast,
                < 0.8 => SpeedStatus.Slow,
                _ => SpeedStatus.Normal
            };
        }
    }

    public string ResetDisplay
    {
        get
        {
            if (ResetSeconds == null) return string.Empty;
            var ts = TimeSpan.FromSeconds(ResetSeconds.Value);
            if (ts.TotalDays >= 1)
                return $"{ts.Days}d {ts.Hours}h";
            return $"{ts.Hours}h {ts.Minutes}m";
        }
    }

    public string UsageDisplay
    {
        get
        {
            if (Usage == null || Total == null) return string.Empty;
            return $"{FormatBytes(Usage.Value)}/{FormatBytes(Total.Value)}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000.0:F1}G";
        if (bytes >= 1_000_000) return $"{bytes / 1_000_000.0:F1}M";
        if (bytes >= 1_000) return $"{bytes / 1_000.0:F1}K";
        return bytes.ToString();
    }
}
