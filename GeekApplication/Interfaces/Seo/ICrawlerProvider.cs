using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface ICrawlerProvider
{
    string ProviderName { get; }
    Task<Result<PageContent>> CrawlPageAsync(string url, CancellationToken ct = default);
    Task<bool> IsAllowedByRobotsTxtAsync(string url, CancellationToken ct = default);
}
