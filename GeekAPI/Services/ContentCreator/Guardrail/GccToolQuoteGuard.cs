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
            var text = Normalize(string.Join(" ", quote.Runs.Select(r => r.Text)));
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

    /// <summary>Whitespace only. Wording, punctuation and case-sensitivity of the match are the point.</summary>
    private static string Normalize(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string Excerpt(string value) =>
        value.Length <= 60 ? value : value[..60].TrimEnd() + "…";
}
