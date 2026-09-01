using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.ProjectSite;

public static class GccV2ProjectSiteCrawlEventMapper
{
    public static object MapRun(GccV2ProjectSiteCrawlRunDto run, int? pageCount = null) =>
        new
        {
            runId = run.Id,
            siteUrl = run.SiteUrl,
            status = run.Status,
            errorSummary = run.ErrorSummary,
            pageCount,
            createdAtUtc = run.CreatedAtUtc,
            startedAtUtc = run.StartedAtUtc,
            completedAtUtc = run.CompletedAtUtc,
        };
}
