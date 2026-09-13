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
        IReadOnlyList<GeekCrawlerExtractedLink> Links,
        string? FailureReason = null);

    public async Task CrawlOriginAsync(
        string origin,
        IReadOnlyList<string> seedUrls,
        Func<IReadOnlyList<CrawledPageResult>, Task> onBatchReady,
        CancellationToken ct,
        GeekCrawlerBfsResume? resume = null,
        IReadOnlyList<string>? extraSeedUrls = null,
        OriginCrawlLiveMetrics? liveMetrics = null,
        DateTimeOffset? deadlineUtc = null)
    {
        if (seedUrls.Count == 0) return;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri)) return;

        var normalizedOrigin = GeekCrawlerSeedNormalizer.NormalizeOriginAuthority(origin);
        var queue = new ConcurrentQueue<string>();
        var seen = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var pendingBatch = new List<CrawledPageResult>();
        var batchLock = new object();
        var inFlight = 0;

        void ThrowIfBudgetExceeded()
        {
            if (deadlineUtc is DateTimeOffset deadline && DateTimeOffset.UtcNow >= deadline)
            {
                throw new GeekCrawlerBudgetExceededException(
                    $"Crawl exceeded MaxCrawlDurationMinutes={GeekCrawlerCaps.MaxCrawlDurationMinutes}.");
            }

            if (liveMetrics is not null
                && liveMetrics.BytesFetched > GeekCrawlerCaps.MaxBytesFetchedPerRun)
            {
                throw new GeekCrawlerBudgetExceededException(
                    $"Crawl exceeded MaxBytesFetchedPerRun={GeekCrawlerCaps.MaxBytesFetchedPerRun}.");
            }
        }

        bool IsSameOrigin(Uri u) =>
            string.Equals(
                GeekCrawlerSeedNormalizer.NormalizeOriginAuthority(u.GetLeftPart(UriPartial.Authority)),
                normalizedOrigin,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(u.Scheme, originUri.Scheme, StringComparison.OrdinalIgnoreCase);

        void Enqueue(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return;
            if (!IsSameOrigin(u)) return;
            if (!GeekCrawlerSeedNormalizer.IsAllowedCrawlUri(u, out _)) return;
            if (seen.Count >= GeekCrawlerCaps.MaxUrlsPerRun) return;
            var key = GeekCrawlerUrlKeys.CrawlKey(u.AbsoluteUri);
            if (!seen.TryAdd(key, 0)) return;
            queue.Enqueue(u.AbsoluteUri);
        }

        foreach (var seed in seedUrls)
        {
            Enqueue(seed);
            if (GeekCrawlerHomepageUrl.TryNormalize(seed, out var homepage)
                && !string.Equals(
                    homepage.TrimEnd('/'),
                    seed.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                Enqueue(homepage);
            }
        }

        if (extraSeedUrls is not null)
        {
            foreach (var url in extraSeedUrls)
                Enqueue(url);
        }

        if (resume is not null)
        {
            foreach (var key in resume.PreSeenUrlKeys)
                seen.TryAdd(key, 0);

            foreach (var url in resume.QueueUrls)
                Enqueue(url);
        }

        var batchSaveSize = _options.SeedsOnly ? 1 : _options.BatchSaveSize;

        async Task FlushBatchIfReadyAsync()
        {
            if (liveMetrics is not null)
            {
                liveMetrics.QueueDepth = queue.Count;
                liveMetrics.InFlightCount = Volatile.Read(ref inFlight);
            }

            List<CrawledPageResult>? toFlush = null;
            lock (batchLock)
            {
                if (pendingBatch.Count >= batchSaveSize)
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
                ThrowIfBudgetExceeded();

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
                    ThrowIfBudgetExceeded();
                    if (seen.Count > GeekCrawlerCaps.MaxUrlsPerRun)
                    {
                        _logger.LogWarning(
                            "Geek-Crawler origin {Origin} hit MaxUrlsPerRun={Cap}; stopping enqueue expansion.",
                            origin, GeekCrawlerCaps.MaxUrlsPerRun);
                        break;
                    }
                    var fetched = await _fetcher.FetchAsync(url, ct).ConfigureAwait(false);
                    if (fetched.Html is not null)
                        liveMetrics?.AddBytes(System.Text.Encoding.UTF8.GetByteCount(fetched.Html));
                    ThrowIfBudgetExceeded();

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
                            links,
                            fetched.FailureReason));
                    }

                    foreach (var link in GeekCrawlerLinkExtractor.SameOriginLinksForQueue(links))
                    {
                        if (!_options.SeedsOnly)
                            Enqueue(link);
                    }

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
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (Exception ex) when (TryUnwrapBudgetExceeded(ex) is { } budget)
        {
            throw budget;
        }

        List<CrawledPageResult>? finalBatch;
        lock (batchLock)
        {
            finalBatch = pendingBatch.Count > 0 ? pendingBatch : null;
            pendingBatch = [];
        }

        if (finalBatch is not null)
            await onBatchReady(finalBatch).ConfigureAwait(false);

        _logger.LogInformation(
            "Geek-Crawler {Mode} for {Origin} finished after {Pages} page attempt(s) with parallelism {Parallelism}.",
            _options.SeedsOnly ? "seeds-only crawl" : "BFS",
            origin,
            seen.Count,
            workerCount);
    }

    private static GeekCrawlerBudgetExceededException? TryUnwrapBudgetExceeded(Exception ex)
    {
        if (ex is GeekCrawlerBudgetExceededException direct)
            return direct;
        if (ex is AggregateException aggregate)
        {
            return aggregate.Flatten().InnerExceptions
                .OfType<GeekCrawlerBudgetExceededException>()
                .FirstOrDefault();
        }

        return null;
    }
}
