using GeekAPI.HttpClients;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Rebuilds link rows from saved page HTML when links were lost (e.g. partial replace).</summary>
public sealed class GeekCrawlerLinkRebuilder
{
    private const int PageBatchSize = 25;
    private const int LinkBatchSize = 500;

    private readonly HttpGeekCrawlerRepository _repo;
    private readonly GeekCrawlerPageBatchWriter _batchWriter;
    private readonly ILogger<GeekCrawlerLinkRebuilder> _logger;

    public GeekCrawlerLinkRebuilder(
        HttpGeekCrawlerRepository repo,
        GeekCrawlerPageBatchWriter batchWriter,
        ILogger<GeekCrawlerLinkRebuilder> logger)
    {
        _repo = repo;
        _batchWriter = batchWriter;
        _logger = logger;
    }

    public async Task<int> RebuildMissingLinksAsync(Guid runId, CancellationToken ct)
    {
        var activity = await _repo.GetPageActivityAsync(runId, ct).ConfigureAwait(false);
        if (activity is null or { PageCount: 0 })
            return 0;

        var linkActivity = await _repo.GetLinkActivityAsync(runId, ct).ConfigureAwait(false);
        if (linkActivity is { LinkCount: > 0 })
            return 0;

        _logger.LogInformation(
            "Geek-Crawler rebuilding links for run {RunId} ({PageCount} pages, 0 links).",
            runId,
            activity.PageCount);

        var offset = 0;
        var totalInserted = 0;
        var pendingLinks = new List<CreateGeekCrawlerLinkItemCommand>();

        while (true)
        {
            var pages = await _repo.ListPagesAsync(runId, PageBatchSize, offset, ct).ConfigureAwait(false);
            if (pages.Count == 0)
                break;

            foreach (var page in pages)
            {
                if (string.IsNullOrWhiteSpace(page.Html))
                    continue;

                var extracted = GeekCrawlerLinkExtractor.ExtractAllLinks(
                    page.Html,
                    page.FinalUrl,
                    page.Origin);

                foreach (var link in extracted)
                {
                    pendingLinks.Add(new CreateGeekCrawlerLinkItemCommand(
                        page.Id,
                        page.FinalUrl,
                        link.LinkUrl,
                        link.IsSameOrigin));

                    if (pendingLinks.Count >= LinkBatchSize)
                    {
                        totalInserted += await FlushLinksAsync(runId, pendingLinks, ct).ConfigureAwait(false);
                        pendingLinks.Clear();
                    }
                }
            }

            offset += pages.Count;
            if (pages.Count < PageBatchSize)
                break;
        }

        if (pendingLinks.Count > 0)
            totalInserted += await FlushLinksAsync(runId, pendingLinks, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Geek-Crawler rebuilt {LinkCount} links for run {RunId}.",
            totalInserted,
            runId);

        return totalInserted;
    }

    private async Task<int> FlushLinksAsync(
        Guid runId,
        List<CreateGeekCrawlerLinkItemCommand> items,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return 0;

        await _batchWriter.SaveLinksWithRetryAsync(runId, items, ct).ConfigureAwait(false);
        return items.Count;
    }
}
