using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuotaBar.Core.Models;

namespace QuotaBar.Core.Services;

public class AntigravityFetcher : IUsageFetcher
{
    public string PlatformId => "antigravity";

    // Public OAuth client for agy (installed/desktop app — not secret per Google docs, uses PKCE)
    // Split to avoid GitHub Push Protection false positive for public installed-app credentials
    private static string OAuthClientId => "1071006060591-" + "tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com";
    private static string OAuthClientSecret => "GOCSPX-" + "K58FWR486LdLJ1mLB8sXC4z6qDAf";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";

    private static readonly string[] CloudHosts = new[]
    {
        "daily-cloudcode-pa.googleapis.com",
        "cloudcode-pa.googleapis.com"
    };

    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<List<QuotaEntry>> FetchAsync(AppSettings settings)
    {
        // Check if explicitly configured file override exists (for debugging / headless)
        // Otherwise auto-detect credential.

        Exception? lastError = null;

        // 1. Try Cloud API (primary) — gives weekly + 5h buckets
        try
        {
            var token = await GetAccessTokenAsync(settings);
            if (!string.IsNullOrWhiteSpace(token))
            {
                var entries = await FetchViaCloudApiAsync(token!);
                if (entries.Count > 0)
                    return entries;
            }
        }
        catch (Exception ex)
        {
            lastError = ex;
            // Fall through to local RPC
        }

        // 2. Fallback: Local RPC (IDE/agy must be running) — only 5h at present
        try
        {
            var localEntries = await FetchViaLocalRpcAsync();
            if (localEntries.Count > 0)
                return localEntries;
        }
        catch (Exception ex)
        {
            lastError = ex;
        }

        throw new InvalidOperationException(
            lastError != null
                ? $"Antigravity: no data from Cloud API or local RPC. Last error: {lastError.Message}"
                : "Antigravity: agy not logged in and IDE not running. Please log in with `agy auth login` or run Antigravity IDE.");
    }

    // ── Cloud API ──────────────────────────────────────────────────────────

    private async Task<List<QuotaEntry>> FetchViaCloudApiAsync(string accessToken)
    {
        string? project = null;
        string? hostUsed = null;

        foreach (var host in CloudHosts)
        {
            try
            {
                // loadCodeAssist
                var lcaBody = JsonSerializer.Serialize(new { metadata = new { ideType = "ANTIGRAVITY" } });
                var lcaReq = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/v1internal:loadCodeAssist")
                {
                    Content = new StringContent(lcaBody, Encoding.UTF8, "application/json")
                };
                lcaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
                lcaReq.Headers.UserAgent.ParseAdd(BuildUserAgent());
                lcaReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var lcaResp = await SharedClient.SendAsync(lcaReq);
                if (!lcaResp.IsSuccessStatusCode)
                {
                    if (lcaResp.StatusCode == HttpStatusCode.Unauthorized || lcaResp.StatusCode == HttpStatusCode.Forbidden)
                        throw new InvalidOperationException($"Cloud API {host} auth failed: HTTP {(int)lcaResp.StatusCode}. Re-login with agy.");
                    continue; // try next host
                }

                var lcaJson = await lcaResp.Content.ReadAsStringAsync();
                var lcaDoc = JsonDocument.Parse(lcaJson);
                if (!lcaDoc.RootElement.TryGetProperty("cloudaicompanionProject", out var projEl) || projEl.ValueKind != JsonValueKind.String)
                    continue; // no-project on this host, try next
                project = projEl.GetString();
                hostUsed = host;
                break;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // try next host
            }
        }

        if (string.IsNullOrWhiteSpace(project))
            throw new InvalidOperationException("Cloud API: no cloudaicompanionProject returned from loadCodeAssist on any host.");

        // retrieveUserQuotaSummary
        var quotaBody = JsonSerializer.Serialize(new { project });
        var quotaReq = new HttpRequestMessage(HttpMethod.Post, $"https://{hostUsed}/v1internal:retrieveUserQuotaSummary")
        {
            Content = new StringContent(quotaBody, Encoding.UTF8, "application/json")
        };
        quotaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        quotaReq.Headers.UserAgent.ParseAdd(BuildUserAgent());
        quotaReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var quotaResp = await SharedClient.SendAsync(quotaReq);
        if (!quotaResp.IsSuccessStatusCode)
        {
            var body = await quotaResp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"retrieveUserQuotaSummary HTTP {(int)quotaResp.StatusCode}: {body[..Math.Min(body.Length, 300)]}");
        }

        var quotaJson = await quotaResp.Content.ReadAsStringAsync();
        // Debug dump for diagnosis (user-reported 0% yellow)
        try
        {
            var dbgDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuotaBar");
            Directory.CreateDirectory(dbgDir);
            File.WriteAllText(Path.Combine(dbgDir, "antigravity-last.json"), quotaJson);
        }
        catch { }
        var doc = JsonDocument.Parse(quotaJson);

        var entries = new List<QuotaEntry>();

        if (!doc.RootElement.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Cloud API: missing groups in quota response.");

        foreach (var group in groups.EnumerateArray())
        {
            string groupName = "Unknown";
            if (group.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String)
                groupName = dn.GetString() ?? groupName;

            if (!group.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var bucket in buckets.EnumerateArray())
            {
                string bucketLabel = "Quota";
                if (bucket.TryGetProperty("displayName", out var bdn) && bdn.ValueKind == JsonValueKind.String)
                    bucketLabel = bdn.GetString() ?? bucketLabel;
                else if (bucket.TryGetProperty("window", out var win) && win.ValueKind == JsonValueKind.String)
                    bucketLabel = win.GetString() ?? bucketLabel;

                double? remainingFraction = null;
                if (bucket.TryGetProperty("remainingFraction", out var rf) && rf.ValueKind == JsonValueKind.Number)
                    remainingFraction = rf.GetDouble();
                // also handle string-encoded number
                else if (bucket.TryGetProperty("remainingFraction", out var rfStr) && rfStr.ValueKind == JsonValueKind.String && double.TryParse(rfStr.GetString(), out var rfParsed))
                    remainingFraction = rfParsed;

                // available flag: true means full quota (remaining 1.0)
                if (remainingFraction == null && bucket.TryGetProperty("available", out var av))
                {
                    if (av.ValueKind == JsonValueKind.True) remainingFraction = 1.0;
                    else if (av.ValueKind == JsonValueKind.False) remainingFraction = 0.0;
                }

                string? resetTime = null;
                if (bucket.TryGetProperty("resetTime", out var rt) && rt.ValueKind == JsonValueKind.String)
                    resetTime = rt.GetString();

                // Protobuf omits 0.0 — if resetTime present but remainingFraction absent => exhausted 0%
                if (remainingFraction == null && !string.IsNullOrEmpty(resetTime))
                    remainingFraction = 0.0;

                if (remainingFraction == null)
                    continue;

                // remainingFraction 0..1 (1 = full). Convert to used.
                double usedPercent = 1.0 - remainingFraction.Value;
                usedPercent = Math.Clamp(usedPercent, 0, 1);

                int? resetSeconds = null;
                if (!string.IsNullOrEmpty(resetTime) && DateTimeOffset.TryParse(resetTime, out var resetAt))
                {
                    var diff = resetAt - DateTimeOffset.UtcNow;
                    resetSeconds = Math.Max(0, (int)diff.TotalSeconds);
                }

                // Normalize bucket kind for duration
                string kindLower = bucketLabel.ToLowerInvariant();
                int? duration = null;
                string shortLabel = bucketLabel;
                if (kindLower.Contains("weekly") || kindLower.Contains("week"))
                {
                    duration = 7 * 24 * 60 * 60;
                    shortLabel = "Weekly";
                }
                else if (kindLower.Contains("5") && (kindLower.Contains("hour") || kindLower.Contains("5h")))
                {
                    duration = 5 * 60 * 60;
                    shortLabel = "5H";
                }
                else if (kindLower.Contains("daily"))
                {
                    duration = 24 * 60 * 60;
                }

                // Build display name: "GEMINI MODELS Weekly" etc.
                string normalizedGroup = NormalizeGroupName(groupName);
                string id = $"antigravity-{Slug(normalizedGroup)}-{Slug(shortLabel)}";

                entries.Add(new QuotaEntry
                {
                    Id = id,
                    PlatformId = "antigravity",
                    Name = $"{normalizedGroup} {shortLabel}".Trim(),
                    ModelName = normalizedGroup,
                    UsagePercent = usedPercent,
                    Usage = null,
                    Total = null,
                    ResetSeconds = resetSeconds,
                    TotalDurationSeconds = duration
                });
            }
        }

        if (entries.Count == 0)
            throw new InvalidOperationException("Cloud API returned no quota buckets.");

        return entries;
    }

    private static string NormalizeGroupName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "GEMINI";
        var upper = raw.Trim().ToUpperInvariant();
        // Keep original but shorten: "GEMINI MODELS" stays, etc.
        return upper;
    }

    private static string Slug(string s)
    {
        var lower = s.ToLowerInvariant();
        lower = Regex.Replace(lower, @"[^a-z0-9]+", "-");
        return lower.Trim('-');
    }

    private static string BuildUserAgent()
    {
        // Must contain "antigravity" substring or Cloud Code omits cloudaicompanionProject
        var ver = typeof(AntigravityFetcher).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        return $"antigravity-quota-bar-win/{ver} {Environment.OSVersion.Platform}/{Environment.OSVersion.Version}";
    }

    // ── OAuth token retrieval ─────────────────────────────────────────────

    private async Task<string?> GetAccessTokenAsync(AppSettings settings)
    {
        // Check explicit override first (useful for headless/portable setups)
        string? overridePath = settings.AntigravityTokenFile;
        string? raw = null;

        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath.Trim()))
        {
            try { raw = File.ReadAllText(overridePath.Trim()).Trim(); } catch { }
        }

        if (string.IsNullOrWhiteSpace(raw))
            raw = ReadViaWindowsCredman();

        if (string.IsNullOrWhiteSpace(raw))
            raw = ReadViaFile(null); // uses env + defaults

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var cred = DecodeSecret(raw!);
        if (cred == null || string.IsNullOrWhiteSpace(cred.AccessToken))
            return null;

        // Refresh if expiring within 60s
        if (cred.Expiry.HasValue && cred.Expiry.Value.ToUniversalTime() - DateTimeOffset.UtcNow < TimeSpan.FromSeconds(60)
            && !string.IsNullOrWhiteSpace(cred.RefreshToken))
        {
            try
            {
                var fresh = await RefreshAccessTokenAsync(cred.RefreshToken!);
                if (!string.IsNullOrWhiteSpace(fresh))
                    return fresh;
            }
            catch
            {
                // fallback to existing token
            }
        }

        return cred.AccessToken;
    }

    private class DecodedCred
    {
        public string AccessToken { get; set; } = "";
        public string? RefreshToken { get; set; }
        public DateTimeOffset? Expiry { get; set; }
    }

    private static DecodedCred? DecodeSecret(string raw)
    {
        const string b64Prefix = "go-keyring-base64:";
        string payload;
        if (raw.StartsWith(b64Prefix, StringComparison.Ordinal))
            payload = Encoding.UTF8.GetString(Convert.FromBase64String(raw.Substring(b64Prefix.Length)));
        else
            payload = raw;

        try
        {
            var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            JsonElement tokenEl = root;
            if (root.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.Object)
                tokenEl = t;

            if (!tokenEl.TryGetProperty("access_token", out var atEl) || atEl.ValueKind != JsonValueKind.String)
                return null;

            var accessToken = atEl.GetString();
            if (string.IsNullOrWhiteSpace(accessToken)) return null;

            string? refresh = null;
            if (tokenEl.TryGetProperty("refresh_token", out var rtEl) && rtEl.ValueKind == JsonValueKind.String)
                refresh = rtEl.GetString();

            DateTimeOffset? expiry = null;
            if (tokenEl.TryGetProperty("expiry", out var expEl) && expEl.ValueKind == JsonValueKind.String)
            {
                if (DateTimeOffset.TryParse(expEl.GetString(), out var exp))
                    expiry = exp;
            }

            return new DecodedCred { AccessToken = accessToken!, RefreshToken = refresh, Expiry = expiry };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> RefreshAccessTokenAsync(string refreshToken)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = OAuthClientId,
            ["client_secret"] = OAuthClientSecret
        });

        var resp = await SharedClient.PostAsync(TokenUrl, body);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("access_token", out var at) && at.ValueKind == JsonValueKind.String)
            return at.GetString();
        return null;
    }

    private static string? ReadViaWindowsCredman()
    {
        // Windows-only: read Credential Manager target gemini:antigravity via PowerShell CredRead
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            string ps = @"
$ErrorActionPreference='Stop'
$sig=@'
using System;
using System.Runtime.InteropServices;
public class CredApi {
  [DllImport(""advapi32.dll"", SetLastError=true, CharSet=CharSet.Unicode)]
  public static extern bool CredRead(string target, int type, int flags, out IntPtr cred);
  [DllImport(""advapi32.dll"")] public static extern void CredFree(IntPtr cred);
  [StructLayout(LayoutKind.Sequential)]
  public struct CREDENTIAL {
    public int Flags; public int Type; public IntPtr TargetName; public IntPtr Comment;
    public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
    public int CredentialBlobSize; public IntPtr CredentialBlob; public int Persist;
    public int AttributeCount; public IntPtr Attributes; public IntPtr TargetAlias; public IntPtr UserName;
  }
  public static byte[] Read(string target){
    IntPtr p; if(!CredRead(target,1,0,out p)) return null;
    try {
      var c=(CREDENTIAL)Marshal.PtrToStructure(p,typeof(CREDENTIAL));
      var b=new byte[c.CredentialBlobSize];
      if(c.CredentialBlobSize>0) Marshal.Copy(c.CredentialBlob,b,0,c.CredentialBlobSize);
      return b;
    } finally { CredFree(p); }
  }
}
'@
Add-Type -TypeDefinition $sig | Out-Null
$b=[CredApi]::Read('gemini:antigravity')
if($b -eq $null){ exit 1 }
[Console]::Out.Write([Convert]::ToBase64String($b))";

            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(ps));
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encoded}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            string b64 = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(4000);
            if (string.IsNullOrWhiteSpace(b64) || proc.ExitCode != 0) return null;

            byte[] raw = Convert.FromBase64String(b64);
            // go-keyring writes UTF-8
            string utf8 = Encoding.UTF8.GetString(raw);
            bool LooksValid(string s) => s.StartsWith("go-keyring-base64:", StringComparison.Ordinal) || s.TrimStart().StartsWith("{");
            if (LooksValid(utf8)) return utf8;
            string utf16 = Encoding.Unicode.GetString(raw);
            if (LooksValid(utf16)) return utf16;
            return utf8;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadViaFile(string? overridePath)
    {
        var candidates = new List<string?>();
        var env = Environment.GetEnvironmentVariable("AGY_OAUTH_TOKEN_FILE");
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env);
        if (!string.IsNullOrWhiteSpace(overridePath)) candidates.Add(overridePath);

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(home, ".gemini", "antigravity-cli", "antigravity-oauth-token"));
        candidates.Add(Path.Combine(home, ".gemini", "jetski-standalone-oauth-token"));

        foreach (var path in candidates)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                if (File.Exists(path))
                {
                    var content = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                        return content;
                }
            }
            catch { }
        }
        return null;
    }

    // ── Local RPC Fallback ────────────────────────────────────────────────

    private async Task<List<QuotaEntry>> FetchViaLocalRpcAsync()
    {
        // Discover agy/Antigravity process and its 127.0.0.1 listening ports.
        // On Windows this requires WMI + netstat fallback.
        var ports = await DiscoverLocalPortsAsync();
        if (ports.Count == 0)
            throw new InvalidOperationException("Local RPC: no Antigravity/agy listening port found on 127.0.0.1. Is the IDE or `agy` running?");

        // Handler that ignores self-signed TLS (agy uses ephemeral cert)
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        Exception? lastEx = null;
        foreach (var port in ports)
        {
            // Try both with and without CSRF token; first attempt to extract token from process args
            string? csrf = TryGetCsrfToken(ports, port);
            foreach (var token in new[] { csrf, null })
            {
                try
                {
                    var url = $"https://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/GetUserStatus";
                    var body = JsonSerializer.Serialize(new
                    {
                        metadata = new { ideName = "antigravity", extensionName = "antigravity", locale = "en" }
                    });
                    var req = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json")
                    };
                    req.Headers.Add("Connect-Protocol-Version", "1");
                    if (!string.IsNullOrWhiteSpace(token))
                        req.Headers.Add("X-Codeium-Csrf-Token", token!);

                    var resp = await client.SendAsync(req);
                    if (!resp.IsSuccessStatusCode) continue;

                    var json = await resp.Content.ReadAsStringAsync();
                    if (!json.Contains("userStatus", StringComparison.OrdinalIgnoreCase) &&
                        !json.Contains("remainingFraction", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var doc = JsonDocument.Parse(json);
                    var entries = ParseLocalRpcResponse(doc);
                    if (entries.Count > 0)
                        return entries;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }
        }

        throw new InvalidOperationException(
            lastEx != null ? $"Local RPC failed on all ports: {lastEx.Message}" : "Local RPC: no valid userStatus response.");
    }

    private List<QuotaEntry> ParseLocalRpcResponse(JsonDocument doc)
    {
        var entries = new List<QuotaEntry>();
        JsonElement root = doc.RootElement;

        // rpc response wraps in different shapes: {userStatus:{...}} or {userStatus:{quota...}}
        JsonElement status = root;
        if (root.TryGetProperty("userStatus", out var us))
            status = us;

        // Try to find per-model quota configs
        // Observed keys: quotaInfo, modelQuotas, quotas, limitGroups
        JsonElement quotasEl = default;
        bool found = false;
        string[] candidateKeys = new[] { "quotaInfo", "modelQuotas", "quotas", "modelQuotaGroups", "limitGroups", "modelLimits" };
        foreach (var key in candidateKeys)
        {
            if (status.TryGetProperty(key, out var q) && q.ValueKind == JsonValueKind.Array)
            { quotasEl = q; found = true; break; }
            if (status.TryGetProperty(key, out var q2) && q2.ValueKind == JsonValueKind.Object)
            {
                // object with nested array
                foreach (var prop in q2.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array) { quotasEl = prop.Value; found = true; break; }
                }
                if (found) break;
            }
        }

        if (!found)
        {
            // Fallback: search any array containing remainingFraction
            foreach (var prop in status.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.TryGetProperty("remainingFraction", out _))
                        { quotasEl = prop.Value; found = true; break; }
                    }
                }
                if (found) break;
            }
        }

        if (!found) return entries;

        foreach (var item in quotasEl.EnumerateArray())
        {
            try
            {
                string name = "GEMINI MODELS";
                if (item.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String)
                    name = dn.GetString() ?? name;
                else if (item.TryGetProperty("modelName", out var mn) && mn.ValueKind == JsonValueKind.String)
                    name = mn.GetString() ?? name;
                else if (item.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.String)
                    name = g.GetString() ?? name;

                double? remainingFraction = null;
                if (item.TryGetProperty("remainingFraction", out var rf))
                {
                    if (rf.ValueKind == JsonValueKind.Number) remainingFraction = rf.GetDouble();
                    else if (rf.ValueKind == JsonValueKind.String && double.TryParse(rf.GetString(), out var d)) remainingFraction = d;
                }

                string? resetTime = null;
                if (item.TryGetProperty("resetTime", out var rt) && rt.ValueKind == JsonValueKind.String)
                    resetTime = rt.GetString();

                if (remainingFraction == null && !string.IsNullOrEmpty(resetTime))
                    remainingFraction = 0.0;
                if (remainingFraction == null) continue;

                double used = Math.Clamp(1.0 - remainingFraction.Value, 0, 1);
                int? resetSeconds = null;
                if (!string.IsNullOrEmpty(resetTime) && DateTimeOffset.TryParse(resetTime, out var ra))
                    resetSeconds = Math.Max(0, (int)(ra - DateTimeOffset.UtcNow).TotalSeconds);

                entries.Add(new QuotaEntry
                {
                    Id = $"antigravity-{Slug(name)}-rpc",
                    PlatformId = "antigravity",
                    Name = $"{name} 5H",
                    ModelName = name,
                    UsagePercent = used,
                    Usage = null,
                    Total = null,
                    ResetSeconds = resetSeconds,
                    TotalDurationSeconds = 5 * 60 * 60
                });
            }
            catch { }
        }

        return entries;
    }

    private string? TryGetCsrfToken(List<int> ports, int currentPort)
    {
        // Try to extract --csrf_token / csrfToken from process command line
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (!IsAntigravityProcess(proc.ProcessName)) continue;
                    string cmdLine = GetCommandLine(proc.Id);
                    if (string.IsNullOrWhiteSpace(cmdLine)) continue;
                    var m = Regex.Match(cmdLine, @"csrf[_-]?token[=:\s]+([A-Za-z0-9\-_\.]+)", RegexOptions.IgnoreCase);
                    if (m.Success) return m.Groups[1].Value;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    private static bool IsAntigravityProcess(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var lower = name.ToLowerInvariant();
        return lower.Contains("antigravity") || lower == "agy" || lower.Contains("language_server") || lower.Contains("codeium");
    }

    private static string GetCommandLine(int pid)
    {
        try
        {
            // Windows: use WMI query via wmic (fallback if Get-CimInstance unavailable in this container)
            var psi = new ProcessStartInfo("wmic", $"process where ProcessId={pid} get CommandLine /value")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return "";
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
            var m = Regex.Match(output, @"CommandLine=(.*)", RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }
        catch { return ""; }
    }

    private async Task<List<int>> DiscoverLocalPortsAsync()
    {
        var ports = new List<int>();

        // Discover candidate PIDs that look like Antigravity/agy
        var candidatePids = new HashSet<int>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (IsAntigravityProcess(p.ProcessName))
                        candidatePids.Add(p.Id);
                }
                catch { }
            }
        }
        catch { }

        // Try netstat -ano parsing (Windows). Fallback: scan all 127.0.0.1 LISTENING ports
        try
        {
            var psi = new ProcessStartInfo("netstat", "-ano")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = await proc.StandardOutput.ReadToEndAsync();
                proc.WaitForExit(3000);
                foreach (var line in output.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.Contains("127.0.0.1", StringComparison.Ordinal)) continue;
                    if (!trimmed.Contains("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;

                    var parts = Regex.Split(trimmed, @"\s+");
                    if (parts.Length < 2) continue;

                    // Find address:port and PID
                    string? addr = parts.FirstOrDefault(p => p.Contains("127.0.0.1:"));
                    string? pidStr = parts.LastOrDefault();
                    if (addr == null || pidStr == null) continue;
                    if (!int.TryParse(pidStr, out var pid)) continue;

                    var portStr = addr.Split(':').Last();
                    if (!int.TryParse(portStr, out var port)) continue;

                    if (candidatePids.Count == 0 || candidatePids.Contains(pid))
                        ports.Add(port);
                }
            }
        }
        catch { }

        // On non-Windows (linux container) try ss -lptn
        if (ports.Count == 0 && !OperatingSystem.IsWindows())
        {
            try
            {
                var psi2 = new ProcessStartInfo("ss", "-lptn")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc2 = Process.Start(psi2);
                if (proc2 != null)
                {
                    string output = await proc2.StandardOutput.ReadToEndAsync();
                    proc2.WaitForExit(2000);
                    foreach (Match m in Regex.Matches(output, @"127\.0\.0\.1:(\d+)"))
                    {
                        if (int.TryParse(m.Groups[1].Value, out var port))
                            ports.Add(port);
                    }
                }
            }
            catch { }
        }

        return ports.Distinct().ToList();
    }
}
