using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// What the operator's own site already says about them, read off their crawled home page.
///
/// <para>
/// The Create path handed the writer two config strings and the create's Notes field, so a page
/// about the operator's own subject was written by something that had never seen their site. It
/// invented a methodology because it had none, recommended buying criteria that contradicted
/// theirs, and closed on a vague suggestion because it did not know they offer a free consultation.
/// Jeff, 2026-09-23: "You spell out your own methodology instead of enforcing the one on page
/// 1/home?" and "it's common knowledge to reference existing items on the Home page, which this has
/// electronically".
/// </para>
///
/// <para>
/// The crawl was already there and already indexed. Nothing on this path read it, which is the same
/// shape as KnownCrawlTools being empty: the field exists, the data exists, the wire between them
/// does not.
/// </para>
/// </summary>
public sealed class GccPublisherProfileResolver(
    IGccProjectReader projects,
    IGccCrawlPageReader pages,
    ILogger<GccPublisherProfileResolver> logger)
{
    /// <summary>Enough of the home page to carry the framework and the proof points, not the whole site.</summary>
    private const int MaxHeadings = 24;
    private const int MaxParagraphs = 40;
    private const int MaxParagraphChars = 600;

    public sealed record PublisherProfile(
        IReadOnlyList<string> Headings,
        IReadOnlyList<string> Paragraphs)
    {
        public static readonly PublisherProfile Empty = new([], []);
        public bool IsEmpty => Headings.Count == 0 && Paragraphs.Count == 0;
    }

    /// <summary>
    /// The publisher's home page as headings and prose. Empty when the create belongs to no project,
    /// the project has no site run, or the page is not in the crawl -- all of which are "we do not
    /// know", never a reason to fail generation. Refusing here would block every create on a project
    /// whose site crawl has not landed yet.
    /// </summary>
    public async Task<PublisherProfile> ResolveAsync(Guid? projectId, CancellationToken ct = default)
    {
        if (projectId is not { } id) return PublisherProfile.Empty;

        var project = await projects.GetProjectAsync(id, ct);
        if (project?.ProjectSiteRunId is not { } runId || string.IsNullOrWhiteSpace(project.SiteUrl))
        {
            return PublisherProfile.Empty;
        }

        IReadOnlyList<GeekCrawlerPageDto> found;
        try
        {
            found = await pages.ListPagesBySeedsAsync(runId, [project.SiteUrl], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Publisher profile: could not read the home page for project {ProjectId}", id);
            return PublisherProfile.Empty;
        }

        var home = found.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ContentHtml) || p.Blocks is not null);
        if (home is null)
        {
            logger.LogInformation(
                "Publisher profile: {SiteUrl} is not in run {RunId}, so generation has no site grounding.",
                project.SiteUrl, runId);
            return PublisherProfile.Empty;
        }

        var headings = new List<string>();
        var paragraphs = new List<string>();

        // Typed blocks first -- the corpus format. ContentHtml is the fallback for a page crawled
        // before blocks were emitted.
        if (home.Blocks is { ValueKind: JsonValueKind.Array } blocks)
        {
            foreach (var block in blocks.EnumerateArray())
            {
                var type = block.TryGetProperty("type", out var t) ? t.GetString() : null;
                var text = block.TryGetProperty("text", out var x) ? x.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;

                if (type == "heading" && headings.Count < MaxHeadings) headings.Add(text);
                else if (paragraphs.Count < MaxParagraphs)
                    paragraphs.Add(text.Length > MaxParagraphChars ? text[..MaxParagraphChars] : text);
            }
        }
        else if (!string.IsNullOrWhiteSpace(home.ContentHtml))
        {
            foreach (var node in GccV2HeadingTreeBuilder.Build(home.ContentHtml!))
            {
                CollectHeadings(node, headings);
            }
            if (!string.IsNullOrWhiteSpace(home.Excerpt)) paragraphs.Add(home.Excerpt!.Trim());
        }

        return headings.Count == 0 && paragraphs.Count == 0
            ? PublisherProfile.Empty
            : new PublisherProfile(headings, paragraphs);
    }

    private static void CollectHeadings(GccV2HeadingNode node, List<string> into)
    {
        if (into.Count >= MaxHeadings) return;
        if (!string.IsNullOrWhiteSpace(node.HeadingText)) into.Add(node.HeadingText.Trim());
        foreach (var child in node.Children) CollectHeadings(child, into);
    }
}
