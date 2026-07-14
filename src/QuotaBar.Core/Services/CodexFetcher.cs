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
        request.Headers.Add("User-Agent", "codex-cli");
        request.Headers.Add("Origin", "https://chatgpt.com");
        request.Headers.Referrer = new Uri("https://chatgpt.com/");

        var response = await _client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var reason = response.ReasonPhrase?.Trim() ?? "Unknown";
            throw new InvalidOperationException(
                $"HTTP {(int)response.StatusCode} {reason}\n{body[..Math.Min(body.Length, 200)]}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var entries = new List<QuotaEntry>();

        // Try multiple known schemas
        TryParseRateLimit(doc.RootElement, entries);
        TryParseRateLimits(doc.RootElement, entries);
        TryParseNamedLimits(doc.RootElement, entries);

        if (entries.Count == 0)
            throw new InvalidOperationException("Could not parse Codex usage data. Response schema may have changed.");

        // Remove duplicates by Id, preferring entries with reset info
        return entries
            .GroupBy(e => e.Id)
            .Select(g => g.OrderByDescending(e => e.ResetSeconds ?? 0).First())
            .ToList();
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

        // Code review limit (skip if null)
        if (root.TryGetProperty("code_review_rate_limit", out var reviewLimit) &&
            reviewLimit.ValueKind != JsonValueKind.Null)
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
            int resetAfterSeconds = 0;
            int limitWindowSeconds = 0;

            if (elem.TryGetProperty("used_percent", out var up))
                usedPercent = GetDouble(up);
            if (elem.TryGetProperty("reset_at", out var ra))
                resetAt = GetInt64(ra);
            if (elem.TryGetProperty("reset_after_seconds", out var ras))
                resetAfterSeconds = (int)GetInt64(ras);
            if (elem.TryGetProperty("limit_window_seconds", out var lws))
                limitWindowSeconds = (int)GetInt64(lws);

            int? resetSeconds = null;
            if (resetAfterSeconds > 0)
                resetSeconds = resetAfterSeconds;
            else if (resetAt > 0)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                // reset_at might be Unix timestamp in milliseconds (13 digits)
                var resetAtSec = resetAt > 9999999999 ? resetAt / 1000 : resetAt;
                resetSeconds = Math.Max(0, (int)(resetAtSec - now));
            }

            // Prefer the server-provided duration for labeling. Codex has changed
            // shapes over time; primary_window can represent either 5H or 7D.
            var windowName = name;
            var effectiveDuration = limitWindowSeconds > 0 ? limitWindowSeconds : totalDuration;
            if (limitWindowSeconds == 604800)
                windowName = "7D";
            else if (limitWindowSeconds == 18000)
                windowName = "5H";

            if (resetSeconds > effectiveDuration)
                resetSeconds = effectiveDuration;

            var percent = usedPercent / 100.0;

            return new QuotaEntry
            {
                Id = $"codex-{windowName.ToLowerInvariant()}",
                PlatformId = "codex",
                Name = windowName,
                ModelName = "Codex",
                UsagePercent = percent,
                Usage = null,
                Total = null,
                ResetSeconds = resetSeconds,
                TotalDurationSeconds = effectiveDuration
            };
        }
        catch
        {
            return null;
        }
    }

    private static double GetDouble(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Number)
            return elem.GetDouble();
        if (elem.ValueKind == JsonValueKind.String && double.TryParse(elem.GetString(), out var d))
            return d;
        return 0;
    }

    private static long GetInt64(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Number)
            return elem.GetInt64();
        if (elem.ValueKind == JsonValueKind.String && long.TryParse(elem.GetString(), out var l))
            return l;
        return 0;
    }

    private static (string? Token, string? AccountId) ReadAuth(AppSettings settings)
    {
        // Field-by-field merge: settings override auth.json
        string? token = null;
        string? accountId = null;

        // Try auth.json first
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex", "auth.json"
            );

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("tokens", out var tokens))
                {
                    if (tokens.TryGetProperty("access_token", out var at))
                        token = at.GetString();
                    if (tokens.TryGetProperty("account_id", out var ai))
                        accountId = ai.GetString();

                    // Fallback: decode id_token JWT payload for chatgpt_account_id
                    if (string.IsNullOrWhiteSpace(accountId) &&
                        tokens.TryGetProperty("id_token", out var idTokenElem) &&
                        idTokenElem.ValueKind == JsonValueKind.String)
                    {
                        var idTokenJwt = idTokenElem.GetString();
                        if (!string.IsNullOrWhiteSpace(idTokenJwt))
                            accountId = ExtractAccountIdFromJwt(idTokenJwt);
                    }
                }
            }
        }
        catch { /* ignore auth.json read errors; settings override handles missing file */ }

        // Settings override
        if (!string.IsNullOrWhiteSpace(settings.CodexAuthToken))
            token = settings.CodexAuthToken;
        if (!string.IsNullOrWhiteSpace(settings.CodexAccountId))
            accountId = settings.CodexAccountId;

        return (token, accountId);
    }

    private static string? ExtractAccountIdFromJwt(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
                return null;

            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var bytes = Convert.FromBase64String(payload);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("chatgpt_account_id", out var plainClaim) &&
                plainClaim.ValueKind == JsonValueKind.String)
            {
                return plainClaim.GetString();
            }

            if (doc.RootElement.TryGetProperty("https://api.openai.com/auth.chatgpt_account_id", out var nsClaim) &&
                nsClaim.ValueKind == JsonValueKind.String)
            {
                return nsClaim.GetString();
            }

            // Fallback: organizations array (prefer is_default)
            if (doc.RootElement.TryGetProperty("https://api.openai.com/auth.organizations", out var orgs) &&
                orgs.ValueKind == JsonValueKind.Array)
            {
                string? defaultOrgId = null;
                string? firstOrgId = null;

                foreach (var org in orgs.EnumerateArray())
                {
                    if (org.TryGetProperty("id", out var orgId) && orgId.ValueKind == JsonValueKind.String)
                    {
                        var id = orgId.GetString();
                        if (firstOrgId == null)
                            firstOrgId = id;

                        if (org.TryGetProperty("is_default", out var isDefault) &&
                            isDefault.ValueKind == JsonValueKind.True)
                        {
                            defaultOrgId = id;
                            break;
                        }
                    }
                }

                return defaultOrgId ?? firstOrgId;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
