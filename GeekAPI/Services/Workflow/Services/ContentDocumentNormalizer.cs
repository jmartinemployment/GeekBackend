using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.Workflow.Services;

/// <summary>
/// Post-generation structural repair, applied in code so the model cannot leave the defect behind.
/// Same ownership rule as <see cref="ContentDocumentText.AssignSectionIds"/> and
/// <see cref="PillarHeadingContract"/>: the shape of the document is decided here, not by the model.
/// <para>
/// Both passes are exact-match, never fuzzy. Duplicate copy is removed only when the text is
/// character-identical to text that survives elsewhere, and a link is narrowed only when the tool
/// name is found verbatim inside the anchor. Anything uncertain is left exactly as generated.
/// </para>
/// </summary>
public static class ContentDocumentNormalizer
{
    /// <summary>Anchor text at or below this length is a name, not a swallowed sentence.</summary>
    private const int MaxReasonableAnchorLength = 60;

    public static ContentDocument Normalize(ContentDocument document) =>
        document with
        {
            Lede = NormalizeSection(document.Lede),
            Sections = document.Sections.Select(NormalizeSection).ToList(),
        };

    private static Section NormalizeSection(Section section)
    {
        var children = section.Children.Select(NormalizeSection).ToList();

        // Defect: the model writes a subsection twice — inline in the parent's paragraphs (with a
        // <p><strong>…</strong></p> pseudo-heading it had no schema slot for) and again as a real
        // child with identical prose. The child copy is complete and correctly nested, so the
        // flattened one is the accident and dropping it loses nothing.
        var descendantText = new HashSet<string>(StringComparer.Ordinal);
        var descendantHeadings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in children)
        {
            CollectText(child, descendantText, descendantHeadings);
        }

        var paragraphs = section.Paragraphs
            .Where(p => !DuplicatesDescendant(p, descendantText, descendantHeadings))
            .Select(NarrowWholeElementLinks)
            .ToList();

        return section with { Paragraphs = paragraphs, Children = children };
    }

    private static void CollectText(Section section, ISet<string> text, ISet<string> headings)
    {
        if (!string.IsNullOrWhiteSpace(section.Heading))
        {
            headings.Add(Normalized(section.Heading));
        }

        foreach (var paragraph in section.Paragraphs)
        {
            foreach (var line in Lines(paragraph))
            {
                text.Add(line);
            }
        }

        foreach (var child in section.Children)
        {
            CollectText(child, text, headings);
        }
    }

    private static bool DuplicatesDescendant(
        Paragraph paragraph, ICollection<string> descendantText, ICollection<string> descendantHeadings)
    {
        var lines = Lines(paragraph).ToList();
        if (lines.Count == 0)
        {
            return false;
        }

        // The pseudo-heading: an all-bold paragraph whose text is a child's heading.
        if (paragraph is TextParagraph { Runs: { Count: > 0 } runs }
            && runs.All(r => r.Bold)
            && descendantHeadings.Contains(lines[0]))
        {
            return true;
        }

        return lines.All(descendantText.Contains);
    }

    /// <summary>
    /// Defect: an <c>&lt;a&gt;</c> that swallows a whole paragraph or list item, because the model
    /// set <see cref="Run.Href"/> on the run carrying the entire sentence instead of the one
    /// carrying the tool name. A 60-word hyperlink reads badly and hands Google sentence-length
    /// anchor text pointing at a product page.
    /// <para>
    /// The tool name is recovered from the href's own last segment, so no external lookup is
    /// needed: the link is split into unlinked text, the name, and unlinked text. When the name
    /// cannot be found verbatim the run is left untouched — narrowing is a repair, and dropping a
    /// link the operator cannot see would be worse than a long one.
    /// </para>
    /// </summary>
    private static Paragraph NarrowWholeElementLinks(Paragraph paragraph) => paragraph switch
    {
        TextParagraph t => new TextParagraph(NarrowRuns(t.Runs)),
        ListParagraph l => new ListParagraph(l.Ordered, l.Items.Select(NarrowRuns).ToList()),
        _ => paragraph,
    };

    private static IReadOnlyList<Run> NarrowRuns(IReadOnlyList<Run> runs)
    {
        if (runs.Count == 0)
        {
            return runs;
        }

        var narrowed = new List<Run>();
        var changed = false;

        foreach (var run in runs)
        {
            if (string.IsNullOrWhiteSpace(run.Href)
                || run.Text.Length <= MaxReasonableAnchorLength
                || SplitOnToolName(run) is not { } parts)
            {
                narrowed.Add(run);
                continue;
            }

            narrowed.AddRange(parts);
            changed = true;
        }

        return changed ? narrowed : runs;
    }

    /// <summary>Splits an over-long anchor into before / name / after, or null when the name the
    /// href points at does not appear verbatim in the text.</summary>
    private static IReadOnlyList<Run>? SplitOnToolName(Run run)
    {
        var segment = run.Href!.TrimEnd('/').Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(segment))
        {
            return null;
        }

        var match = FindSegmentSpan(run.Text, segment);
        if (match is not var (start, length) || length == 0)
        {
            return null;
        }

        var parts = new List<Run>();
        if (start > 0)
        {
            parts.Add(run with { Text = run.Text[..start], Href = null });
        }

        parts.Add(run with { Text = run.Text.Substring(start, length) });

        var end = start + length;
        if (end < run.Text.Length)
        {
            parts.Add(run with { Text = run.Text[end..], Href = null });
        }

        return parts;
    }

    /// <summary>Finds the shortest run of words whose slug equals the href segment, so
    /// "/tools/marketing/jasper-ai" locates "Jasper AI" inside the sentence regardless of spacing
    /// or punctuation between the words.</summary>
    private static (int Start, int Length)? FindSegmentSpan(string text, string segment)
    {
        var target = SlugHelper.Slugify(segment);
        if (target.Length == 0)
        {
            return null;
        }

        var wordStarts = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i]) && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))
            {
                wordStarts.Add(i);
            }
        }

        foreach (var start in wordStarts)
        {
            for (var end = start + 1; end <= text.Length; end++)
            {
                // Only consider spans ending on a word boundary.
                if (end < text.Length && char.IsLetterOrDigit(text[end]))
                {
                    continue;
                }

                var candidate = text[start..end];
                if (candidate.Length > target.Length * 3)
                {
                    break;
                }

                if (SlugHelper.Slugify(candidate) == target)
                {
                    return (start, candidate.Length);
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> Lines(Paragraph paragraph) => paragraph switch
    {
        TextParagraph t => new[] { Normalized(string.Concat(t.Runs.Select(r => r.Text))) },
        ListParagraph l => l.Items.Select(item => Normalized(string.Concat(item.Select(r => r.Text)))),
        _ => [],
    };

    private static string Normalized(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
