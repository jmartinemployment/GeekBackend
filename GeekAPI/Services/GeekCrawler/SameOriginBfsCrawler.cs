using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Unlimited same-origin BFS per host using mobile Playwright fetch.</summary>
public sealed class SameOriginBfsCrawler
{
    private readonly MobilePageFetcher _fetcher;
    private readonly ILogger<SameOriginBfsCrawler> _logger;

    public SameOriginBfsCrawler(MobilePageFetcher fetcher, ILogger<SameOriginBfsCrawler> logger)
    {
        _fetcher = fetcher;
        _logger = logger;
    }

    public sealed record CrawledPageResult(
        string Origin,
        string Url,
        string FinalUrl,
        int StatusCode,
        bool RobotsAllowed,
        string? Html,
        IReadOnlyList<GeekCrawlerExtractedLink> Links);

    public async Task CrawlOriginAsync(
        string origin,
        IReadOnlyList<string> seedUrls,
        Func<IReadOnlyList<CrawledPageResult>, Task> onBatchReady,
        CancellationToken ct)
    {
        if (seedUrls.Count == 0) return;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri)) return;

        var queue = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingBatch = new List<CrawledPageResult>();

        void Enqueue(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return;
            if (!string.Equals(u.Host, originUri.Host, StringComparison.OrdinalIgnoreCase)) return;
            if (u.Scheme != originUri.Scheme) return;
            var key = u.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (key.Length == 0) key = u.GetLeftPart(UriPartial.Authority);
            if (!seen.Add(key)) return;
            queue.Enqueue(u.AbsoluteUri);
        }

        foreach (var seed in seedUrls)
            Enqueue(seed);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var url = queue.Dequeue();
            var fetched = await _fetcher.FetchAsync(url, ct).ConfigureAwait(false);
            var links = fetched.Html is not null
                ? GeekCrawlerLinkExtractor.ExtractAllLinks(fetched.Html, fetched.FinalUrl, origin)
                : [];

            pendingBatch.Add(new CrawledPageResult(
                origin,
                fetched.Url,
                fetched.FinalUrl,
                fetched.StatusCode,
                fetched.RobotsAllowed,
                fetched.Html,
                links));

            foreach (var link in GeekCrawlerLinkExtractor.SameOriginLinksForQueue(links))
                Enqueue(link);

            if (pendingBatch.Count >= GeekCrawlerCaps.BatchSaveSize)
            {
                await onBatchReady(pendingBatch).ConfigureAwait(false);
                pendingBatch = [];
            }
        }

        if (pendingBatch.Count > 0)
            await onBatchReady(pendingBatch).ConfigureAwait(false);

        _logger.LogInformation(
            "Geek-Crawler BFS for {Origin} finished after {Pages} page attempt(s).",
            origin,
            seen.Count);
    }
}
