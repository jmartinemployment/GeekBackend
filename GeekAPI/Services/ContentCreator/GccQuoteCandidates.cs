using System.Text.RegularExpressions;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The spans a block quotation may be chosen from, cut out of a partner page's typed blocks.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model selects; it never transcribes.</b> Candidates are built here, numbered here, and
/// the chosen one is read back from this list by its number — so the quotation that reaches the
/// page is the system's own string, taken from the retrieved page.
/// </para>
/// <para>
/// That is the whole point of the shape. Asking a model to return the quotation <i>text</i> means
/// asking it to retype a sentence, and a retyped sentence has to be found again: fuzzy-matched back
/// to its source, with an index map to recover the source's own characters, because a model echoing
/// <c>We’ve</c> emits <c>We've</c> and an exact comparison then rejects a correct quote. All of
/// that machinery exists only to undo a transcription nobody needed. A number cannot be mistyped
/// into a paraphrase.
/// </para>
/// <para>
/// So there is no verbatim check downstream, and that is not a weakening: the text was never out of
/// the system's hands to be altered. Each candidate also carries the page it was cut from, which is
/// the cite — so the model never supplies a URL either, and cannot attribute real words to a page
/// that does not carry them.
/// </para>
/// </remarks>
public static partial class GccQuoteCandidates
{
    /// <summary>Shorter than this carries no point; longer reads as a passage, not a quotation.</summary>
    private const int MinChars = 50;
    private const int MaxChars = 300;

    /// <summary>Per page, so one partner's long page cannot crowd out the other seven.</summary>
    private const int MaxPerPage = 12;

    /// <summary>Across all pages, which is what lands in the prompt.</summary>
    public const int MaxCandidates = 40;

    /// <summary>
    /// Every quotable span in these typed passages, numbered from 1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The only source.</b> A <see cref="GccGroundedPassage"/> carries the crawl page's blocks
    /// mapped kind for kind, so a candidate is the page's own prose. There used to be a second
    /// overload taking retrieved pages, whose text is <c>RenderChunk</c> output — a labelled blob
    /// with <c>Section:</c> / <c>Context:</c> / <c>Specific detail:</c> interleaved, which had to be
    /// stripped back off by pattern before anything could be cut out of it. It is gone, and so is the
    /// stripper: two ways to find a quote is two answers to one question, and the brief-time probe
    /// and the writer each read one of them.
    /// </para>
    /// <para>
    /// A <see cref="QuoteParagraph"/> is taken whole rather than sentence-split: the crawler typed
    /// it as a quotation because the page marked it as one, which is a stronger signal than any
    /// shape test here, and splitting it would cut a quotation in half.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<GccQuoteCandidate> From(IReadOnlyList<GccGroundedPassage> passages)
    {
        var candidates = new List<GccQuoteCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var passage in passages)
        {
            if (string.IsNullOrWhiteSpace(passage.Url)) continue;
            if (IsBoilerplatePath(passage.Url)) continue;

            var fromThisPage = 0;
            foreach (var paragraph in passage.Content)
            {
                // A typed quotation is held to length only. The page marked it as a quotation, which
                // outranks any shape test here -- rejecting it for not ending in a full stop is the
                // over-filtering that hides the span that fits.
                var declared = paragraph is QuoteParagraph;

                foreach (var span in SpansOf(paragraph))
                {
                    if (declared ? !IsQuotableLength(span) : !IsQuotable(span)) continue;
                    if (!seen.Add(span)) continue;

                    candidates.Add(new GccQuoteCandidate(
                        candidates.Count + 1, span, passage.Url, passage.Title));

                    if (++fromThisPage >= MaxPerPage) break;
                }

                if (fromThisPage >= MaxPerPage) break;
            }

            if (candidates.Count >= MaxCandidates) break;
        }

        return candidates.Count <= MaxCandidates
            ? candidates
            : candidates.Take(MaxCandidates).ToList();
    }

    /// <summary>
    /// What may be quoted out of one typed paragraph.
    /// </summary>
    /// <remarks>
    /// Only prose and quotations. A list item is a fragment, a table row is cells joined for
    /// reading, a code sample is not speech, and a definition is the page's own glossary — none of
    /// them is something a partner said.
    /// </remarks>
    private static IEnumerable<string> SpansOf(Paragraph paragraph)
    {
        switch (paragraph)
        {
            case QuoteParagraph quote:
                // Whole, not split: the page marked this as a quotation.
                var quoted = Flatten(quote.Runs);
                if (quoted.Length > 0) yield return quoted;
                break;

            case TextParagraph text:
                foreach (var sentence in Sentences(Flatten(text.Runs)))
                {
                    yield return sentence;
                }

                break;
        }
    }

    private static string Flatten(IReadOnlyList<Run> runs) =>
        string.Join(" ", runs.Select(r => r.Text).Where(t => !string.IsNullOrWhiteSpace(t)))
            .Replace("  ", " ")
            .Trim();

    /// <summary>Sentence-ish spans: terminal punctuation followed by space and a capital.</summary>
    private static IEnumerable<string> Sentences(string line)
    {
        var start = 0;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] is not ('.' or '!' or '?')) continue;

            // Not a boundary when the next thing is lowercase — "e.g." and "Inc. v" stay whole.
            var next = i + 1;
            while (next < line.Length && char.IsWhiteSpace(line[next])) next++;
            if (next < line.Length && !char.IsUpper(line[next]) && line[next] != '"' && line[next] != '“')
                continue;

            yield return line[start..(i + 1)].Trim();
            start = next;
            i = next - 1;
        }

        if (start < line.Length) yield return line[start..].Trim();
    }

    /// <summary>
    /// Shape only. Whether a span <i>answers the angle</i> is the selector's judgement, not this
    /// one — over-filtering here would hide the span that fits.
    /// </summary>
    private static bool IsQuotableLength(string span) =>
        span.Length is >= MinChars and <= MaxChars
        && !span.Contains('|')
        && !span.Contains('\u2022');

    private static bool IsQuotable(string sentence)
    {
        if (sentence.Length is < MinChars or > MaxChars) return false;

        // A quotation is a statement someone made. A heading, a nav label or a form field is not,
        // and they are what a sentence-shaped filter alone lets through.
        if (!char.IsUpper(Unwrap(sentence)[0])) return false;
        if (sentence[^1] is not ('.' or '!' or '?')) return false;

        // Table rows and bullet runs arrive as one line and read as prose until you quote one.
        if (sentence.Contains('|') || sentence.Contains('•')) return false;

        return true;
    }

    private static string Unwrap(string s)
    {
        var t = s.TrimStart('"', '“', '‘', '\'', ' ');
        return t.Length == 0 ? s : t;
    }

    private static bool IsBoilerplatePath(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && BoilerplatePath().IsMatch(parsed.AbsolutePath);

    /// <remarks>
    /// Two shapes, and the split is deliberate. Bare words are anchored to the start of a path
    /// segment, because "/legal-automation-software/" is a real product page for a legal-tech
    /// partner and swallowing it would discard that partner's best evidence. Compound tokens are
    /// specific enough to match anywhere in a segment, which is what "/california-notice-at-collection/"
    /// needs — it was missed entirely while every token was anchored.
    /// </remarks>
    [GeneratedRegex(@"(/(privacy|legal|terms|cookies?|gdpr|security|compliance|dpa|licen[cs]es?|"
        + @"careers|msa|sla|accessibility)(/|$|[-.])"
        + @"|notice-at-collection|data-process(or|ing)|sub-?processor)",
        RegexOptions.IgnoreCase)]
    private static partial Regex BoilerplatePath();
}

/// <summary>One span a quotation may be chosen from, and the page it was cut out of.</summary>
/// <param name="Id">1-based, as presented to the selector. The selector returns this and nothing else.</param>
/// <param name="Text">The page's own characters. Never round-tripped through a model.</param>
/// <param name="PageUrl">The cite. Resolved here, so the selector cannot supply a URL.</param>
public sealed record GccQuoteCandidate(int Id, string Text, string PageUrl, string? PageTitle);
