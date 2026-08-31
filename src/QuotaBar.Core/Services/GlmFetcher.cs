using System.Globalization;
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
        var level = ReadLevel(doc);

        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("limits", out var limits) &&
            limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in limits.EnumerateArray())
            {
                var entry = ParseLimit(item, level);
                if (entry != null)
                    entries.Add(entry);
            }
        }

        if (entries.Count == 0)
            throw new InvalidOperationException("GLM: no quota entries found in response.");

        return entries;
    }

    // Plan tier from data.level ("lite" / "pro" / "max")
    private static string? ReadLevel(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("level", out var l) &&
            l.ValueKind == JsonValueKind.String)
        {
            var level = l.GetString()?.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(level) ? null : level;
        }
        return null;
    }

    private QuotaEntry? ParseLimit(JsonElement item, string? level)
    {
        try
        {
            string? type = null;
            int? unit = null;
            double percentage = 0;
            long nextResetTime = 0;

            if (item.TryGetProperty("type", out var t) &&
                t.ValueKind == JsonValueKind.String)
            {
                type = t.GetString();
            }

            if (item.TryGetProperty("unit", out var u) &&
                u.ValueKind == JsonValueKind.Number)
            {
                unit = (int)u.GetDouble();
            }

            // usage = total limit, currentValue = consumed, remaining = left
            // (only TIME_LIMIT / MCP entries carry absolute values; token/credit
            // buckets report percentage only)
            var usage = ReadInt64(item, "usage");
            var currentValue = ReadInt64(item, "currentValue");
            var remaining = ReadInt64(item, "remaining");

            if (item.TryGetProperty("percentage", out var p) &&
                p.ValueKind == JsonValueKind.Number)
            {
                percentage = p.GetDouble();
            }

            // nextResetTime is usually epoch milliseconds, but ISO-8601 strings
            // have been observed in the wild — accept both.
            if (item.TryGetProperty("nextResetTime", out var rt))
            {
                if (rt.ValueKind == JsonValueKind.Number)
                {
                    nextResetTime = (long)rt.GetDouble();
                }
                else if (rt.ValueKind == JsonValueKind.String &&
                         DateTimeOffset.TryParse(
                             rt.GetString(),
                             CultureInfo.InvariantCulture,
                             DateTimeStyles.None,
                             out var parsed))
                {
                    nextResetTime = parsed.ToUnixTimeMilliseconds();
                }
            }

            // Credit-based plans (introduced 2026-07-30) report CREDIT_LIMIT
            // instead of TOKENS_LIMIT for the same 5h/weekly windows.
            var normalized = NormalizeType(type);

            var name = MapGlmName(normalized, unit);
            var total = usage is > 0
                ? usage
                : currentValue.HasValue && remaining.HasValue
                    ? currentValue.Value + remaining.Value
                    : null;
            var percent = total is > 0 && currentValue.HasValue
                ? (double)currentValue.Value / total.Value
                : percentage / 100.0;
            var resetSeconds = nextResetTime > 0
                ? (int)((nextResetTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000)
                : 0;
            var totalDuration = GetGlmDuration(normalized, unit);

            return new QuotaEntry
            {
                Id = $"glm-{normalized}-{unit}",
                PlatformId = "glm",
                Name = name,
                ModelName = normalized == "TIME_LIMIT"
                    ? "MCP"
                    : string.IsNullOrEmpty(level) ? "GLM" : $"GLM {Capitalize(level)}",
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

    private static string? NormalizeType(string? type)
    {
        var upper = type?.Trim().ToUpperInvariant();
        return upper == "CREDIT_LIMIT" ? "TOKENS_LIMIT" : upper;
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value.Substring(1);

    private static long? ReadInt64(JsonElement item, string name)
    {
        if (item.TryGetProperty(name, out var v) &&
            v.ValueKind == JsonValueKind.Number)
        {
            return (long)v.GetDouble();
        }
        return null;
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
