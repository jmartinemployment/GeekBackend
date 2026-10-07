using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The line every Content Creator page ends on, built by code: "Answer these questions when booking your
/// free consultation." with the booking words linked to the scheduler, then the operator's questions.
/// </summary>
/// <remarks>
/// <para>
/// <b>The writer does not write it</b> (Jeff, 2026-10-07: for each content type the page ends with "Answer
/// these questions when booking your free consultation", then the questions from the brief's "One question
/// per line", linked to <c>#consultationAppointment2xl</c>). The wording is fixed, so there was nothing for
/// the writer to decide, and it kept deciding it wrongly: on the Bill tool page it was told to end by
/// "asking for book_now" (the brief form's internal code, printed), wrote the questions three times (once as
/// a self-quiz the quiz check refused), and linked the words "#consultationAppointment2xl" instead of
/// words. The questions no longer reach the model at all, so they cannot be turned into a quiz.
/// </para>
/// <para>
/// It is made of <see cref="Paragraph"/> nodes, so <c>SectionHtmlRenderer</c> stays the only thing that
/// produces tags. It goes after the last body section's last paragraph, which is the last thing that
/// section renders, and before any section written outside the outline (the pillar's People Also Ask, the
/// tool page's FAQ).
/// </para>
/// <para>
/// With no scheduler anchor configured there is nowhere for a booking line to go, so there is no closing at
/// all, the same "this publisher has no scheduler" the closing-link check already honours. A blank wording
/// setting is the same: nothing is built, and the check reports the missing link rather than the page
/// shipping with a line that reads wrongly.
/// </para>
/// </remarks>
internal static class GccClosing
{
    /// <summary>
    /// The closing's paragraphs, or none when the publisher has no scheduler or no wording.
    /// </summary>
    internal static IReadOnlyList<Paragraph> Paragraphs(
        CompanyProfileOptions company, IReadOnlyList<string> questions)
    {
        var anchor = company.ConsultationAnchorHref?.Trim();
        if (string.IsNullOrWhiteSpace(anchor)) return [];

        var asked = questions.Select(question => question.Trim()).Where(question => question.Length > 0).ToList();
        if (asked.Count == 0)
        {
            var book = company.ConsultationClosingLinkWithoutQuestions?.Trim();
            return string.IsNullOrWhiteSpace(book)
                ? []
                : [new TextParagraph([new Run(book, Href: anchor), new Run(".")])];
        }

        var lead = company.ConsultationClosingLead?.Trim();
        var link = company.ConsultationClosingLink?.Trim();
        if (string.IsNullOrWhiteSpace(lead) || string.IsNullOrWhiteSpace(link)) return [];

        return
        [
            new TextParagraph([new Run($"{lead} "), new Run(link, Href: anchor), new Run(".")]),
            new ListParagraph(false, [.. asked.Select(question => (IReadOnlyList<Run>)[new Run(question)])]),
        ];
    }

    /// <summary>
    /// The sections with the closing at the end of the last one, in its deepest last descendant: the
    /// renderer writes a section's paragraphs before its children, so the closing goes where the page's
    /// last body content is, not ahead of a final subsection.
    /// </summary>
    internal static List<Section> AppendTo(IReadOnlyList<Section> sections, IReadOnlyList<Paragraph> closing)
    {
        var result = new List<Section>(sections);
        if (closing.Count == 0 || result.Count == 0) return result;

        result[^1] = WithClosingAtEnd(result[^1], closing);
        return result;
    }

    private static Section WithClosingAtEnd(Section section, IReadOnlyList<Paragraph> closing)
    {
        if (section.Children.Count == 0)
        {
            return section with { Paragraphs = [.. section.Paragraphs, .. closing] };
        }

        var last = section.Children.Count - 1;
        return section with
        {
            Children = [.. section.Children.Take(last), WithClosingAtEnd(section.Children[last], closing)],
        };
    }

    /// <summary>
    /// Takes the closing off a stored body so a revision can be written without it: the paragraphs from the
    /// last one that links <paramref name="anchorHref"/> to the end of the section they end, where that is
    /// the line and at most one list after it. The closing is the page's, not the writer's, so a revision
    /// must neither show it to the writer to be rewritten nor lose it.
    /// </summary>
    /// <returns>
    /// The sections without it, where it was (the index of the top-level section that ended with it) and what
    /// it was. When no section ends with a closing of this shape, the sections as they were and nothing else.
    /// </returns>
    internal static (List<Section> Sections, int? At, IReadOnlyList<Paragraph> Closing) Detach(
        IReadOnlyList<Section> sections, string? anchorHref)
    {
        var anchor = anchorHref?.Trim();
        if (string.IsNullOrWhiteSpace(anchor)) return ([.. sections], null, []);

        for (var index = sections.Count - 1; index >= 0; index--)
        {
            var (stripped, closing) = Strip(sections[index], anchor);
            if (closing.Count == 0) continue;

            var result = new List<Section>(sections) { [index] = stripped };
            return (result, index, closing);
        }

        return ([.. sections], null, []);
    }

    private static (Section Section, IReadOnlyList<Paragraph> Closing) Strip(Section section, string anchor)
    {
        if (section.Children.Count > 0)
        {
            var last = section.Children.Count - 1;
            var (child, closing) = Strip(section.Children[last], anchor);
            return closing.Count == 0
                ? (section, [])
                : (section with { Children = [.. section.Children.Take(last), child] }, closing);
        }

        var paragraphs = section.Paragraphs;
        var from = paragraphs.Count;
        // At most one list after the line; the line is the last text paragraph that links the anchor.
        for (var i = paragraphs.Count - 1; i >= 0 && i >= paragraphs.Count - 2; i--)
        {
            if (paragraphs[i] is TextParagraph text && text.Runs.Any(run => LinksAnchor(run, anchor)))
            {
                from = i;
                break;
            }
        }

        if (from == paragraphs.Count) return (section, []);
        if (paragraphs.Skip(from + 1).Any(paragraph => paragraph is not ListParagraph)) return (section, []);

        return (section with { Paragraphs = [.. paragraphs.Take(from)] }, [.. paragraphs.Skip(from)]);
    }

    private static bool LinksAnchor(Run run, string anchor) =>
        !string.IsNullOrWhiteSpace(run.Href)
        && string.Equals(run.Href.Trim(), anchor, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Puts a detached closing back at the end of the section at <paramref name="at"/> (the last section
    /// when the revision returned fewer than it had).
    /// </summary>
    internal static List<Section> Reattach(
        IReadOnlyList<Section> sections, int? at, IReadOnlyList<Paragraph> closing)
    {
        var result = new List<Section>(sections);
        if (at is null || closing.Count == 0 || result.Count == 0) return result;

        var index = Math.Min(at.Value, result.Count - 1);
        result[index] = WithClosingAtEnd(result[index], closing);
        return result;
    }
}
