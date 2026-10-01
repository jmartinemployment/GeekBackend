using System.Text.RegularExpressions;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The spans a block quotation may be chosen from, cut out of retrieved partner pages.
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
    /// Every quotable span in these pages, numbered from 1 in the order they are presented.
    /// </summary>
    public static IReadOnlyList<GccQuoteCandidate> From(IReadOnlyList<GccQuoteablePage> pages)
    {
        var candidates = new List<GccQuoteCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Url)) continue;

            // Boilerplate cannot be a quotation however well it reads. Licence text is the live
            // example: "means (a) that the initial Contributor has attached the notice described in
            // Exhibit B" is a complete, grammatical sentence on dext.com/licenses.
            if (IsBoilerplatePath(page.Url)) continue;

            var fromThisPage = 0;
            foreach (var paragraph in page.Paragraphs)
            {
                foreach (var line in Lines(paragraph))
                {
                    foreach (var sentence in Sentences(line))
                    {
                        if (!IsQuotable(sentence)) continue;
                        if (!seen.Add(sentence)) continue;

                        candidates.Add(new GccQuoteCandidate(
                            candidates.Count + 1, sentence, page.Url, page.Title));

                        if (++fromThisPage >= MaxPerPage) break;
                    }

                    if (fromThisPage >= MaxPerPage) break;
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
    /// The prose lines of a retrieved paragraph.
    /// </summary>
    /// <remarks>
    /// A retrieved "paragraph" is not prose: <c>HttpGeekCrawlerRagClient.RenderChunk</c> emits a
    /// labelled blob — <c>Section:</c>, <c>Target Entity Match:</c>, <c>Context:</c>, <c>Specific
    /// detail:</c>, <c>Linked from this section:</c> — so the labels and the anchor list have to
    /// come off before anything is cut out of it, or a candidate reads "Section: Pricing".
    /// </remarks>
    private static IEnumerable<string> Lines(string? paragraph)
    {
        if (string.IsNullOrWhiteSpace(paragraph)) yield break;

        foreach (var raw in paragraph.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var label = LabelPrefix().Match(line);
            if (label.Success)
            {
                // "Linked from this section: a, b, c" is an anchor list, not prose.
                if (label.Groups[1].Value.Equals("Linked from this section", StringComparison.OrdinalIgnoreCase))
                    continue;

                line = line[label.Length..].Trim();
                if (line.Length == 0) continue;
            }

            yield return line;
        }
    }

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

    [GeneratedRegex(@"^(Section|Target Entity Match|Context|Specific detail|Linked from this section):\s*",
        RegexOptions.IgnoreCase)]
    private static partial Regex LabelPrefix();

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
