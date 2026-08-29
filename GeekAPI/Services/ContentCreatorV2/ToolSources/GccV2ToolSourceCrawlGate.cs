using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.ToolSources;

public static class GccV2ToolSourceCrawlGate
{
    public static bool RequiresOperatorCrawl(string? rawBriefJson) =>
        GccV2PartnerUrlResearchService.CollectOperatorSeedUrls(rawBriefJson).Count > 0;

    public static bool BriefIncludesToolDraft(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("contentTypes", out var types)
                || types.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return types.EnumerateArray().Any(t =>
                t.ValueKind == JsonValueKind.String
                && string.Equals(t.GetString(), "tool", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static void ThrowIfDeferred(string? rawBriefJson, GccV2ToolSourceCrawlRunDto? run)
    {
        var required = RequiresOperatorCrawl(rawBriefJson) || run is not null;
        if (!required) return;
        if (run is null)
        {
            throw new ToolPages.GccV2ToolWriteDeferredException(
                "Tool source crawl has not started; waiting for vendor page fetch.");
        }

        if (string.Equals(run.Status, "complete", StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(run.Status, "failed", StringComparison.OrdinalIgnoreCase)) return;
        throw new ToolPages.GccV2ToolWriteDeferredException(
            $"Tool source crawl is {run.Status}; tool pages wait for vendor HTML fetch and quote extract to finish.");
    }

    public static void ThrowIfFailed(string? rawBriefJson, GccV2ToolSourceCrawlRunDto? run)
    {
        var required = RequiresOperatorCrawl(rawBriefJson) || run is not null;
        if (!required) return;
        if (run is null || !string.Equals(run.Status, "failed", StringComparison.OrdinalIgnoreCase)) return;
        throw new InvalidOperationException(
            run.ErrorSummary
            ?? "Tool source crawl failed — no vendor pages available for blockquote excerpts.");
    }
}
