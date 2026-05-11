namespace QuotaBar.Core.Models;

public class QuotaEntry
{
    public string Id { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double UsagePercent { get; set; } // 0.0 - 1.0
    public long? Usage { get; set; }
    public long? Total { get; set; }
    public int? ResetSeconds { get; set; }
    public int? TotalDurationSeconds { get; set; }

    public int DisplayPercent => (int)(UsagePercent * 100);

    public double? PaceRatio
    {
        get
        {
            if (TotalDurationSeconds == null || TotalDurationSeconds.Value <= 0) return null;
            var elapsedPercent = 1.0 - (ResetSeconds ?? 0) / (double)TotalDurationSeconds.Value;
            if (elapsedPercent <= 0) return null;
            return UsagePercent / elapsedPercent;
        }
    }

    public SpeedStatus SpeedStatus
    {
        get
        {
            var ratio = PaceRatio;
            if (ratio == null) return SpeedStatus.Normal;
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
                return $"Reset in {ts.Days}d {ts.Hours}h";
            return $"Reset in {ts.Hours}h {ts.Minutes}m";
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
