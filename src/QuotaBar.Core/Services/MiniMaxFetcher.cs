using System.Net.Http.Headers;
using System.Text.Json;
using QuotaBar.Core.Models;

namespace QuotaBar.Core.Services;

public class MiniMaxFetcher : IUsageFetcher
{
    public string PlatformId => "minimax";

    private readonly HttpClient _client = new();

    public async Task<List<QuotaEntry>> FetchAsync(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.MiniMaxApiKey))
            throw new InvalidOperationException("MiniMax API Key is not configured.");

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", settings.MiniMaxApiKey.Trim());

        var entries = new List<QuotaEntry>();

        // Try both endpoints, use the one that returns valid coding plan data
        try
        {
            var response = await _client.GetAsync("https://api.minimax.io/v1/token_plan/remains");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("model_remains", out var modelRemains))
                {
                    foreach (var model in modelRemains.EnumerateArray())
                    {
                        var parsed = ParseModel(model);
                        if (parsed.Count > 0) entries.AddRange(parsed);
                    }
                }
            }
        }
        catch { /* ignore */ }

        // If first endpoint didn't yield results, try the second
        if (entries.Count == 0)
        {
            try
            {
                var response = await _client.GetAsync("https://api.minimax.io/v1/api/openplatform/coding_plan/remains");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("model_remains", out var modelRemains))
                    {
                        foreach (var model in modelRemains.EnumerateArray())
                        {
                            var parsed = ParseModel(model);
                            if (parsed.Count > 0) entries.AddRange(parsed);
                        }
                    }
                }
            }
            catch { /* ignore */ }
        }

        return entries;
    }

    private List<QuotaEntry> ParseModel(JsonElement model)
    {
        var entries = new List<QuotaEntry>();

        try
        {
            string? modelName = null;
            if (model.TryGetProperty("model_name", out var mn))
                modelName = mn.GetString();

            if (modelName == null ||
                (!modelName.Contains("coding", StringComparison.OrdinalIgnoreCase) &&
                 !modelName.Contains("minimax", StringComparison.OrdinalIgnoreCase)))
                return entries;

            long intervalTotal = 0;
            long intervalUsage = 0;
            long weeklyTotal = 0;
            long weeklyUsage = 0;
            long remainsTime = 0;
            long weeklyRemainsTime = 0;

            if (model.TryGetProperty("current_interval_total_count", out var itc))
                intervalTotal = itc.GetInt64();
            if (model.TryGetProperty("current_interval_usage_count", out var iuc))
                intervalUsage = iuc.GetInt64();
            if (model.TryGetProperty("current_weekly_total_count", out var wtc))
                weeklyTotal = wtc.GetInt64();
            if (model.TryGetProperty("current_weekly_usage_count", out var wuc))
                weeklyUsage = wuc.GetInt64();
            if (model.TryGetProperty("remains_time", out var rt))
                remainsTime = rt.GetInt64();
            if (model.TryGetProperty("weekly_remains_time", out var wrt))
                weeklyRemainsTime = wrt.GetInt64();

            if (intervalTotal > 0)
            {
                var percent = intervalTotal > 0 ? (double)intervalUsage / intervalTotal : 0;
                var resetSeconds = (int)(remainsTime / 1000);
                entries.Add(new QuotaEntry
                {
                    Id = $"minimax-5h",
                    PlatformId = "minimax",
                    Name = "5H",
                    UsagePercent = percent,
                    Usage = intervalUsage,
                    Total = intervalTotal,
                    ResetSeconds = resetSeconds,
                    TotalDurationSeconds = 5 * 60 * 60
                });
            }

            if (weeklyTotal > 0)
            {
                var percent = weeklyTotal > 0 ? (double)weeklyUsage / weeklyTotal : 0;
                var resetSeconds = (int)(weeklyRemainsTime / 1000);
                entries.Add(new QuotaEntry
                {
                    Id = $"minimax-weekly",
                    PlatformId = "minimax",
                    Name = "Weekly",
                    UsagePercent = percent,
                    Usage = weeklyUsage,
                    Total = weeklyTotal,
                    ResetSeconds = resetSeconds,
                    TotalDurationSeconds = 7 * 24 * 60 * 60
                });
            }
        }
        catch { /* ignore */ }

        return entries;
    }
}
