using System.Collections.Concurrent;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Unlimited same-origin BFS per host using mobile Playwright fetch.</summary>
public sealed class SameOriginBfsCrawler
{
    private readonly MobilePageFetcher _fetcher;
    private readonly GeekCrawlerOptions _options;
    private readonly ILogger<SameOriginBfsCrawler> _logger;

    public SameOriginBfsCrawler(
        MobilePageFetcher fetcher,
        GeekCrawlerOptions options,
        ILogger<SameOriginBfsCrawler> logger)
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
        IReadOnlyList<GeekCrawlerExtractedLink> Links);

    public async Task CrawlOriginAsync(
        string origin,
        IReadOnlyList<string> seedUrls,
        Func<IReadOnlyList<CrawledPageResult>, Task> onBatchReady,
        CancellationToken ct)
    {
        if (seedUrls.Count == 0) return;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri)) return;

        var queue = new ConcurrentQueue<string>();
        var seen = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var pendingBatch = new List<CrawledPageResult>();
        var batchLock = new object();
        var inFlight = 0;

        void Enqueue(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return;
            if (!string.Equals(u.Host, originUri.Host, StringComparison.OrdinalIgnoreCase)) return;
            if (u.Scheme != originUri.Scheme) return;
            var key = u.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (key.Length == 0) key = u.GetLeftPart(UriPartial.Authority);
            if (!seen.TryAdd(key, 0)) return;
            queue.Enqueue(u.AbsoluteUri);
        }

        foreach (var seed in seedUrls)
            Enqueue(seed);

        async Task FlushBatchIfReadyAsync()
        {
            List<CrawledPageResult>? toFlush = null;
            lock (batchLock)
            {
                if (pendingBatch.Count >= GeekCrawlerCaps.BatchSaveSize)
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
                    var fetched = await _fetcher.FetchAsync(url, ct).ConfigureAwait(false);
                    var links = fetched.Html is not null
                        ? GeekCrawlerLinkExtractor.ExtractAllLinks(fetched.Html, fetched.FinalUrl, origin)
                        : [];

                    lock (batchLock)
                    {
                        pendingBatch.Add(new CrawledPageResult(
                            origin,
                            fetched.Url,
                            fetched.FinalUrl,
                            fetched.StatusCode,
                            fetched.RobotsAllowed,
                            fetched.Html,
                            links));
                    }

                    foreach (var link in GeekCrawlerLinkExtractor.SameOriginLinksForQueue(links))
                        Enqueue(link);

                    await FlushBatchIfReadyAsync().ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                }
            }
        }

        var workerCount = Math.Max(1, _options.ParallelismPerOrigin);
        var workers = Enumerable.Range(0, workerCount).Select(_ => WorkerAsync()).ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);

        List<CrawledPageResult>? finalBatch;
        lock (batchLock)
        {
            finalBatch = pendingBatch.Count > 0 ? pendingBatch : null;
            pendingBatch = [];
        }

        if (finalBatch is not null)
            await onBatchReady(finalBatch).ConfigureAwait(false);

        _logger.LogInformation(
            "Geek-Crawler BFS for {Origin} finished after {Pages} page attempt(s) with parallelism {Parallelism}.",
            origin,
            seen.Count,
            workerCount);
    }
}
