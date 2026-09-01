namespace GeekAPI.HttpClients;

public interface IGeekCrawlerResumeRepository
{
    Task<IReadOnlyList<GeekCrawlerPageResumeRowDto>> ListPagesForResumeAsync(
        Guid runId,
        int limit = 500,
        int offset = 0,
        CancellationToken ct = default);

    Task<IReadOnlyList<GeekCrawlerLinkResumeRowDto>> ListLinksForResumeAsync(
        Guid runId,
        int limit = 500,
        DateTimeOffset? afterDiscoveredAtUtc = null,
        Guid? afterId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<GeekCrawlerLinkDto>> ListLinksAsync(
        Guid runId,
        bool? sameOrigin = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default);
}
