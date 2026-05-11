using QuotaBar.Core.Models;

namespace QuotaBar.Core.Services;

public interface IUsageService
{
    Task<Dictionary<string, PlatformResult>> FetchAllAsync();
}

public class MockUsageService : IUsageService
{
    public Task<Dictionary<string, PlatformResult>> FetchAllAsync()
    {
        var results = new Dictionary<string, PlatformResult>
        {
            ["glm"] = new PlatformResult
            {
                Entries = new List<QuotaEntry>
                {
                    new()
                    {
                        Id = "glm-1",
                        PlatformId = "glm",
                        Name = "5 Hours Quota",
                        UsagePercent = 0.67,
                        Usage = 3_200_000_000,
                        Total = 5_000_000_000,
                        ResetSeconds = 7200 + 2580, // ~2h 43m
                        TotalDurationSeconds = 18000
                    },
                    new()
                    {
                        Id = "glm-2",
                        PlatformId = "glm",
                        Name = "Weekly Quota",
                        UsagePercent = 0.23,
                        Usage = 2_100_000_000,
                        Total = 9_000_000_000,
                        ResetSeconds = 446400, // ~5d 4h
                        TotalDurationSeconds = 604800
                    },
                    new()
                    {
                        Id = "glm-3",
                        PlatformId = "glm",
                        Name = "Monthly MCP",
                        UsagePercent = 0.45,
                        Usage = 900_000_000,
                        Total = 2_000_000_000,
                        ResetSeconds = 2_592_000, // ~30d
                        TotalDurationSeconds = 2_592_000
                    }
                }
            },
            ["minimax"] = new PlatformResult
            {
                Entries = new List<QuotaEntry>
                {
                    new()
                    {
                        Id = "minimax-1",
                        PlatformId = "minimax",
                        Name = "5H",
                        UsagePercent = 0.82,
                        Usage = 4_100_000,
                        Total = 5_000_000,
                        ResetSeconds = 3600,
                        TotalDurationSeconds = 18000
                    },
                    new()
                    {
                        Id = "minimax-2",
                        PlatformId = "minimax",
                        Name = "Weekly",
                        UsagePercent = 0.34,
                        Usage = 1_700_000,
                        Total = 5_000_000,
                        ResetSeconds = 518400,
                        TotalDurationSeconds = 604800
                    }
                }
            },
            ["codex"] = new PlatformResult
            {
                Entries = new List<QuotaEntry>
                {
                    new()
                    {
                        Id = "codex-1",
                        PlatformId = "codex",
                        Name = "5H",
                        UsagePercent = 0.15,
                        Usage = 150_000,
                        Total = 1_000_000,
                        ResetSeconds = 14400,
                        TotalDurationSeconds = 18000
                    },
                    new()
                    {
                        Id = "codex-2",
                        PlatformId = "codex",
                        Name = "7D",
                        UsagePercent = 0.41,
                        Usage = 410_000,
                        Total = 1_000_000,
                        ResetSeconds = 518400,
                        TotalDurationSeconds = 604800
                    },
                    new()
                    {
                        Id = "codex-3",
                        PlatformId = "codex",
                        Name = "Review",
                        UsagePercent = 0.78,
                        Usage = 780_000,
                        Total = 1_000_000,
                        ResetSeconds = 604800,
                        TotalDurationSeconds = 604800
                    }
                }
            },
            ["opencodego"] = new PlatformResult
            {
                Entries = new List<QuotaEntry>
                {
                    new()
                    {
                        Id = "opencodego-1",
                        PlatformId = "opencodego",
                        Name = "Rolling",
                        UsagePercent = 0.55,
                        Usage = 5_500_000,
                        Total = 10_000_000,
                        ResetSeconds = 86400,
                        TotalDurationSeconds = 86400
                    },
                    new()
                    {
                        Id = "opencodego-2",
                        PlatformId = "opencodego",
                        Name = "Weekly",
                        UsagePercent = 0.30,
                        Usage = 3_000_000,
                        Total = 10_000_000,
                        ResetSeconds = 518400,
                        TotalDurationSeconds = 604800
                    },
                    new()
                    {
                        Id = "opencodego-3",
                        PlatformId = "opencodego",
                        Name = "Monthly",
                        UsagePercent = 0.12,
                        Usage = 1_200_000,
                        Total = 10_000_000,
                        ResetSeconds = 2_592_000,
                        TotalDurationSeconds = 2_592_000
                    }
                }
            }
        };

        return Task.FromResult(results);
    }
}
