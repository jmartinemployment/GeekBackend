using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.ContentCreator.Hierarchy;

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
    // The home page is read whole: every heading and every block of text outside the roster, each
    // in full (2026-10-12; Jeff: "STOP NOT USING DATA, EVEN IF THAT MEANS IT COSTS MORE"). Until then
    // it stopped at the page's 24th heading and 40th block and cut a block at 600 characters, so what
    // a longer home page says lower down -- the offer, the figures, the service area -- reached no
    // writer.

    /// <summary>A roster's label is a line ("Top 5 Automated Approval Workflow Tools:"), not prose that
    /// happens to say "best" and "tools" somewhere. Longer than this is never taken for one.</summary>
    private const int MaxRosterLabelChars = 120;

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
            // The home page's own roster of tools ("Top 5 Automated Approval Workflow Tools:" and the
            // names under it) is not read: the tools are entered in the brief form (Jeff, 2026-10-07),
            // and a page that printed a tool the project does not list beside the rule not to name it
            // is how the 2026-10-06 run named Tipalti. The label and what follows it, up to the next
            // heading, are the roster.
            var inRoster = false;
            foreach (var block in blocks.EnumerateArray())
            {
                var type = block.TryGetProperty("type", out var t) ? t.GetString() : null;
                var text = block.TryGetProperty("text", out var x) ? x.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;

                var isHeading = type == "heading";
                if (isHeading) inRoster = false;
                if (IsRosterLabel(text))
                {
                    inRoster = true;
                    continue;
                }

                if (inRoster) continue;

                if (isHeading) headings.Add(text);
                else paragraphs.Add(text);
            }
        }
        else if (!string.IsNullOrWhiteSpace(home.ContentHtml))
        {
            foreach (var node in GccHeadingTreeBuilder.Build(home.ContentHtml!))
            {
                CollectHeadings(node, headings);
            }
            if (!string.IsNullOrWhiteSpace(home.Excerpt)) paragraphs.Add(home.Excerpt!.Trim());
        }

        return headings.Count == 0 && paragraphs.Count == 0
            ? PublisherProfile.Empty
            : new PublisherProfile(headings, paragraphs);
    }

    private static bool IsRosterLabel(string text) =>
        text.Length <= MaxRosterLabelChars && GccToolsSectionGuard.EnumeratesTools(text);

    private static void CollectHeadings(GccHeadingNode node, List<string> into)
    {
        // A roster's heading takes its subtree with it: the headings under it are the tools.
        if (!string.IsNullOrWhiteSpace(node.HeadingText) && IsRosterLabel(node.HeadingText.Trim())) return;
        if (!string.IsNullOrWhiteSpace(node.HeadingText)) into.Add(node.HeadingText.Trim());
        foreach (var child in node.Children) CollectHeadings(child, into);
    }
}
