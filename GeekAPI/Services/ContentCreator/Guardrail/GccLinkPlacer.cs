using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// Puts each link where the writer marked it, on the page the writer was actually given. The writer
/// marks a run with a target id; this resolves the id to that target's href, puts it on the run and
/// clears the id. The words under the link are the run's own words, so there is nothing to find and
/// nothing that can fail to be found.
/// </summary>
/// <remarks>
/// <para>
/// Every way the writer can still get a link wrong is a refusal here, by name, and nothing is repaired:
/// an id the prompt did not print, a linked run with no words in it, a linked run longer than
/// <see cref="GccDraftGuard.MaxLinkWords"/> words, and any href the writer typed itself -- on a run or a
/// section. The one href a document may carry before placement is the page's own scheduler link,
/// which the page builds (<c>GccClosing</c>), and that is passed in so it is recognised rather than refused.
/// </para>
/// <para>
/// The previous shape (2026-10-09, one day) had the paragraph carry <c>links: [{target, anchor}]</c>
/// with the anchor copied from the paragraph, and this class searched for it. The writer paraphrased
/// its own words -- "real-time dashboards" for a run beginning "Real-time dashboards", "Upflow syncs
/// with" for "Upflow natively syncs with" -- and seven pages were refused for anchors that were not
/// found. The link is now the run; the search is gone, and so is the refusal it produced.
/// </para>
/// <para>
/// Runs after parsing and before <c>GccDraftGuard</c>, which still checks every href and every
/// linked run's length. The guard is unchanged: placement is what makes its link checks pass by
/// construction rather than by the writer's compliance.
/// </para>
/// </remarks>
public static class GccLinkPlacer
{
    /// <summary>The document with its links placed, and every link that could not be, in the order found.</summary>
    public sealed record Placed(ContentDocument Document, IReadOnlyList<string> Refusals);

    public static Placed Place(ContentDocument document, GccLinkTargets targets, string? pageHref)
    {
        var refusals = new List<string>();
        var own = pageHref?.Trim();
        var lede = PlaceSection(document.Lede, targets, own, refusals);
        var sections = document.Sections.Select(section => PlaceSection(section, targets, own, refusals)).ToList();
        return new Placed(new ContentDocument(lede, sections), refusals);
    }

    private static Section PlaceSection(Section section, GccLinkTargets targets, string? own, List<string> refusals)
    {
        if (!string.IsNullOrWhiteSpace(section.Href) && !IsOwn(section.Href, own))
        {
            refusals.Add(
                $"The section \"{section.Heading}\" carries an href ({section.Href.Trim()}). A link is a run's "
                + "\"link\" naming a target printed in the prompt; the writer supplies no address.");
        }

        var paragraphs = section.Paragraphs
            .Select(paragraph => PlaceParagraph(paragraph, section.Heading, targets, own, refusals))
            .ToList();
        var children = section.Children
            .Select(child => PlaceSection(child, targets, own, refusals))
            .ToList();
        return section with { Paragraphs = paragraphs, Children = children };
    }

    private static Paragraph PlaceParagraph(
        Paragraph paragraph, string heading, GccLinkTargets targets, string? own, List<string> refusals)
    {
        switch (paragraph)
        {
            case TextParagraph text:
                return new TextParagraph(PlaceRuns(text.Runs, heading, targets, own, refusals));

            case ListParagraph list:
                return new ListParagraph(
                    list.Ordered,
                    list.Items.Select(item => PlaceRuns(item, heading, targets, own, refusals)).ToList());

            case QuoteParagraph quote:
                // A quotation's source is its cite, resolved from the candidate list; its runs carry no link.
                RefuseWriterHrefs(quote.Runs, heading, own, refusals);
                foreach (var run in quote.Runs.Where(run => !string.IsNullOrWhiteSpace(run.Link)))
                {
                    refusals.Add(
                        $"In \"{heading}\", the quotation's run \"{GccDraftGuard.Opening(run.Text)}...\" is marked as a link "
                        + $"to {run.Link!.Trim()}. A quotation's source is its cite; its words carry no link.");
                }

                return quote;

            default:
                return paragraph;
        }
    }

    /// <summary>
    /// The runs with every marked run's id resolved to its href, or left as written with the reason
    /// recorded. A run's text is never changed: the words under the link are the writer's own.
    /// </summary>
    private static IReadOnlyList<Run> PlaceRuns(
        IReadOnlyList<Run> runs, string heading, GccLinkTargets targets, string? own, List<string> refusals)
    {
        RefuseWriterHrefs(runs, heading, own, refusals);
        var placed = new List<Run>(runs.Count);
        foreach (var run in runs)
        {
            if (string.IsNullOrWhiteSpace(run.Link))
            {
                placed.Add(run);
                continue;
            }

            if (Resolve(run, heading, targets, own, refusals) is not { } target)
            {
                placed.Add(run);
                continue;
            }

            placed.Add(run with { Href = target.Href, Link = null });
        }

        return placed;
    }

    private static GccLinkTarget? Resolve(Run run, string heading, GccLinkTargets targets, string? own, List<string> refusals)
    {
        var id = run.Link!.Trim();
        if (!targets.TryGet(id, out var target))
        {
            var printed = targets.All.Count == 0
                ? "no target was printed for this page"
                : "the targets printed are " + string.Join(", ", targets.All.Select(t => t.Id));
            refusals.Add(
                $"In \"{heading}\", the run \"{GccDraftGuard.Opening(run.Text)}\" links to \"{id}\", which the prompt did not "
                + $"print; {printed}. A link may lead only to a page the writer was given.");
            return null;
        }

        var text = (run.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            refusals.Add($"In \"{heading}\", a run with no words links to {target.Id} ({target.Name}). A link sits on words.");
            return null;
        }

        var words = GccDraftGuard.WordCount(text);
        if (words > GccDraftGuard.MaxLinkWords)
        {
            refusals.Add(
                $"In \"{heading}\", the run linking to {target.Id} ({target.Name}) is {words} words: \"{GccDraftGuard.Opening(text)}...\". "
                + $"A link sits on the name of what it leads to, {GccDraftGuard.MaxLinkWords} words at most: split those words into "
                + "their own run and link that run alone.");
            return null;
        }

        // A run the writer both linked by id and gave an href is refused for the href already
        // (RefuseWriterHrefs); the id is not resolved on top of it. One on the page's own scheduler
        // link is the closing's, and a second destination on the same words is refused by name.
        if (!string.IsNullOrWhiteSpace(run.Href))
        {
            if (IsOwn(run.Href, own))
            {
                refusals.Add(
                    $"In \"{heading}\", the run \"{GccDraftGuard.Opening(text)}\" links to {target.Id} ({target.Name}) and is already "
                    + "the page's scheduler link. Words carry one link.");
            }

            return null;
        }

        return target;
    }

    private static void RefuseWriterHrefs(IEnumerable<Run> runs, string heading, string? own, List<string> refusals)
    {
        foreach (var run in runs)
        {
            if (string.IsNullOrWhiteSpace(run.Href) || IsOwn(run.Href, own)) continue;
            refusals.Add(
                $"In \"{heading}\", the run \"{GccDraftGuard.Opening(run.Text)}...\" carries an href ({run.Href.Trim()}). "
                + "A link is a run's \"link\" naming a target printed in the prompt; the writer supplies no address.");
        }
    }

    private static bool IsOwn(string href, string? own) =>
        !string.IsNullOrWhiteSpace(own) && string.Equals(href.Trim(), own, StringComparison.OrdinalIgnoreCase);
}
