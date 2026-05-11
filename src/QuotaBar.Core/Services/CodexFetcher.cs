using System.Net.Http.Headers;
using System.Text.Json;
using QuotaBar.Core.Models;

namespace QuotaBar.Core.Services;

public class CodexFetcher : IUsageFetcher
{
    public string PlatformId => "codex";

    private readonly HttpClient _client = new();

    public async Task<List<QuotaEntry>> FetchAsync(AppSettings settings)
    {
        var (token, accountId) = ReadAuth(settings);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Codex auth token not found. Configure in Settings or ensure ~/.codex/auth.json exists.");
        if (string.IsNullOrWhiteSpace(accountId))
            throw new InvalidOperationException("Codex account_id not found. Configure in Settings or ensure ~/.codex/auth.json exists.");

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://chatgpt.com/backend-api/wham/usage");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Trim());
        request.Headers.Add("ChatGPT-Account-Id", accountId.Trim());
        request.Headers.Add("Accept", "application/json");

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var entries = new List<QuotaEntry>();

        // Try multiple known schemas
        TryParseRateLimit(doc.RootElement, entries);
        TryParseRateLimits(doc.RootElement, entries);
        TryParseNamedLimits(doc.RootElement, entries);

        if (entries.Count == 0)
            throw new InvalidOperationException("Could not parse Codex usage data. Response schema may have changed.");

        return entries;
    }

    private void TryParseRateLimit(JsonElement root, List<QuotaEntry> entries)
    {
        if (!root.TryGetProperty("rate_limit", out var rateLimit))
            return;

        if (rateLimit.TryGetProperty("primary_window", out var primary))
        {
            var entry = ParseWindow(primary, "5H", 18000);
            if (entry != null) entries.Add(entry);
        }
        if (rateLimit.TryGetProperty("secondary_window", out var secondary))
        {
            var entry = ParseWindow(secondary, "7D", 604800);
            if (entry != null) entries.Add(entry);
        }
    }

    private void TryParseRateLimits(JsonElement root, List<QuotaEntry> entries)
    {
        if (!root.TryGetProperty("rate_limits", out var rateLimits))
            return;

        if (rateLimits.TryGetProperty("five_hour_limit", out var fiveH))
        {
            var entry = ParseWindow(fiveH, "5H", 18000);
            if (entry != null) entries.Add(entry);
        }
        if (rateLimits.TryGetProperty("weekly_limit", out var weekly))
        {
            var entry = ParseWindow(weekly, "7D", 604800);
            if (entry != null) entries.Add(entry);
        }
    }

    private void TryParseNamedLimits(JsonElement root, List<QuotaEntry> entries)
    {
        // Direct window properties at root level
        if (root.TryGetProperty("primary_window", out var primary))
        {
            var entry = ParseWindow(primary, "5H", 18000);
            if (entry != null) entries.Add(entry);
        }
        if (root.TryGetProperty("secondary_window", out var secondary))
        {
            var entry = ParseWindow(secondary, "7D", 604800);
            if (entry != null) entries.Add(entry);
        }

        // Code review limit
        if (root.TryGetProperty("code_review_rate_limit", out var reviewLimit))
        {
            if (reviewLimit.TryGetProperty("primary_window", out var reviewWindow))
            {
                var entry = ParseWindow(reviewWindow, "Review", 604800);
                if (entry != null) entries.Add(entry);
            }
        }
    }

    private QuotaEntry? ParseWindow(JsonElement elem, string name, int totalDuration)
    {
        try
        {
            double usedPercent = 0;
            long resetAt = 0;
            int limitWindowSeconds = 0;

            if (elem.TryGetProperty("used_percent", out var up))
            {
                if (up.ValueKind == JsonValueKind.Number)
                    usedPercent = up.GetDouble();
            }
            if (elem.TryGetProperty("reset_at", out var ra))
                resetAt = ra.GetInt64();
            if (elem.TryGetProperty("limit_window_seconds", out var lws))
                limitWindowSeconds = lws.GetInt32();

            var resetSeconds = (int)(resetAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var percent = usedPercent / 100.0;

            return new QuotaEntry
            {
                Id = $"codex-{name.ToLowerInvariant()}",
                PlatformId = "codex",
                Name = name,
                UsagePercent = percent,
                Usage = null,
                Total = null,
                ResetSeconds = Math.Max(0, resetSeconds),
                TotalDurationSeconds = limitWindowSeconds > 0 ? limitWindowSeconds : totalDuration
            };
        }
        catch
        {
            return null;
        }
    }

    private static (string? Token, string? AccountId) ReadAuth(AppSettings settings)
    {
        // Use settings values if provided (override auth.json)
        if (!string.IsNullOrWhiteSpace(settings.CodexAuthToken) &&
            !string.IsNullOrWhiteSpace(settings.CodexAccountId))
        {
            return (settings.CodexAuthToken, settings.CodexAccountId);
        }

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex", "auth.json"
            );

            if (!File.Exists(path))
                return (null, null);

            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);

            string? token = null;
            string? accountId = null;

            if (doc.RootElement.TryGetProperty("tokens", out var tokens))
            {
                if (tokens.TryGetProperty("access_token", out var at))
                    token = at.GetString();
                if (tokens.TryGetProperty("account_id", out var ai))
                    accountId = ai.GetString();

                // Fallback: tokens.id_token.chatgpt_account_id
                if (string.IsNullOrWhiteSpace(accountId) &&
                    tokens.TryGetProperty("id_token", out var idToken))
                {
                    if (idToken.TryGetProperty("chatgpt_account_id", out var cai))
                        accountId = cai.GetString();
                }
            }

            return (token, accountId);
        }
        catch
        {
            return (null, null);
        }
    }
}
