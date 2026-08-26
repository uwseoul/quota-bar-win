using System.Net.Http.Headers;
using System.Text.Json;
using QuotaBar.Core.Models;

namespace QuotaBar.Core.Services;

public class OpenRouterFetcher : IUsageFetcher
{
    public string PlatformId => "openrouter";

    private readonly HttpClient _client = new();

    public async Task<List<QuotaEntry>> FetchAsync(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
            throw new InvalidOperationException("OpenRouter API Key is not configured.");

        var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/key");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenRouterApiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("User-Agent", "QuotaBar-Win");

        var response = await _client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var reason = response.ReasonPhrase?.Trim() ?? "Unknown";
            throw new InvalidOperationException($"OpenRouter HTTP {(int)response.StatusCode} {reason}\n{body[..Math.Min(body.Length, 300)]}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("OpenRouter: missing 'data' in response.");

        double? limit = GetDoubleOrNull(data, "limit");
        double? limitRemaining = GetDoubleOrNull(data, "limit_remaining");
        double? usage = GetDoubleOrNull(data, "usage");
        // Fallbacks: if usage is null, try usage_daily/monthly
        usage ??= GetDoubleOrNull(data, "usage_daily") ?? GetDoubleOrNull(data, "usage_monthly") ?? 0;

        string? label = data.TryGetProperty("label", out var labelEl) && labelEl.ValueKind == JsonValueKind.String ? labelEl.GetString() : null;
        string? limitReset = data.TryGetProperty("limit_reset", out var lrEl) && lrEl.ValueKind == JsonValueKind.String ? lrEl.GetString() : null;
        bool isFreeTier = data.TryGetProperty("is_free_tier", out var ftEl) && ftEl.ValueKind == JsonValueKind.True;

        // Compute percent: prefer limit, fallback to usage+remaining
        double percent;

        if (limit.HasValue && limit.Value > 0)
        {
            percent = usage!.Value / limit.Value;
        }
        else if (limitRemaining.HasValue && usage.HasValue && (usage.Value + limitRemaining.Value) > 0)
        {
            var total = usage.Value + limitRemaining.Value;
            percent = usage.Value / total;
        }
        else
        {
            percent = -1; // N/A - unlimited / no limit set
        }

        percent = percent >= 0 ? Math.Clamp(percent, 0, 1) : -1;

        string name;
        if (isFreeTier)
            name = "Free Tier";
        else if (limit.HasValue && limit.Value > 0)
            name = limitReset switch
            {
                "daily" => "Daily",
                "weekly" => "Weekly",
                "monthly" => "Monthly",
                _ when !string.IsNullOrEmpty(limitReset) => char.ToUpper(limitReset![0]) + limitReset[1..],
                _ => "Credits"
            };
        else
            name = "Usage";

        // ModelName shows dollar amount, e.g. "$25.50 / $100.00" or "$12.34"
        string dollarDisplay;
        if (limit.HasValue && limit.Value > 0)
            dollarDisplay = $"${usage!.Value:F2} / ${limit.Value:F2}";
        else if (limitRemaining.HasValue)
            dollarDisplay = $"${usage!.Value:F2} (rem ${limitRemaining.Value:F2})";
        else
            dollarDisplay = $"${usage!.Value:F2}";

        // If label is present and not too long, prefer it; otherwise use dollar string
        string? modelName = !string.IsNullOrWhiteSpace(label) && label!.Length <= 30 ? label : dollarDisplay;
        if (!string.IsNullOrWhiteSpace(label) && label!.StartsWith("sk-or-"))
            modelName = dollarDisplay;

        // Detail view does not show ModelName — for unlimited keys (N/A) embed the
        // dollar amount into Name so it is visible in all three view modes.
        if (percent < 0)
            name = dollarDisplay;

        int? totalDuration = limitReset switch
        {
            "daily" => 24 * 60 * 60,
            "weekly" => 7 * 24 * 60 * 60,
            "monthly" => 30 * 24 * 60 * 60,
            _ => null
        };

        var entry = new QuotaEntry
        {
            Id = "openrouter-credits",
            PlatformId = "openrouter",
            Name = name,
            ModelName = modelName,
            UsagePercent = percent,
            Usage = null,
            Total = null,
            ResetSeconds = null,
            TotalDurationSeconds = totalDuration
        };

        return new List<QuotaEntry> { entry };
    }

    private static double? GetDoubleOrNull(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Null)
            return null;
        if (el.ValueKind == JsonValueKind.Number)
            return el.GetDouble();
        if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), out var d))
            return d;
        return null;
    }
}
