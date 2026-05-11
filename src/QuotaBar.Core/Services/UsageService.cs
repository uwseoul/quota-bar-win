using QuotaBar.Core.Models;

namespace QuotaBar.Core.Services;

public class UsageService : IUsageService
{
    private readonly List<IUsageFetcher> _fetchers = new();
    private readonly SettingsService _settingsService = new();

    public UsageService()
    {
        _fetchers.Add(new GlmFetcher());
        _fetchers.Add(new MiniMaxFetcher());
        _fetchers.Add(new CodexFetcher());
        _fetchers.Add(new OpenCodeGoFetcher());
    }

    public async Task<Dictionary<string, PlatformResult>> FetchAllAsync()
    {
        var settings = _settingsService.Load();
        var results = new Dictionary<string, PlatformResult>();

        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(4, 4);

        foreach (var fetcher in _fetchers)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var isEnabled = fetcher.PlatformId switch
                    {
                        "glm" => settings.GlmEnabled,
                        "minimax" => settings.MiniMaxEnabled,
                        "codex" => settings.CodexEnabled,
                        "opencodego" => settings.OpenCodeGoEnabled,
                        _ => true
                    };

                    if (!isEnabled)
                    {
                        results[fetcher.PlatformId] = new PlatformResult
                        {
                            Entries = new List<QuotaEntry>(),
                            Error = "Disabled"
                        };
                        return;
                    }

                    try
                    {
                        var entries = await fetcher.FetchAsync(settings);
                        results[fetcher.PlatformId] = new PlatformResult
                        {
                            Entries = entries,
                            Error = null
                        };
                    }
                    catch (Exception ex)
                    {
                        results[fetcher.PlatformId] = new PlatformResult
                        {
                            Entries = new List<QuotaEntry>(),
                            Error = ex.Message
                        };
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
        return results;
    }
}
