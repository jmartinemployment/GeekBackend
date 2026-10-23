using System.Text;
using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>A block quotation kept on the page: its words, the page they are on, and the listed span it was chosen by, when it was.</summary>
/// <param name="Heading">The heading of the section it stands under.</param>
/// <param name="Text">The page's own characters.</param>
/// <param name="Url">The page, written as the quotation's cite.</param>
/// <param name="Span">The number of the listed span, for the tool page's quotation of its partner; null for a quotation found by its words.</param>
public sealed record GccKeptQuotation(string Heading, string Text, string Url, int? Span);

/// <summary>A block quotation deleted from the page: what the writer typed, the heading it stood under, and why it went.</summary>
public sealed record GccDeletedQuotation(string Heading, string Text, string Why);

/// <summary>
/// Every block quotation on a page is the source's own words on a page the writer was shown, cited to
/// that page by code, or it is deleted and the page is kept.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two kinds of quotation, one rule.</b> The tool page's quotation of its partner (Jeff, 2026-09-26:
/// "I want a blockquote in each tool") is chosen by number from the spans <c>GccQuoteCandidates</c> cut
/// and listed, and the number is the whole answer: the words and the cite are read back from the list,
/// so nothing the writer typed beside the number reaches the page. A point of law (Jeff, 2026-10-22:
/// "such a passage should be contained within a blockquote with cite url") is written out, on the
/// pillar, the blog and the tool page alike, and is found by its words on one of the pages the writer
/// was shown: the partners' pages, then the publisher's own, never a competitor's. Its cite is that
/// page's address, written here. The writer writes no address, and one it types is never used.
/// </para>
/// <para>
/// <b>What matches nothing is deleted, and the page is kept</b> (Jeff, 2026-10-22: "delete the
/// invented quotation, do not make me pay for the page and get nothing!", then "and blog posts").
/// Until then an invented or misattributed quotation refused the whole tool page, and any quotation at
/// all refused a pillar or a blog. Nothing is sent a second time, so each refusal was a page lost for one
/// paragraph. The quotation goes whole; the paragraphs either side stay; a heading under another that
/// is left with nothing under it goes with it; and every deletion is named for the operator with the
/// heading it stood under.
/// </para>
/// <para>
/// <b>The words are the page's, not the writer's.</b> A quotation chosen by number is the span's own
/// string. A quotation written out is matched to a passage with whitespace, quotation marks, dashes and
/// case set aside, and the page's own characters for that stretch are what is kept: a model echoing
/// <c>We’ve</c> emits <c>We've</c>, and the reader is owed the source's text. Nothing inside the words
/// is repaired: a quotation that differs in a word from every passage matches none and is deleted. The
/// retyping that this guards against is real: a shortened span with an ellipsis added lost AvidXchange's
/// page, and a retyped sentence lost Stampli's, before the partner's quotation was chosen by number.
/// </para>
/// <para>
/// <b>One quotation of the partner by number.</b> A second quotation chosen by number on a tool page is
/// deleted and named: the first batch is asked for the page's one, and every later batch is told it is
/// written elsewhere. A point of law's quotation is not counted against it.
/// </para>
/// <para>
/// <b>Pages are searched before the listed spans</b> for a quotation written out. The spans are
/// sentences of the same pages, and a quotation the writer typed rather than numbered is, by its
/// instruction, a point of law's. Matching it to a span first would make it the partner's one quotation
/// and delete a real one chosen by number as a second. A listed span typed out all the same is still
/// found, on its page or in the list.
/// </para>
/// <para>
/// This runs on the opening and the body as the writer returned them, before the keyword remap, the
/// linker, the citations, the closing and the FAQ. <c>GccDraftGuard</c> then refuses a quotation whose
/// cite is not an evidence page's address, which after this step is none: the refusal is the boundary,
/// this is what keeps the page from reaching it.
/// </para>
/// </remarks>
public static class GccQuotations
{
    /// <summary>What a page's quotations are matched against.</summary>
    /// <param name="Candidates">The numbered spans a tool page's partner quotation is chosen from. Null on a
    /// page that lists none: the pillar, the blog, and a tool page written with no create.</param>
    /// <param name="Pages">The pages the writer was shown and may quote: the partners' pages, then the
    /// publisher's own. Never a competitor's.</param>
    /// <param name="PartnerName">The tool page's partner, for the line that names a second quotation of it.
    /// Null on the pillar and the blog.</param>
    public sealed record Evidence(
        IReadOnlyList<GccQuoteCandidate>? Candidates,
        IReadOnlyList<GccQuoteablePage> Pages,
        string? PartnerName);

    /// <summary>The opening and the body with every quotation resolved, what was kept, and what was deleted.</summary>
    /// <param name="Headings">The headings that went with a deleted quotation, each having had nothing else under it.</param>
    public sealed record Result(
        Section Lede,
        IReadOnlyList<Section> Sections,
        IReadOnlyList<GccKeptQuotation> Kept,
        IReadOnlyList<GccDeletedQuotation> Deleted,
        IReadOnlyList<string> Headings);

    public const string OnNoPage = "its words are on no page the writer was shown";
    public const string IsEmpty = "it has no words";

    public static string NamesNoListedSpan(int id, int listed) =>
        $"it names quotable span {id}, but {listed} were listed";

    public static string ASecondOfThePartner(string partner) =>
        $"the page carries one quotation of {partner} chosen by number, and this was a second";

    /// <summary>A page's own characters for a quotation's words, and the page.</summary>
    private sealed record Found(string Text, string Url);

    private sealed class State(Evidence evidence)
    {
        public IReadOnlyList<GccQuoteCandidate> Candidates { get; } = evidence.Candidates ?? [];
        public IReadOnlyList<GccQuoteablePage> Pages { get; } = evidence.Pages;
        public string Partner { get; } = string.IsNullOrWhiteSpace(evidence.PartnerName) ? "the partner" : evidence.PartnerName.Trim();
        public bool PartnerQuotationKept { get; set; }
        public List<GccKeptQuotation> Kept { get; } = [];
        public List<GccDeletedQuotation> Deleted { get; } = [];
        public List<string> Headings { get; } = [];
    }

    /// <summary>
    /// <paramref name="lede"/> and <paramref name="sections"/> with every block quotation resolved: the
    /// words and the cite the page's, or the quotation gone.
    /// </summary>
    public static Result Resolve(Section lede, IReadOnlyList<Section> sections, Evidence evidence)
    {
        var state = new State(evidence);
        // The opening and the page's own sections stay whatever happens under them: one left with
        // nothing is put back as written.
        var resolvedLede = Clean(lede, state, mayGo: false)!;
        var resolvedSections = new List<Section>(sections.Count);
        foreach (var section in sections)
        {
            resolvedSections.Add(Clean(section, state, mayGo: false)!);
        }

        return new Result(resolvedLede, resolvedSections, state.Kept, state.Deleted, state.Headings);
    }

    /// <summary>
    /// What the operator is told when quotations were deleted from a page: how many, each with its words,
    /// the heading it stood under and why it went. One line for a page's quotations together; null when
    /// none went.
    /// </summary>
    /// <param name="type">"pillar", "blog" or "tool page".</param>
    public static string? Line(string type, Result result)
    {
        if (result.Deleted.Count == 0) return null;

        static string Words(GccDeletedQuotation quotation) =>
            quotation.Text.Length == 0 ? "a quotation with no words of its own" : $"\"{quotation.Text}\"";

        var deleted = result.Deleted;
        var line = deleted.Count == 1
            ? $"The {type}: 1 block quotation was deleted because {deleted[0].Why}: {Words(deleted[0])}, under \"{deleted[0].Heading}\"."
            : $"The {type}: {deleted.Count} block quotations were deleted: "
              + string.Join("; ", deleted.Select(q => $"{Words(q)} under \"{q.Heading}\" ({q.Why})")) + ".";
        if (result.Headings.Count > 0)
        {
            var named = string.Join(", ", result.Headings.Select(heading => $"\"{heading}\""));
            line += result.Headings.Count == 1
                ? $" The heading {named} went with it, having nothing else under it."
                : $" The headings {named} went with them, having nothing else under them.";
        }

        return line + " The rest of the page is as written.";
    }

    /// <summary>
    /// Why a tool page carries no block quotation of its partner, or null when it carries one. A reported
    /// gap, not a refusal, since 2026-10-08 (Jeff: do not fail a tool page when no fitting quotation was
    /// found): the page ships and the absence is named.
    /// </summary>
    /// <remarks>
    /// A quotation of the partner is one cited to a page on the partner's host: chosen by number from the
    /// listed spans, or a point of law written out from one of the partner's own pages. Both are the
    /// partner's words in a quote box, which is what the tool page is asked to carry.
    /// </remarks>
    public static string? MissingPartnerQuotation(ContentDocument document, IReadOnlyList<GccQuoteCandidate>? candidates)
    {
        if (candidates is null) return null;

        var partnerHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var host = GccRequiredToolMentions.HostKeyOf(candidate.PageUrl ?? string.Empty);
            if (host.Length > 0) partnerHosts.Add(host);
        }

        foreach (var quote in Quotes(document))
        {
            var host = GccRequiredToolMentions.HostKeyOf(quote.Cite ?? string.Empty);
            if (host.Length > 0 && partnerHosts.Contains(host)) return null;
        }

        return candidates.Count == 0
            ? "The page carries no block quotation of the partner, and nothing retrieved from the partner's "
              + "pages is shaped like one -- no complete sentence outside boilerplate."
            : $"The page carries no block quotation of the partner. {candidates.Count} quotable partner span(s) were supplied and none was used.";
    }

    /// <summary>
    /// <paramref name="section"/> with its quotations resolved, or null when it is left with nothing
    /// under its heading and <paramref name="mayGo"/>: a heading over nothing is not left on a page.
    /// </summary>
    private static Section? Clean(Section section, State state, bool mayGo)
    {
        var deletedBefore = state.Deleted.Count;
        var changed = false;
        var paragraphs = new List<Paragraph>(section.Paragraphs.Count);
        foreach (var paragraph in section.Paragraphs)
        {
            if (paragraph is not QuoteParagraph quote)
            {
                paragraphs.Add(paragraph);
                continue;
            }

            var judged = Judge(quote, section.Heading, state);
            changed |= !ReferenceEquals(judged, quote);
            if (judged is not null) paragraphs.Add(judged);
        }

        var children = new List<Section>(section.Children.Count);
        foreach (var child in section.Children)
        {
            var cleaned = Clean(child, state, mayGo: true);
            changed |= !ReferenceEquals(cleaned, child);
            if (cleaned is not null) children.Add(cleaned);
        }

        if (!changed) return section;
        if (mayGo && paragraphs.Count == 0 && children.Count == 0 && state.Deleted.Count > deletedBefore)
        {
            state.Headings.Add(section.Heading);
            return null;
        }

        return section with { Paragraphs = paragraphs, Children = children };
    }

    /// <summary>The quotation as it goes on the page, or null when it goes.</summary>
    private static QuoteParagraph? Judge(QuoteParagraph quote, string heading, State state)
    {
        var typed = CollapseWhitespace(string.Concat(quote.Runs.Select(run => run.Text ?? string.Empty)));

        // Chosen by number. The number is the whole answer: a number that names nothing is a quotation
        // of nothing, not a fall-through to whatever was typed beside it.
        if (quote.Candidate is { } id)
        {
            if (id < 1 || id > state.Candidates.Count)
            {
                state.Deleted.Add(new GccDeletedQuotation(heading, typed, NamesNoListedSpan(id, state.Candidates.Count)));
                return null;
            }

            return KeepSpan(state.Candidates[id - 1], heading, state);
        }

        var words = Unquote(Fold(typed).Folded);
        if (words.Length == 0)
        {
            state.Deleted.Add(new GccDeletedQuotation(heading, typed, IsEmpty));
            return null;
        }

        // Written out: a point of law's, found on a page the writer was shown.
        if (FindOnAPage(words, state.Pages) is { } found)
        {
            state.Kept.Add(new GccKeptQuotation(heading, found.Text, found.Url, null));
            return new QuoteParagraph([new Run(found.Text)], found.Url);
        }

        // A listed span typed out rather than numbered: the partner's quotation all the same.
        var span = state.Candidates.FirstOrDefault(
            candidate => Fold(candidate.Text).Folded.Contains(words, StringComparison.OrdinalIgnoreCase));
        if (span is not null) return KeepSpan(span, heading, state);

        state.Deleted.Add(new GccDeletedQuotation(heading, typed, OnNoPage));
        return null;
    }

    /// <summary>The partner's quotation, the span's own words and page; or nothing, when the page already carries one.</summary>
    private static QuoteParagraph? KeepSpan(GccQuoteCandidate span, string heading, State state)
    {
        if (state.PartnerQuotationKept)
        {
            state.Deleted.Add(new GccDeletedQuotation(heading, span.Text, ASecondOfThePartner(state.Partner)));
            return null;
        }

        state.PartnerQuotationKept = true;
        state.Kept.Add(new GccKeptQuotation(heading, span.Text, span.PageUrl, span.Id));
        // One run: the span is one string, and splitting it across runs would invent emphasis the
        // partner's page never had.
        return new QuoteParagraph([new Run(span.Text)], span.PageUrl);
    }

    /// <summary>
    /// The first page, in the order the writer was shown them, with a passage that carries
    /// <paramref name="words"/>, and that passage's own characters for them.
    /// </summary>
    private static Found? FindOnAPage(string words, IReadOnlyList<GccQuoteablePage> pages)
    {
        foreach (var page in pages)
        {
            if (page is null || string.IsNullOrWhiteSpace(page.Url)) continue;
            foreach (var passage in page.Paragraphs ?? [])
            {
                if (string.IsNullOrWhiteSpace(passage)) continue;
                var (folded, map) = Fold(passage);
                var at = folded.IndexOf(words, StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;

                var start = map[at];
                var end = map[at + words.Length - 1] + 1;
                return new Found(CollapseWhitespace(passage[start..end]), page.Url.Trim());
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="text"/> with whitespace runs as one space, curly quotation marks straight, an en
    /// or em dash a hyphen and an ellipsis three stops, and for each character of the result the index of
    /// the character it came from, so a match can be read back as the text's own characters.
    /// </summary>
    private static (string Folded, int[] Map) Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        var spacePending = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                spacePending = true;
                continue;
            }

            if (spacePending)
            {
                if (folded.Length > 0)
                {
                    folded.Append(' ');
                    map.Add(i - 1);
                }

                spacePending = false;
            }

            switch (c)
            {
                case '‘' or '’':
                    folded.Append('\'');
                    map.Add(i);
                    break;
                case '“' or '”':
                    folded.Append('"');
                    map.Add(i);
                    break;
                case '–' or '—':
                    folded.Append('-');
                    map.Add(i);
                    break;
                case '…':
                    folded.Append("...");
                    map.Add(i);
                    map.Add(i);
                    map.Add(i);
                    break;
                default:
                    folded.Append(c);
                    map.Add(i);
                    break;
            }
        }

        return (folded.ToString(), map.ToArray());
    }

    /// <summary>
    /// A typed quotation with its wrapping quotation marks and any trailing ellipsis removed. Only the
    /// edges: these are the two things a model adds while copying, and nothing inside is touched.
    /// </summary>
    private static string Unquote(string folded)
    {
        var trimmed = folded.Trim();
        string previous;
        // Until stable, because the two wrappers nest: "...nine days..." ends in a quotation mark, not
        // in the ellipsis, so stripping in one fixed order leaves whichever came second behind.
        do
        {
            previous = trimmed;
            while (trimmed.Length > 0 && trimmed[0] is '"' or '\'')
            {
                trimmed = trimmed[1..].TrimStart();
            }

            while (trimmed.Length > 0 && trimmed[^1] is '"' or '\'')
            {
                trimmed = trimmed[..^1].TrimEnd();
            }

            if (trimmed.EndsWith("...", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3].TrimEnd();
            }
        }
        while (trimmed != previous && trimmed.Length > 0);

        return trimmed;
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<QuoteParagraph> Quotes(ContentDocument document)
    {
        static IEnumerable<QuoteParagraph> Of(Section section)
        {
            foreach (var quote in section.Paragraphs.OfType<QuoteParagraph>()) yield return quote;
            foreach (var child in section.Children)
            {
                foreach (var quote in Of(child)) yield return quote;
            }
        }

        foreach (var quote in Of(document.Lede)) yield return quote;
        foreach (var section in document.Sections)
        {
            foreach (var quote in Of(section)) yield return quote;
        }
    }
}
