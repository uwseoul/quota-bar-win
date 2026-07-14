using System.Net.Http.Headers;
using System.Text.Json;
using QuotaBar.Core.Models;

namespace QuotaBar.Core.Services;

public class GlmFetcher : IUsageFetcher
{
    public string PlatformId => "glm";

    private readonly HttpClient _client = new();

    public async Task<List<QuotaEntry>> FetchAsync(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GlmApiKey))
            throw new InvalidOperationException("GLM API Key is not configured.");

        var baseUrl = settings.GlmPlatform == GLMPlatform.Zai
            ? "https://api.z.ai"
            : "https://open.bigmodel.cn";

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/api/monitor/usage/quota/limit");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", settings.GlmApiKey.Trim());

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var entries = new List<QuotaEntry>();

        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("limits", out var limits) &&
            limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in limits.EnumerateArray())
            {
                var entry = ParseLimit(item);
                if (entry != null)
                    entries.Add(entry);
            }
        }

        if (entries.Count == 0)
            throw new InvalidOperationException("GLM: no quota entries found in response.");

        return entries;
    }

    private QuotaEntry? ParseLimit(JsonElement item)
    {
        try
        {
            string? type = null;
            int? unit = null;
            long usage = 0;        // total limit
            long currentValue = 0; // consumed
            long remaining = 0;    // left
            int percentage = 0;
            long nextResetTime = 0;

            if (item.TryGetProperty("type", out var t))
                type = t.GetString();
            if (item.TryGetProperty("unit", out var u))
                unit = u.GetInt32();
            if (item.TryGetProperty("usage", out var us))
                usage = us.GetInt64();
            if (item.TryGetProperty("currentValue", out var cv))
                currentValue = cv.GetInt64();
            if (item.TryGetProperty("remaining", out var rem))
                remaining = rem.GetInt64();
            if (item.TryGetProperty("percentage", out var p))
                percentage = p.GetInt32();
            if (item.TryGetProperty("nextResetTime", out var rt))
                nextResetTime = rt.GetInt64();

            var name = MapGlmName(type, unit);
            // usage = total limit, currentValue = consumed, remaining = left
            var total = usage > 0 ? usage : currentValue + remaining;
            var percent = total > 0 ? (double)currentValue / total : percentage / 100.0;
            var resetSeconds = nextResetTime > 0
                ? (int)((nextResetTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000)
                : 0;
            var totalDuration = GetGlmDuration(type, unit);

            return new QuotaEntry
            {
                Id = $"glm-{type}-{unit}",
                PlatformId = "glm",
                Name = name,
                ModelName = type == "TIME_LIMIT" ? "MCP" : "GLM",
                UsagePercent = percent,
                Usage = currentValue,
                Total = total,
                ResetSeconds = Math.Max(0, resetSeconds),
                TotalDurationSeconds = totalDuration
            };
        }
        catch
        {
            return null;
        }
    }

    private static string MapGlmName(string? type, int? unit)
    {
        return (type, unit) switch
        {
            ("TIME_LIMIT", 5) => "Monthly MCP",
            ("TOKENS_LIMIT", 3) => "5 Hours Quota",
            ("TOKENS_LIMIT", 6) => "Weekly Quota",
            _ => $"{type} ({unit})"
        };
    }

    private static int GetGlmDuration(string? type, int? unit)
    {
        return (type, unit) switch
        {
            ("TOKENS_LIMIT", 3) => 5 * 60 * 60,      // 5 hours
            ("TOKENS_LIMIT", 6) => 7 * 24 * 60 * 60, // 1 week
            ("TIME_LIMIT", 5) => 30 * 24 * 60 * 60,  // 30 days
            _ => 24 * 60 * 60
        };
    }
}
