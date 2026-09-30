using GeekAPI.HttpClients;

namespace GeekAPI.Services.GeekCrawler;

internal static class GeekCrawlerEventMapper
{
    public static object MapRun(GeekCrawlerRunDto run, string? currentOrigin = null)
    {
        var snapshot = GeekCrawlerService.ToSnapshot(run);
        return new
        {
            runId = snapshot.RunId,
            crawlType = snapshot.CrawlType,
            status = snapshot.Status,
            seedUrls = snapshot.SeedUrls,
            hosts = snapshot.Hosts,
            errorSummary = snapshot.ErrorSummary,
            createdAtUtc = snapshot.CreatedAtUtc,
            startedAtUtc = snapshot.StartedAtUtc,
            completedAtUtc = snapshot.CompletedAtUtc,
            // Carried so the UI can render "content ready: no" beside "status: complete". Without
            // it the frame cannot show the field even as null, and a run that finished crawling but
            // produced nothing indexable looks identical to a good one until the defect resurfaces
            // much later as "its crawl extracted no content". ToSnapshot has always had it; the
            // live frame was the only hole.
            contentReadyAt = snapshot.ContentReadyAt,
            currentOrigin,
        };
    }
}
