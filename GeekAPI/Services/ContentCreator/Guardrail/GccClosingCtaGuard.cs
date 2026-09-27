using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// Every page references the scheduler, as a link.
///
/// <para>
/// Jeff, 2026-09-27: "CTA is on every page and should be referenced". The scheduler is a shared
/// component the site renders on every page, so the ask is an in-page anchor and the draft has to
/// carry it -- <c>ClosingCallToActionInstruction</c> asks for exactly that, naming the href and
/// forbidding "visit our site", a contact page and an email address.
/// </para>
///
/// <para>
/// Asking is not having: the instruction was there, nothing checked, and a draft came back closing
/// on "book a consultation with our team" as plain text. A closing sentence with no link is a call
/// to action the reader cannot act on, and nothing further down would have noticed --
/// <c>SectionHtmlRenderer</c> renders a run without an href as ordinary prose, correctly, because
/// that is what it was given.
/// </para>
///
/// <para>
/// Every live type carries this -- pillar, blog and tool -- because the scheduler component is on
/// every page. Reported rather than refused: the href is a known constant that did not get attached
/// to a sentence, so nothing is invented either way, and an unlinked closing is something the
/// operator can see and fix.
/// </para>
/// </summary>
public static class GccClosingCtaGuard
{
    /// <summary>
    /// Violations, empty when the document links the anchor at least once. Never repairs: writing
    /// the href in afterwards would make the check pass on a closing the model did not actually
    /// write as an ask, which is the difference between a page that converts and a page with a link
    /// stapled to the end.
    /// </summary>
    public static IReadOnlyList<string> FindViolations(ContentDocument? document, string? anchorHref)
    {
        // No anchor configured is "this publisher has no scheduler", which the instruction already
        // handles by asking for no destination at all. Nothing to enforce.
        if (string.IsNullOrWhiteSpace(anchorHref)) return [];
        if (document is null) return ["The draft has no document, so it carries no call to action."];

        var anchor = anchorHref.Trim();
        if (LinksAnchor(document.Lede, anchor)) return [];
        foreach (var section in document.Sections)
        {
            if (LinksAnchor(section, anchor)) return [];
        }

        return
        [
            $"No run in the draft links {anchor}, so the closing asks for nothing the reader can act on. "
            + "The scheduler is on this page already; the ask has to be a link to it.",
        ];
    }

    /// <summary>
    /// What to tell the writer when the first attempt came back without the link.
    ///
    /// <para>
    /// One retry naming the omission, then the refusal stands -- the same treatment
    /// <c>GccRequiredToolMentions</c> gives a missing partner, and for the same reason: discarding a
    /// finished draft over something the model would fix if told is waste, and asking repeatedly
    /// until the answer comes back right is how unsupported work gets written.
    /// </para>
    ///
    /// <para>
    /// It shows the run shape rather than describing it. The instruction already said "put it on a
    /// run in the closing paragraph with href", and a draft still came back with the words and no
    /// link, so the thing worth repeating is the JSON.
    /// </para>
    /// </summary>
    public static string RetryInstruction(string anchorHref) =>
        $"OMITTED ON THE LAST ATTEMPT -- the closing did not link the scheduler. The last section "
        + "must end by asking the reader to book time, and that ask is a run carrying the href: "
        + $"{{\"text\": \"Book a free consultation.\", \"href\": \"{anchorHref}\"}} inside the closing "
        + "paragraph's runs. Write the wording that fits the piece, but the run has to carry that "
        + "exact href. Do not add a separate link paragraph at the end -- the ask is the closing "
        + "sentence, and the link is on it.";

    private static bool LinksAnchor(Section? section, string anchor)
    {
        if (section is null) return false;

        foreach (var paragraph in section.Paragraphs)
        {
            if (RunsLinkAnchor(RunsOf(paragraph), anchor)) return true;
        }

        foreach (var child in section.Children)
        {
            if (LinksAnchor(child, anchor)) return true;
        }

        return false;
    }

    /// <summary>
    /// Every run a paragraph kind carries. A list item is as good a home for the ask as a sentence
    /// is, and a quote is not -- but excluding one kind here would only mean a draft that put the
    /// link somewhere reasonable got refused for it.
    /// </summary>
    private static IEnumerable<Run> RunsOf(Paragraph paragraph) => paragraph switch
    {
        TextParagraph text => text.Runs,
        QuoteParagraph quote => quote.Runs,
        ListParagraph list => list.Items.SelectMany(item => item),
        DefinitionParagraph definition => definition.Items.SelectMany(item => item.Term.Concat(item.Definition)),
        _ => [],
    };

    private static bool RunsLinkAnchor(IEnumerable<Run> runs, string anchor) =>
        runs.Any(run => !string.IsNullOrWhiteSpace(run.Href)
            && string.Equals(run.Href!.Trim(), anchor, StringComparison.OrdinalIgnoreCase));
}
