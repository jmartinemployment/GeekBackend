namespace GeekAPI.HttpClients;

/// <summary>
/// Reads crawled pages by URL within a run, for the sake of their typed blocks.
/// </summary>
/// <remarks>
/// Narrow on purpose, like <see cref="IGccProjectReader"/>. Grounding needs the blocks behind the
/// handful of URLs retrieval returned — never a run's whole page set, which routinely exceeds
/// 50,000 pages.
/// </remarks>
public interface IGccCrawlPageReader
{
    /// <summary>At most 32 URLs per call; the repository route caps it there.</summary>
    Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
        Guid runId,
        IReadOnlyList<string> seedUrls,
        CancellationToken ct = default);

    /// <summary>
    /// One page of a run's crawled pages with their blocks, for a caller that needs the site rather
    /// than named URLs — the heading tree a tool match is built from. Paged and bounded by the
    /// caller, because the remark above still holds: a project-site run is routinely 50,000 pages.
    /// </summary>
    Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
        Guid runId,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default);
}
