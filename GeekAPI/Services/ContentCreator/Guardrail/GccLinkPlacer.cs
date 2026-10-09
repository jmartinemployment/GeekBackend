using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// Puts each paragraph's links where the writer said they go, on the page the writer was actually
/// given. The writer names a target id and the exact words the link sits on; this resolves the id and
/// splits the run so the href covers those words and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Every way the writer used to get a link wrong is a refusal here, by name, and nothing is repaired:
/// an id the prompt did not print, anchor words that are not in the paragraph, an anchor longer than
/// <see cref="GccDraftGuard.MaxLinkWords"/> words, an anchor that crosses a run boundary or lands on
/// words already linked, and any href the writer typed itself -- on a run or a section. The one href
/// a document may carry before placement is the page's own scheduler link, which the page builds
/// (<c>GccClosing</c>), and that is passed in so it is recognised rather than refused.
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
                $"The section \"{section.Heading}\" carries an href ({section.Href.Trim()}). A link is a \"links\" "
                + "entry naming a target printed in the prompt; the writer supplies no address.");
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
            {
                RefuseWriterHrefs(text.Runs, heading, own, refusals);
                var runs = text.Runs.ToList();
                foreach (var link in text.Links ?? [])
                {
                    if (Resolve(link, heading, targets, refusals) is not { } target) continue;
                    var placed = TryPlace(runs, link.Anchor.Trim(), target.Href);
                    if (placed.Runs is null)
                    {
                        refusals.Add(Refusal(heading, link, target, placed.Why!, runs));
                        continue;
                    }

                    runs = placed.Runs;
                }

                return new TextParagraph(runs);
            }

            case ListParagraph list:
            {
                foreach (var item in list.Items) RefuseWriterHrefs(item, heading, own, refusals);
                var items = list.Items.Select(item => item.ToList()).ToList();
                foreach (var link in list.Links ?? [])
                {
                    if (Resolve(link, heading, targets, refusals) is not { } target) continue;
                    var anchor = link.Anchor.Trim();
                    var placedIn = -1;
                    string? why = null;
                    for (var i = 0; i < items.Count && placedIn < 0; i++)
                    {
                        var placed = TryPlace(items[i], anchor, target.Href);
                        if (placed.Runs is not null)
                        {
                            items[i] = placed.Runs;
                            placedIn = i;
                        }
                        else if (placed.Why != NotInText)
                        {
                            // The words are in this item but cannot take the link: that is the answer.
                            why = placed.Why;
                            break;
                        }
                    }

                    if (placedIn < 0)
                    {
                        refusals.Add(Refusal(heading, link, target, why ?? "does not appear in any item of the list", items.SelectMany(i => i).ToList()));
                    }
                }

                return new ListParagraph(list.Ordered, items.Select(item => (IReadOnlyList<Run>)item).ToList());
            }

            case QuoteParagraph quote:
                // A quotation's source is its cite, resolved from the candidate list; its runs carry no link.
                RefuseWriterHrefs(quote.Runs, heading, own, refusals);
                return quote;

            default:
                return paragraph;
        }
    }

    private static GccLinkTarget? Resolve(LinkRef link, string heading, GccLinkTargets targets, List<string> refusals)
    {
        var id = (link.Target ?? string.Empty).Trim();
        if (!targets.TryGet(id, out var target))
        {
            var printed = targets.All.Count == 0
                ? "no target was printed for this page"
                : "the targets printed are " + string.Join(", ", targets.All.Select(t => t.Id));
            refusals.Add(
                $"In \"{heading}\", a link names the target \"{id}\", which the prompt did not print; {printed}. "
                + "A link may lead only to a page the writer was given.");
            return null;
        }

        var anchor = (link.Anchor ?? string.Empty).Trim();
        if (anchor.Length == 0)
        {
            refusals.Add($"In \"{heading}\", the link to {target.Id} ({target.Name}) names no anchor words.");
            return null;
        }

        var words = GccDraftGuard.WordCount(anchor);
        if (words > GccDraftGuard.MaxLinkWords)
        {
            refusals.Add(
                $"In \"{heading}\", the anchor for {target.Id} ({target.Name}) is {words} words: \"{GccDraftGuard.Opening(anchor)}...\". "
                + $"A link sits on the name of what it leads to, {GccDraftGuard.MaxLinkWords} words at most.");
            return null;
        }

        return target;
    }

    private const string NotInText = "does not appear in the paragraph";

    /// <summary>
    /// The runs with the href on the first occurrence of <paramref name="anchor"/>, or why it cannot be
    /// placed. The anchor must lie inside one run that carries no href: the writer's runs are its own
    /// sentence boundaries, and an anchor across two of them is not the name of one thing.
    /// </summary>
    private static (List<Run>? Runs, string? Why) TryPlace(IReadOnlyList<Run> runs, string anchor, string href)
    {
        var joined = string.Concat(runs.Select(run => run.Text));
        var start = joined.IndexOf(anchor, StringComparison.Ordinal);
        if (start < 0) return (null, NotInText);
        var end = start + anchor.Length;

        var offset = 0;
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            var runEnd = offset + run.Text.Length;
            if (start >= offset && start < runEnd)
            {
                if (end > runEnd) return (null, "crosses a run boundary");
                if (!string.IsNullOrWhiteSpace(run.Href)) return (null, "sits on words that are already linked");

                var result = new List<Run>(runs.Count + 2);
                result.AddRange(runs.Take(i));
                var before = run.Text[..(start - offset)];
                var after = run.Text[(end - offset)..];
                if (before.Length > 0) result.Add(new Run(before));
                result.Add(new Run(anchor, Href: href));
                if (after.Length > 0) result.Add(new Run(after));
                result.AddRange(runs.Skip(i + 1));
                return (result, null);
            }

            offset = runEnd;
        }

        return (null, NotInText);
    }

    private static string Refusal(string heading, LinkRef link, GccLinkTarget target, string why, IReadOnlyList<Run> runs) =>
        $"In \"{heading}\", the anchor \"{link.Anchor.Trim()}\" for {target.Id} ({target.Name}) {why}: "
        + $"\"{GccDraftGuard.Opening(string.Concat(runs.Select(run => run.Text)))}...\". "
        + "The anchor is the exact words in that paragraph the link sits on.";

    private static void RefuseWriterHrefs(IEnumerable<Run> runs, string heading, string? own, List<string> refusals)
    {
        foreach (var run in runs)
        {
            if (string.IsNullOrWhiteSpace(run.Href) || IsOwn(run.Href, own)) continue;
            refusals.Add(
                $"In \"{heading}\", the run \"{GccDraftGuard.Opening(run.Text)}...\" carries an href ({run.Href.Trim()}). "
                + "A link is a \"links\" entry naming a target printed in the prompt; the writer supplies no address.");
        }
    }

    private static bool IsOwn(string href, string? own) =>
        !string.IsNullOrWhiteSpace(own) && string.Equals(href.Trim(), own, StringComparison.OrdinalIgnoreCase);
}
