using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// Every tool page carries a block quotation, and it is the partner's own published words.
///
/// <para>
/// A tool page is an advertisement for that partner, which is what makes a quote box belong on it:
/// the partner saying a thing in their own wording, cited to the page they said it on. Jeff,
/// 2026-09-26: <i>"I want a blockquote in each tool"</i>.
/// </para>
///
/// <para>
/// Asking the prompt for one is not having one. The prompt asked, nothing checked, and the guard
/// that runs over a blog body deliberately leaves quotes alone -- cliche-cleaning a citation turns
/// it into a misquote -- so an invented quote with a real company's URL on its cite would have
/// parsed, rendered and shipped. AGENTS.md: <i>"a boundary is only fail-closed if code rejects the
/// bad input."</i> This is that code.
/// </para>
///
/// <para>
/// Verbatim means verbatim. The check is against the spans extraction already isolated for exactly
/// this purpose -- <see cref="GccPartnerTestimonialAsset.QuoteText"/>, and a citable's
/// <see cref="GccPartnerExtractionProvenance.Quote"/>, documented there as the "exact quote span
/// used for verify" -- and the cite must be the OriginProofUrl that span came from. A quote matched
/// to one source and cited to another is not a near miss; it attributes words to a page that does
/// not carry them.
/// </para>
/// </summary>
public static class GccToolQuoteGuard
{
    /// <summary>A quotable span and the single URL that span is provably on.</summary>
    private sealed record QuotableSpan(string Text, string OriginProofUrl);

    /// <summary>
    /// Violations, most specific first. Empty means the page carries at least one block quotation
    /// and every quotation on it is a verbatim span from <paramref name="extraction"/>, cited to
    /// its own source. Never repairs: a rewritten quote is still a quote nobody verified.
    /// </summary>
    public static IReadOnlyList<string> FindViolations(
        IReadOnlyList<Section> sections,
        IReadOnlyList<GccQuoteCandidate>? candidates)
    {
        var quotes = new List<QuoteParagraph>();
        foreach (var section in sections)
        {
            CollectQuotes(section, quotes);
        }

        var spans = QuotableSpans(candidates);

        if (quotes.Count == 0)
        {
            return spans.Count == 0
                ? ["The page carries no block quotation, and nothing retrieved from the partner's "
                    + "pages is shaped like one -- no complete sentence outside boilerplate."]
                : [$"The page carries no block quotation. {spans.Count} quotable partner span(s) were supplied and none was used."];
        }

        var violations = new List<string>();
        foreach (var quote in quotes)
        {
            // Chosen by number. The number is the whole answer -- the words and the cite are the
            // candidate's, read back by SnapQuotesToCandidates -- so a quotation that names a
            // listed span is verified by that alone, whatever the model typed beside it. A number
            // that names nothing is a refusal, not a fall-through to the text: the writer claimed a
            // span that was never offered.
            if (quote.Candidate is { } id)
            {
                if (id < 1 || id > spans.Count)
                {
                    violations.Add(
                        $"A block quotation names quotable span {id}, but {spans.Count} were listed. "
                        + "A quote box takes its words from a listed span, by its number.");
                }

                continue;
            }

            var text = Unquote(Normalize(string.Join(" ", quote.Runs.Select(r => r.Text))));
            if (text.Length == 0)
            {
                violations.Add("A block quotation is empty.");
                continue;
            }

            var matched = spans.FirstOrDefault(s => Normalize(s.Text).Contains(text, StringComparison.OrdinalIgnoreCase));
            if (matched is null)
            {
                violations.Add(
                    $"Block quotation \"{Excerpt(text)}\" is not a verbatim span of the partner evidence. "
                    + "A quote box asserts these are their exact published words.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(quote.Cite))
            {
                violations.Add($"Block quotation \"{Excerpt(text)}\" carries no cite, so it names no source to check it against.");
                continue;
            }

            if (!string.Equals(quote.Cite!.Trim(), matched.OriginProofUrl.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(
                    $"Block quotation \"{Excerpt(text)}\" cites {quote.Cite!.Trim()}, but that wording was extracted "
                    + $"from {matched.OriginProofUrl.Trim()}.");
            }
        }

        return violations;
    }

    private static void CollectQuotes(Section section, List<QuoteParagraph> into)
    {
        foreach (var paragraph in section.Paragraphs)
        {
            if (paragraph is QuoteParagraph quote)
            {
                into.Add(quote);
            }
        }

        foreach (var child in section.Children)
        {
            CollectQuotes(child, into);
        }
    }

    /// <summary>
    /// Testimonials and citables, the two asset kinds that carry wording someone published rather
    /// than a field extraction assembled. A citable contributes its verify span and its isolated
    /// claim, since the claim is the span for most of them.
    /// </summary>
    /// <summary>
    /// The spans a quotation may be matched against: the partner's own published sentences, as
    /// retrieved.
    /// </summary>
    /// <remarks>
    /// One source, deliberately. This read two of GccPartnerExtractionDocument's twenty-two
    /// categories -- Testimonials and Citables -- and refused a page when the extraction model had
    /// filed its findings under features and pricing instead. Adding the retrieved pages beside
    /// those buckets fixed the refusal and left two derivations of "a quotable span" with different
    /// rules, which is the drift this pipeline keeps paying for.
    ///
    /// The buckets were also the weaker of the two: a citable's span is
    /// <c>Provenance.Quote ?? IsolatedClaim</c>, so its fallback is a field the extractor assembled
    /// rather than wording the partner published -- the thing a quote box must never contain.
    /// </remarks>
    private static List<QuotableSpan> QuotableSpans(IReadOnlyList<GccQuoteCandidate>? candidates) =>
        [.. (candidates ?? []).Select(c => new QuotableSpan(c.Text, c.PageUrl))];

    /// <summary>
    /// Replaces each block quotation's text with the exact candidate span it came from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The text was never supposed to be the model's to type.</b> Candidates are cut by
    /// <c>GccQuoteCandidates</c> and listed for the writer to choose from; the writer copies one, and
    /// copying is where drift enters. A live run refused AvidXchange's page on
    /// <c>""We offer an end-to-end accounts payable automation solution…"</c> — the model had shortened a
    /// real span and added an ellipsis, so it was no longer a verbatim substring of anything.
    /// </para>
    /// <para>
    /// Snapping it back to the candidate is the design being enforced, not the guard being loosened. The
    /// published words become the system's own string again — the one cut from the partner's typed blocks
    /// — and <see cref="FindViolations"/> still refuses anything that matches no candidate at all. What
    /// stops failing is a page losing to punctuation.
    /// </para>
    /// <para>
    /// A draft quote matches when it is a span of a candidate once wrapping quote marks and a trailing
    /// ellipsis are set aside. The cite is taken from the candidate too, so a correct quotation can no
    /// longer carry the wrong source URL.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Section> SnapQuotesToCandidates(
        IReadOnlyList<Section> sections,
        IReadOnlyList<GccQuoteCandidate>? candidates)
    {
        var spans = QuotableSpans(candidates);
        if (spans.Count == 0) return sections;

        return [.. sections.Select(section => SnapSection(section, spans))];
    }

    private static Section SnapSection(Section section, IReadOnlyList<QuotableSpan> spans) =>
        section with
        {
            Paragraphs = [.. section.Paragraphs.Select(p => SnapParagraph(p, spans))],
            Children = [.. section.Children.Select(child => SnapSection(child, spans))],
        };

    private static Paragraph SnapParagraph(Paragraph paragraph, IReadOnlyList<QuotableSpan> spans)
    {
        if (paragraph is not QuoteParagraph quote) return paragraph;

        // By number, which is the design: GccQuoteCandidates numbers the spans and says "the model
        // selects; it never transcribes", and the brief-time probe has always read its answer back
        // this way. The writer did not -- it was shown the same list unnumbered and asked to copy
        // one "character for character", and copying is where Stampli's page was lost on
        // 2026-10-03: a quotation that matched no candidate, refused as it should be, on a page
        // whose evidence held the sentence it was reaching for. A number cannot be paraphrased.
        if (quote.Candidate is { } id && id >= 1 && id <= spans.Count)
        {
            var chosen = spans[id - 1];
            return new QuoteParagraph([new Run(chosen.Text)], chosen.OriginProofUrl);
        }

        var drafted = Unquote(Normalize(string.Join(" ", quote.Runs.Select(r => r.Text))));
        if (drafted.Length == 0) return paragraph;

        var matched = spans.FirstOrDefault(s =>
            Normalize(s.Text).Contains(drafted, StringComparison.OrdinalIgnoreCase));
        if (matched is null) return paragraph;

        // One run: the span is one string and splitting it across runs would invent emphasis the
        // partner's page never had.
        return new QuoteParagraph([new Run(matched.Text)], matched.OriginProofUrl);
    }

    /// <summary>
    /// A drafted quote with its wrapping quote marks and any trailing ellipsis removed.
    /// </summary>
    /// <remarks>
    /// Only the edges. Nothing inside is touched, so a quote that differs in the middle still fails to
    /// match and is still refused — this strips the two things a model adds while copying, not the words.
    /// </remarks>
    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        string previous;

        // Until stable, because the two wrappers nest: "...nine days\u2026" ends in a quote mark, not in
        // the ellipsis, so stripping in one fixed order leaves whichever came second behind.
        do
        {
            previous = trimmed;

            while (trimmed.Length > 0 && (trimmed[0] is '"' or '\u201C' or '\u2018' or '\''))
            {
                trimmed = trimmed[1..].TrimStart();
            }

            while (trimmed.Length > 0 && (trimmed[^1] is '"' or '\u201D' or '\u2019' or '\''))
            {
                trimmed = trimmed[..^1].TrimEnd();
            }

            if (trimmed.EndsWith('\u2026'))
            {
                trimmed = trimmed[..^1].TrimEnd();
            }
            else if (trimmed.EndsWith("...", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3].TrimEnd();
            }
        }
        while (trimmed != previous && trimmed.Length > 0);

        return trimmed;
    }

    /// <summary>Whitespace only. Wording, punctuation and case-sensitivity of the match are the point.</summary>
    private static string Normalize(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string Excerpt(string value) =>
        value.Length <= 60 ? value : value[..60].TrimEnd() + "…";
}
