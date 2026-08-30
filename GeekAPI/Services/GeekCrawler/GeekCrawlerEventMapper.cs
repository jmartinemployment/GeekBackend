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
            currentOrigin,
        };
    }
}
