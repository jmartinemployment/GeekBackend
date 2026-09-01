using GeekAPI.HttpClients;

namespace GeekAPI.Services.GeekCrawler;

public sealed class GeekCrawlerPageBatchWriter
{
    private readonly HttpGeekCrawlerRepository _repo;
    private readonly ILogger<GeekCrawlerPageBatchWriter> _logger;

    public GeekCrawlerPageBatchWriter(
        HttpGeekCrawlerRepository repo,
        ILogger<GeekCrawlerPageBatchWriter> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<GeekCrawlerPageBatchResult> SavePagesWithRetryAsync(
        Guid runId,
        IReadOnlyList<SameOriginBfsCrawler.CrawledPageResult> batch,
        CancellationToken ct)
    {
        var items = batch.Select(p => new CreateGeekCrawlerPageItemCommand(
            p.Origin,
            p.Url,
            p.FinalUrl,
            p.StatusCode,
            p.RobotsAllowed,
            p.Html)).ToList();

        try
        {
            return await _repo.CreatePagesBatchAsync(
                new CreateGeekCrawlerPageBatchCommand(runId, items),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRetriableBatchError(ex) && items.Count > 1)
        {
            var approxBytes = items.Sum(i => (i.Html?.Length ?? 0) + i.Url.Length + 64);
            _logger.LogWarning(
                ex,
                "Geek-Crawler batch save failed for {Count} pages (~{ApproxKb} KB HTML); splitting.",
                items.Count,
                approxBytes / 1024);

            var mid = items.Count / 2;
            var first = await SavePagesWithRetryAsync(
                runId,
                batch.Take(mid).ToList(),
                ct).ConfigureAwait(false);
            var second = await SavePagesWithRetryAsync(
                runId,
                batch.Skip(mid).ToList(),
                ct).ConfigureAwait(false);

            var merged = first.Pages.Concat(second.Pages).ToList();
            return new GeekCrawlerPageBatchResult(merged.Count, merged);
        }
    }

    public async Task SaveLinksWithRetryAsync(
        Guid runId,
        IReadOnlyList<CreateGeekCrawlerLinkItemCommand> linkItems,
        CancellationToken ct)
    {
        if (linkItems.Count == 0)
            return;

        try
        {
            await _repo.CreateLinksBatchAsync(
                new CreateGeekCrawlerLinkBatchCommand(runId, linkItems),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRetriableBatchError(ex) && linkItems.Count > 1)
        {
            _logger.LogWarning(
                ex,
                "Geek-Crawler link batch save failed for {Count} links; splitting.",
                linkItems.Count);

            var mid = linkItems.Count / 2;
            await SaveLinksWithRetryAsync(runId, linkItems.Take(mid).ToList(), ct).ConfigureAwait(false);
            await SaveLinksWithRetryAsync(runId, linkItems.Skip(mid).ToList(), ct).ConfigureAwait(false);
        }
    }

    private static bool IsRetriableBatchError(Exception ex) =>
        ex is HttpRequestException or IOException or System.Net.Sockets.SocketException;
}
