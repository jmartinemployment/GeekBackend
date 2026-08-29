using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekAPI.Services.ContentCreatorV2.Polite;

namespace GeekAPI.Services.ContentCreatorV2.ToolSources;

/// <summary>
/// Pass 1 — unlimited same-origin BFS per host using polite HTTP fetch (robots-gated).
/// </summary>
public sealed class GccV2SameOriginBfsCrawler
{
    private readonly IGccV2PoliteCrawler _crawler;
    private readonly ILogger<GccV2SameOriginBfsCrawler> _logger;

    public GccV2SameOriginBfsCrawler(IGccV2PoliteCrawler crawler, ILogger<GccV2SameOriginBfsCrawler> logger)
    {
        _crawler = crawler;
        _logger = logger;
    }

    public sealed record CrawledPageResult(
        string Origin,
        string Url,
        string FinalUrl,
        int StatusCode,
        bool RobotsAllowed,
        string? Html,
        string Status);

    public async Task<IReadOnlyList<CrawledPageResult>> CrawlOriginAsync(
        string origin,
        IReadOnlyList<string> seedUrls,
        CancellationToken ct)
    {
        if (seedUrls.Count == 0) return [];

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return [];

        var queue = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<CrawledPageResult>();

        void Enqueue(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return;
            if (!string.Equals(u.Host, originUri.Host, StringComparison.OrdinalIgnoreCase)) return;
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
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;

            var fetch = await _crawler.GetHtmlAsync(uri, ct).ConfigureAwait(false);
            if (fetch.Status == GccV2PoliteFetchResult.Statuses.BlockedByRobots)
            {
                results.Add(new CrawledPageResult(origin, url, url, 0, false, null, fetch.Status));
                continue;
            }

            if (!fetch.HasHtml)
            {
                results.Add(new CrawledPageResult(origin, url, url, 0, true, null, fetch.Status));
                continue;
            }

            results.Add(new CrawledPageResult(origin, url, url, 200, true, fetch.Html, fetch.Status));

            foreach (var link in GccV2PageFetcher.ExtractSameOriginLinks(fetch.Html!, url))
                Enqueue(link);
        }

        _logger.LogInformation(
            "BFS crawl for {Origin}: {PageCount} page attempt(s), {HtmlCount} with HTML.",
            origin,
            results.Count,
            results.Count(r => !string.IsNullOrWhiteSpace(r.Html)));

        return results;
    }
}
