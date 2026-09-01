namespace GeekAPI.HttpClients;

public interface IGeekCrawlerResumeRepository
{
    Task<IReadOnlyList<GeekCrawlerPageResumeRowDto>> ListPagesForResumeAsync(
        Guid runId,
        int limit = 500,
        int offset = 0,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListLinksForResumeAsync(
        Guid runId,
        int limit = 500,
        int offset = 0,
        CancellationToken ct = default);

    Task<IReadOnlyList<GeekCrawlerLinkDto>> ListLinksAsync(
        Guid runId,
        bool? sameOrigin = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default);
}
