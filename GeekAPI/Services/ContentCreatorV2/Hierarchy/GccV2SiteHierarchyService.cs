using System.Text.Json;
using System.Text.Json.Nodes;

namespace GeekAPI.Services.ContentCreatorV2.Hierarchy;

/// <summary>
/// Mobile homepage hierarchy → structured <c>siteHierarchy</c> on the brief.
/// Soft-fail: never invent a tree when fetch/browser fails.
/// </summary>
public sealed class GccV2SiteHierarchyService
{
    public const int HomepageOnlyMaxPages = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly GccV2PageFetcher _fetcher;
    private readonly ILogger<GccV2SiteHierarchyService> _logger;

    public GccV2SiteHierarchyService(
        GccV2PageFetcher fetcher,
        ILogger<GccV2SiteHierarchyService> logger)
    {
        _fetcher = fetcher;
        _logger = logger;
    }

    public async Task<GccV2SiteHierarchy?> BuildHomepageAsync(string? siteUrl, CancellationToken ct)
    {
        if (!GccV2HomepageUrl.TryNormalize(siteUrl, out var homepage))
        {
            _logger.LogDebug("Hierarchy skipped — could not normalize siteUrl '{SiteUrl}'", siteUrl);
            return null;
        }

        // Phase 1 queue policy: homepage only (MaxPages = 1). BFS unlocked later via SameOriginLinks.
        _ = HomepageOnlyMaxPages;

        var fetched = await _fetcher.FetchAsync(homepage, ct);
        if (fetched is null || string.IsNullOrWhiteSpace(fetched.Html))
        {
            _logger.LogWarning("Hierarchy soft-fail — no HTML for homepage {Homepage}", homepage);
            return null;
        }

        if (fetched.StatusCode is < 200 or >= 300)
        {
            _logger.LogWarning(
                "Hierarchy soft-fail — HTTP {Status} for homepage {Homepage}",
                fetched.StatusCode,
                homepage);
            return null;
        }

        var roots = GccV2HeadingTreeBuilder.Build(fetched.Html);
        return new GccV2SiteHierarchy(
            HomepageUrl: homepage,
            Viewport: GccV2CrawlerIdentity.ViewportLabel,
            BuiltAtUtc: DateTimeOffset.UtcNow,
            Pages:
            [
                new GccV2PageHierarchy(fetched.FinalUrl, roots),
            ]);
    }

    /// <summary>Merge structured siteHierarchy onto brief JSON (replace prior value).</summary>
    public static string? MergeIntoBriefJson(string? rawBriefJson, GccV2SiteHierarchy hierarchy)
    {
        JsonObject root;
        if (string.IsNullOrWhiteSpace(rawBriefJson))
        {
            root = new JsonObject();
        }
        else
        {
            try
            {
                root = JsonNode.Parse(rawBriefJson) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                root = new JsonObject();
            }
        }

        var node = JsonSerializer.SerializeToNode(hierarchy, JsonOpts);
        root["siteHierarchy"] = node;
        return root.ToJsonString(JsonOpts);
    }
}
