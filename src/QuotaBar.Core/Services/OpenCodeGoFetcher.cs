using System.Net.Http;
using System.Text.RegularExpressions;
using QuotaBar.Core.Models;

namespace QuotaBar.Core.Services;

public class OpenCodeGoFetcher : IUsageFetcher
{
    public string PlatformId => "opencodego";

    private readonly HttpClient _client = new();

    public async Task<List<QuotaEntry>> FetchAsync(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenCodeGoWorkspaceId) ||
            string.IsNullOrWhiteSpace(settings.OpenCodeGoAuthCookie))
            throw new InvalidOperationException("OpenCode Go Workspace ID and Auth Cookie are not configured.");

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://opencode.ai/workspace/{settings.OpenCodeGoWorkspaceId.Trim()}/go");
        request.Headers.Add("Cookie", $"auth={settings.OpenCodeGoAuthCookie.Trim()}");

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        var entries = new List<QuotaEntry>();

        var rolling = ExtractUsage(html, "rollingUsage");
        if (rolling != null)
        {
            entries.Add(new QuotaEntry
            {
                Id = "opencodego-rolling",
                PlatformId = "opencodego",
                Name = "Rolling",
                UsagePercent = rolling.Value.Percent / 100.0,
                Usage = null,
                Total = null,
                ResetSeconds = rolling.Value.ResetSeconds,
                TotalDurationSeconds = 5 * 60 * 60
            });
        }

        var weekly = ExtractUsage(html, "weeklyUsage");
        if (weekly != null)
        {
            entries.Add(new QuotaEntry
            {
                Id = "opencodego-weekly",
                PlatformId = "opencodego",
                Name = "Weekly",
                UsagePercent = weekly.Value.Percent / 100.0,
                Usage = null,
                Total = null,
                ResetSeconds = weekly.Value.ResetSeconds,
                TotalDurationSeconds = 7 * 24 * 60 * 60
            });
        }

        var monthly = ExtractUsage(html, "monthlyUsage");
        if (monthly != null)
        {
            entries.Add(new QuotaEntry
            {
                Id = "opencodego-monthly",
                PlatformId = "opencodego",
                Name = "Monthly",
                UsagePercent = monthly.Value.Percent / 100.0,
                Usage = null,
                Total = null,
                ResetSeconds = monthly.Value.ResetSeconds,
                TotalDurationSeconds = 30 * 24 * 60 * 60
            });
        }

        if (entries.Count == 0)
            throw new InvalidOperationException("Could not parse OpenCode Go usage data from HTML response.");

        return entries;
    }

    private (int Percent, int ResetSeconds)? ExtractUsage(string html, string label)
    {
        try
        {
            // Extract the block: label: { ... }
            var blockPattern = $"{label}\\s*:\\s*(?:\\$R\\[\\d+\\]\\s*=\\s*)?\\{{([^}}]+)\\}}";
            var blockMatch = Regex.Match(html, blockPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!blockMatch.Success)
                return null;

            var block = blockMatch.Groups[1].Value;

            // Order-independent extraction of resetInSec and usagePercent
            var resetMatch = Regex.Match(block, @"resetInSec\s*:\s*(\d+)");
            var percentMatch = Regex.Match(block, @"usagePercent\s*:\s*(\d+)");

            if (resetMatch.Success && percentMatch.Success)
            {
                var resetSec = int.Parse(resetMatch.Groups[1].Value);
                var percent = int.Parse(percentMatch.Groups[1].Value);
                return (percent, resetSec);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
