using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Per-question evidence for the tool page's operator-supplied FAQ, read from the partner pages
/// already retrieved for this create.
/// </summary>
/// <remarks>
/// <para>
/// Until 2026-10-09, <c>ToolFaqAsync</c> answered every batch of the operator's questions from
/// <c>toolOutlineCtx.EvidenceBlock</c> -- the same page-level block the lede and body sections
/// read, assembled once for the page's own keyword and angle. A question whose topic the
/// page-level retrieval never surfaced (a specific feature, an integration, a narrow "how does X
/// interact with Y" question the body never needed) was reported "left out" even when the
/// partner's crawled pages covered it elsewhere in <c>partnerPages</c> -- the evidence existed,
/// it was just never in the slice handed to this specific call.
/// </para>
/// <para>
/// This does not add a network round-trip. <c>GccGenerateService</c> has no
/// <c>IGeekCrawlerRagClient</c> of its own -- every retrieval already happened once, before
/// generation started, in <c>GccGroundingResolver</c>, and <c>partnerPages</c> is that result
/// read back out of <c>create.ResearchJson</c> (<c>GccResearchFetchService.Deserialize</c>).
/// Re-querying the index per question would need a <c>runId</c> scoped to this one product, and
/// there is no safe way to derive one here: <c>GccQuoteablePage.RunId</c> is never populated by
/// <c>HttpGeekCrawlerRagClient.MapChunksToQuoteable</c> (the mapper that produces every RAG-retrieved
/// page), and a create can declare several partners whose pages share one research blob -- the
/// same ambiguity <c>GenerateToolPageAsync</c> already documents at its single-origin check
/// (<c>partnerOrigins.Count == 1</c>), tracked as unresolved in
/// <c>plans/tool-page-per-partner.md</c>. Guessing a run id in the ambiguous case would silently
/// answer a question from the wrong partner's crawl, which is worse than today's gap. So instead:
/// search the full, untruncated pool this create already fetched for this product
/// (<c>partnerPages</c>, the same pool <c>GccV2PartnerExtractionService.ExtractFromPagesAsync</c>
/// reads for this product's capability/FAQ-bank extraction) rather than the page-level block's
/// truncated slice of it. "Unsupported" is still measured honestly -- a question with no matching
/// paragraph here gets no evidence block and is still left out and reported by name -- but it is
/// now measured against everything this create retrieved for this product, not against whatever
/// the page's lede/body query happened to need.
/// </para>
/// </remarks>
internal static class GccToolFaqEvidence
{
    /// <summary>Paragraphs rendered per question batch, across all matching pages combined.</summary>
    internal const int MaxParagraphsPerBatch = 24;

    /// <summary>
    /// A word short enough, or common enough, that its presence in both a question and a
    /// paragraph says nothing about whether the paragraph answers the question.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "but", "if", "then", "than", "that", "this", "these", "those",
        "is", "are", "was", "were", "be", "been", "being", "am",
        "do", "does", "did", "can", "could", "will", "would", "should", "shall", "may", "might", "must",
        "have", "has", "had", "having",
        "to", "of", "in", "on", "at", "by", "for", "with", "without", "from", "into", "onto", "over",
        "under", "about", "against", "between", "through", "during", "before", "after", "above", "below",
        "what", "which", "who", "whom", "whose", "when", "where", "why", "how",
        "it", "its", "it's", "they", "them", "their", "there", "here",
        "you", "your", "yours", "we", "our", "ours", "i", "my", "mine", "he", "she", "his", "her",
        "not", "no", "yes", "so", "as", "also", "just", "only", "even", "still", "also",
        "any", "all", "some", "each", "every", "other", "another", "same", "such",
        "up", "out", "off", "down", "again", "once",
    };

    /// <summary>
    /// The evidence block for one batch of operator FAQ questions: the partner's own paragraphs
    /// whose words overlap the batch's questions, grouped by page and rendered in the same
    /// "[Title] (Url)\n  - paragraph" shape <see cref="ContentPromptBuilder"/>'s PARTNER EVIDENCE
    /// block already uses. Empty when nothing in <paramref name="partnerPages"/> shares a
    /// significant word with any question in the batch -- the caller renders that the same way an
    /// empty page-level block already does ("nothing was retrieved -- answer no question"), so a
    /// batch with no real evidence still answers nothing rather than guessing from a near-miss.
    /// </summary>
    internal static string BuildFor(IReadOnlyList<GccQuoteablePage> partnerPages, IReadOnlyList<string> questionBatch)
    {
        if (partnerPages.Count == 0 || questionBatch.Count == 0)
            return string.Empty;

        var questionWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var question in questionBatch)
        {
            foreach (var word in SignificantWords(question))
            {
                questionWords.Add(word);
            }
        }

        if (questionWords.Count == 0)
            return string.Empty;

        var scored = new List<(GccQuoteablePage Page, string Paragraph, int ParagraphIndex, int Score)>();
        foreach (var page in partnerPages)
        {
            for (var i = 0; i < page.Paragraphs.Count; i++)
            {
                var paragraph = page.Paragraphs[i];
                if (string.IsNullOrWhiteSpace(paragraph))
                    continue;

                var score = Overlap(questionWords, SignificantWords(paragraph));
                if (score > 0)
                {
                    scored.Add((page, paragraph, i, score));
                }
            }
        }

        if (scored.Count == 0)
            return string.Empty;

        // Highest overlap first; a tie keeps the page's own reading order, so two equally-scored
        // paragraphs from one page still read top to bottom rather than by whichever page happened
        // to sort first.
        var kept = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.ParagraphIndex)
            .Take(MaxParagraphsPerBatch)
            .ToList();

        var byPage = kept
            .GroupBy(s => s.Page)
            .Select(g => (g.Key, g.OrderBy(s => s.ParagraphIndex).Select(s => s.Paragraph).ToList()))
            // Pages whose best-matching paragraph scores highest are read first.
            .OrderByDescending(pg => kept.Where(s => ReferenceEquals(s.Page, pg.Key)).Max(s => s.Score));

        var sb = new System.Text.StringBuilder();
        foreach (var (page, paragraphs) in byPage)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"[{page.Title}] ({page.Url})");
            foreach (var paragraph in paragraphs)
            {
                sb.AppendLine($"  - {paragraph}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Count of distinct question words this text also contains, as whole words, case-insensitive.</summary>
    private static int Overlap(IReadOnlySet<string> questionWords, IEnumerable<string> textWords)
    {
        var distinct = new HashSet<string>(textWords, StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var word in questionWords)
        {
            if (distinct.Contains(word)) count++;
        }

        return count;
    }

    /// <summary>
    /// Lowercase words of three letters or more, punctuation stripped, stop words dropped. Matching
    /// on whole words rather than substrings: "cash" must not match inside "cashier", and a question
    /// about "AI models" must not match every paragraph that happens to contain "a".
    /// </summary>
    private static IEnumerable<string> SignificantWords(string text) =>
        text
            .ToLowerInvariant()
            .Split(
                [' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}', '/', '\\', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !StopWords.Contains(w));
}
