using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekRepository.Providers.Seo;

/// <summary>Used when DISABLE_PLAYWRIGHT=true — scoring falls back to SERP-snippet benchmarks only.</summary>
public sealed class NoOpCrawlerProvider : ICrawlerProvider
{
    public string ProviderName => "disabled";

    public Task<Result<PageContent>> CrawlPageAsync(string url, CancellationToken ct = default) =>
        Task.FromResult(Result<PageContent>.Failure("Playwright crawler is disabled (DISABLE_PLAYWRIGHT=true)."));

    public Task<bool> IsAllowedByRobotsTxtAsync(string url, CancellationToken ct = default) =>
        Task.FromResult(false);
}
