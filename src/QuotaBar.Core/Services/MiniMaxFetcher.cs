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

        var entries = new List<QuotaEntry>();
        var seenNames = new HashSet<string>();

        // Call both endpoints and merge unique quotas
        var endpoints = new[]
        {
            "https://api.minimax.io/v1/token_plan/remains",
            "https://api.minimax.io/v1/api/openplatform/coding_plan/remains"
        };

        foreach (var endpoint in endpoints)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", settings.MiniMaxApiKey.Trim());

                var response = await _client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    continue;

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("model_remains", out var modelRemains) &&
                    modelRemains.ValueKind == JsonValueKind.Array)
                {
                    foreach (var model in modelRemains.EnumerateArray())
                    {
                        foreach (var entry in ParseModel(model))
                        {
                            if (seenNames.Add(entry.Name))
                                entries.Add(entry);
                        }
                    }
                }
            }
            catch { /* ignore partial failure per endpoint */ }
        }

        if (entries.Count == 0)
            throw new InvalidOperationException("MiniMax: both endpoints failed or returned no usable quota data.");

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
            long intervalRemaining = 0;
            long weeklyTotal = 0;
            long weeklyRemaining = 0;
            long remainsTime = 0;
            long weeklyRemainsTime = 0;

            if (model.TryGetProperty("current_interval_total_count", out var itc))
                intervalTotal = itc.GetInt64();
            if (model.TryGetProperty("current_interval_usage_count", out var iuc))
                intervalRemaining = iuc.GetInt64();
            if (model.TryGetProperty("current_weekly_total_count", out var wtc))
                weeklyTotal = wtc.GetInt64();
            if (model.TryGetProperty("current_weekly_usage_count", out var wuc))
                weeklyRemaining = wuc.GetInt64();
            if (model.TryGetProperty("remains_time", out var rt))
                remainsTime = rt.GetInt64();
            if (model.TryGetProperty("weekly_remains_time", out var wrt))
                weeklyRemainsTime = wrt.GetInt64();

            // current_*_usage_count is actually REMAINING, not used
            if (intervalTotal > 0)
            {
                var intervalUsed = Math.Max(0, intervalTotal - intervalRemaining);
                var percent = intervalTotal > 0 ? (double)intervalUsed / intervalTotal : 0;
                var resetSeconds = (int)(remainsTime / 1000);
                entries.Add(new QuotaEntry
                {
                    Id = "minimax-5h",
                    PlatformId = "minimax",
                    Name = "5H",
                    UsagePercent = percent,
                    Usage = intervalUsed,
                    Total = intervalTotal,
                    ResetSeconds = resetSeconds,
                    TotalDurationSeconds = 5 * 60 * 60
                });
            }

            if (weeklyTotal > 0)
            {
                var weeklyUsed = Math.Max(0, weeklyTotal - weeklyRemaining);
                var percent = weeklyTotal > 0 ? (double)weeklyUsed / weeklyTotal : 0;
                var resetSeconds = (int)(weeklyRemainsTime / 1000);
                entries.Add(new QuotaEntry
                {
                    Id = "minimax-weekly",
                    PlatformId = "minimax",
                    Name = "Weekly",
                    UsagePercent = percent,
                    Usage = weeklyUsed,
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
