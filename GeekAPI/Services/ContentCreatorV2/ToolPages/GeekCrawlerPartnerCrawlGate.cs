using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Partner;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

public static class GeekCrawlerPartnerCrawlGate
{
    public static bool RequiresOperatorCrawl(string? rawBriefJson) =>
        GccV2PartnerUrlResearchService.CollectOperatorSeedUrls(rawBriefJson).Count > 0;

    public static void ThrowIfDeferred(string? rawBriefJson, GeekCrawlerRunDto? run)
    {
        var required = RequiresOperatorCrawl(rawBriefJson) || run is not null;
        if (!required) return;
        if (run is null)
        {
            throw new GccV2ToolWriteDeferredException(
                "Partner crawl has not started; waiting for operator page fetch.");
        }

        if (string.Equals(run.Status, "complete", StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(run.Status, "failed", StringComparison.OrdinalIgnoreCase)) return;
        throw new GccV2ToolWriteDeferredException(
            $"Partner crawl is {run.Status}; tool pages wait for operator HTML fetch and quote extract to finish.");
    }

    public static void ThrowIfFailed(string? rawBriefJson, GeekCrawlerRunDto? run)
    {
        var required = RequiresOperatorCrawl(rawBriefJson) || run is not null;
        if (!required) return;
        if (run is null || !string.Equals(run.Status, "failed", StringComparison.OrdinalIgnoreCase)) return;
        throw new InvalidOperationException(
            run.ErrorSummary
            ?? "Partner crawl failed — no operator pages available for blockquote excerpts.");
    }
}
