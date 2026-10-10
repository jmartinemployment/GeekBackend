using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The evidence for the tool page's operator-supplied FAQ: for each question, what a search of the
/// partner's own crawl found for that question.
/// </summary>
/// <remarks>
/// <para>
/// The search is <c>GccGroundingResolver</c>'s, one per question, made before generation with the
/// partner's run id in hand, and filed by host and question in the research's
/// <see cref="GccResearchDocument.FaqEvidence"/>. This class finds a question's entry and renders a
/// call's worth of them. It searches nothing and selects nothing.
/// </para>
/// <para>
/// Until 2026-10-10 the questions were never searched for. This class picked paragraphs for them, by
/// shared words, out of the passages the page's other searches had brought back -- first from the
/// page-level block, and from 2026-10-09 from every passage retrieved for the product. Either way a
/// question none of those searches was about had nothing to be picked from, the writer left it out as
/// told, and the report said no page of the partner's answered it when nothing had looked. That
/// selector is gone rather than kept beside the search: two ways of finding one question's evidence is
/// how they come to disagree.
/// </para>
/// <para>
/// <b>Three states, never folded.</b> A question with no entry was not searched for (research that
/// did not come through the resolver, or a run that wrote no tool page). An entry with no passages is
/// a search that found nothing. An entry with passages is evidence. The first two are both "left
/// out", for different reasons, and the page says which.
/// </para>
/// </remarks>
internal static class GccToolFaqEvidence
{
    /// <summary>
    /// What the search of <paramref name="host"/>'s crawl found for <paramref name="question"/>, or null
    /// when no such search was made. Empty is a search that found nothing.
    /// </summary>
    /// <param name="searched">The research's FAQ searches; null when it carries none.</param>
    /// <param name="host">The partner's host key, the one the brief files the tool's questions under.</param>
    /// <param name="question">The question exactly as the operator wrote it, which is how its search is filed.</param>
    internal static IReadOnlyList<GccQuoteablePage>? FoundFor(
        IReadOnlyList<GccFaqEvidence>? searched, string host, string question)
    {
        if (searched is null || string.IsNullOrWhiteSpace(host)) return null;

        var entries = searched
            .Where(e => string.Equals(e.Host, host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Question, question, StringComparison.Ordinal))
            .ToList();
        if (entries.Count == 0) return null;

        // One entry per search. A host indexed under two runs is searched in each, so their pages add.
        return [.. entries.SelectMany(e => e.Pages)];
    }

    /// <summary>
    /// What the search scored each passage of <paramref name="pages"/>, with the page it is from: the
    /// library's score, and the reranking model's when the search was reranked. For the run's record.
    /// A page that carries no scores (one that did not come from a search) adds nothing.
    /// </summary>
    internal static IReadOnlyList<object> ScoresOf(IReadOnlyList<GccQuoteablePage> pages) =>
    [
        .. pages.SelectMany(page => (page.Scores ?? [])
            .Select(score => (object)new { url = page.Url, score = score.Score, reranked = score.Reranked })),
    ];

    /// <summary>How many passages <paramref name="pages"/> carry between them: what the writer is shown.</summary>
    internal static int PassageCount(IReadOnlyList<GccQuoteablePage> pages) =>
        pages.Sum(p => p.Paragraphs.Count(paragraph => !string.IsNullOrWhiteSpace(paragraph)));

    /// <summary>
    /// The evidence block for one call: each question's label, then the passages found for it, in the
    /// "[Title] (Url)\n  - paragraph" shape <see cref="ContentPromptBuilder"/>'s PARTNER EVIDENCE block
    /// uses. The labels are the ones the prompt lists the questions under (Q1, Q2, ... in the order
    /// given), so a passage is read beside the question it was found for.
    /// </summary>
    /// <param name="batch">The call's questions, each with its passages. A question with none is not sent.</param>
    internal static string Render(IReadOnlyList<(string Question, IReadOnlyList<GccQuoteablePage> Pages)> batch)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < batch.Count; i++)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"Found for Q{i + 1}:");
            foreach (var page in batch[i].Pages)
            {
                var paragraphs = page.Paragraphs.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                if (paragraphs.Count == 0) continue;
                sb.AppendLine($"[{page.Title}] ({page.Url})");
                foreach (var paragraph in paragraphs)
                {
                    sb.AppendLine($"  - {paragraph}");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }
}
