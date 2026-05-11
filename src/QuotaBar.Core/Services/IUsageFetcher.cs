using QuotaBar.Core.Models;

namespace QuotaBar.Core.Services;

public interface IUsageFetcher
{
    string PlatformId { get; }
    Task<List<QuotaEntry>> FetchAsync(AppSettings settings);
}
