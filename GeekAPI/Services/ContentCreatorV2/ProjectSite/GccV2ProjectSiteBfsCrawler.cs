using System.Collections.Concurrent;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;

namespace GeekAPI.Services.ContentCreatorV2.ProjectSite;

/// <summary>Same-origin BFS for owned project-site crawl using mobile Playwright fetch.</summary>
public sealed class GccV2ProjectSiteBfsCrawler
{
    private readonly GccV2PageFetcher _fetcher;
    private readonly GccV2ProjectSiteCrawlOptions _options;
    private readonly ILogger<GccV2ProjectSiteBfsCrawler> _logger;

    public GccV2ProjectSiteBfsCrawler(
        GccV2PageFetcher fetcher,
        GccV2ProjectSiteCrawlOptions options,
        ILogger<GccV2ProjectSiteBfsCrawler> logger)
    {
        _fetcher = fetcher;
        _options = options;
        _logger = logger;
    }

    public sealed record CrawledPageResult(
        string Origin,
        string Url,
        string FinalUrl,
        int StatusCode,
        bool RobotsAllowed,
        string? Html,
        IReadOnlyList<string> SameOriginLinks);

    public async Task CrawlSiteAsync(
        string siteUrl,
        Func<IReadOnlyList<CrawledPageResult>, Task> onBatchReady,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(siteUrl, UriKind.Absolute, out var originUri))
            return;

        var origin = originUri.GetLeftPart(UriPartial.Authority);
        var queue = new ConcurrentQueue<string>();
        var seen = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var pendingBatch = new List<CrawledPageResult>();
        var batchLock = new object();
        var inFlight = 0;
        var pagesCrawled = 0;

        void Enqueue(string url)
        {
            if (pagesCrawled >= _options.MaxPages) return;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return;
            if (!string.Equals(u.Host, originUri.Host, StringComparison.OrdinalIgnoreCase)) return;
            if (u.Scheme != originUri.Scheme) return;
            var key = u.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (!seen.TryAdd(key, 0)) return;
            queue.Enqueue(u.AbsoluteUri);
        }

        Enqueue(siteUrl);
        if (GccV2HomepageUrl.TryNormalize(siteUrl, out var homepageUrl)
            && !string.Equals(
                homepageUrl.TrimEnd('/'),
                siteUrl.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase))
        {
            Enqueue(homepageUrl);
        }

        async Task FlushBatchIfReadyAsync()
        {
            List<CrawledPageResult>? toFlush = null;
            lock (batchLock)
            {
                if (pendingBatch.Count >= GccV2ProjectSiteCrawlOptions.BatchSaveSize)
                {
                    toFlush = pendingBatch;
                    pendingBatch = [];
                }
            }

            if (toFlush is not null)
                await onBatchReady(toFlush).ConfigureAwait(false);
        }

        async Task WorkerAsync()
        {
            while (true)
            {
                if (pagesCrawled >= _options.MaxPages)
                {
                    if (Volatile.Read(ref inFlight) == 0)
                        break;
                    await Task.Yield();
                    continue;
                }

                if (!queue.TryDequeue(out var url))
                {
                    if (Volatile.Read(ref inFlight) == 0 && queue.IsEmpty)
                        break;

                    await Task.Yield();
                    continue;
                }

                Interlocked.Increment(ref inFlight);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (_options.HostDelayMs > 0)
                        await Task.Delay(_options.HostDelayMs, ct).ConfigureAwait(false);

                    var fetched = await _fetcher.FetchAsync(url, ct).ConfigureAwait(false);
                    if (fetched is null)
                    {
                        lock (batchLock)
                        {
                            pendingBatch.Add(new CrawledPageResult(
                                origin, url, url, 0, true, null, []));
                        }
                        continue;
                    }

                    var links = fetched.StatusCode is >= 200 and < 300
                        ? GccV2PageFetcher.ExtractSameOriginLinks(fetched.Html, fetched.FinalUrl)
                        : [];

                    lock (batchLock)
                    {
                        pendingBatch.Add(new CrawledPageResult(
                            origin,
                            fetched.RequestedUrl,
                            fetched.FinalUrl,
                            fetched.StatusCode,
                            true,
                            fetched.Html,
                            links));
                    }

                    Interlocked.Increment(ref pagesCrawled);
                    foreach (var link in links)
                        Enqueue(link);

                    await FlushBatchIfReadyAsync().ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                }
            }
        }

        var workerCount = Math.Max(1, _options.Parallelism);
        await Task.WhenAll(Enumerable.Range(0, workerCount).Select(_ => WorkerAsync())).ConfigureAwait(false);

        List<CrawledPageResult>? finalBatch;
        lock (batchLock)
        {
            finalBatch = pendingBatch.Count > 0 ? pendingBatch : null;
            pendingBatch = [];
        }

        if (finalBatch is not null)
            await onBatchReady(finalBatch).ConfigureAwait(false);

        _logger.LogInformation(
            "Project-site BFS for {Origin} finished after {Pages} page attempt(s).",
            origin,
            seen.Count);
    }
}
