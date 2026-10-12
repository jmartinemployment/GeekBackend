using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The evidence for the pillar's People Also Ask and the blog's FAQ: for each question, what a
/// search of every partner's crawl found for that question.
/// </summary>
/// <remarks>
/// <para>
/// A tool page's question is about one partner, so one crawl is searched for it
/// (<see cref="GccToolFaqEvidence"/>). A pillar's or a blog's question is about the subject, and the
/// page is about every partner, so each partner's crawl is searched for it and what they found is
/// read together. The searches are <c>GccGroundingResolver</c>'s, filed in the same
/// <see cref="GccResearchDocument.FaqEvidence"/> under the partner searched; this class finds a
/// question's entries across partners and divides the questions into calls. It searches nothing and
/// selects nothing.
/// </para>
/// <para>
/// Until 2026-10-11 these questions were searched for nowhere. The call was given the questions, the
/// title and the keyword, and the writer answered from its own general knowledge
/// (<c>plans/real-run-findings-2026-10-07.md</c>, finding 5).
/// </para>
/// <para>
/// <b>The publisher's own site is not searched.</b> Its pages are what the body is told is already
/// published and must not be repeated, and they name tools that are other projects' partners.
/// </para>
/// <para>
/// <b>Three states, never folded,</b> as on the tool page: a question with no entry was not searched
/// for, entries with no passages are searches that found nothing, entries with passages are evidence.
/// </para>
/// </remarks>
internal static class GccPageFaqEvidence
{
    /// <summary>
    /// The most characters of evidence one call carries, so a call's size is bounded by this and not by
    /// how many partners the project has or how long their passages run.
    /// </summary>
    /// <remarks>
    /// A question brings three passages from each partner, and a passage is up to 2,000 characters
    /// with up to 2,000 more of the section around it. Eight questions of a five-partner project is
    /// 120 passages, about 180,000 characters as passages ran on 2026-10-10 and 510,000 at their
    /// longest, which was all of the 128,000 tokens the model of that day accepted in one request
    /// (what a model accepts is in <c>OpenAiModelLimits</c>). 200,000 characters is about 50,000
    /// tokens at the four characters a token these prompts never fall below.
    /// <para>
    /// This leaves nothing out: it decides how many requests a page's questions are spread over, and
    /// every question's passages are sent in one of them.
    /// </para>
    /// </remarks>
    internal const int MaxEvidenceCharsPerCall = 200_000;

    /// <summary>
    /// What the searches of the partners' crawls found for <paramref name="question"/>, or null when
    /// no partner's crawl was searched for it. Empty is searches that found nothing.
    /// </summary>
    /// <param name="searched">The research's FAQ searches; null when it carries none.</param>
    /// <param name="question">The question exactly as the operator wrote it, which is how its searches are filed.</param>
    internal static IReadOnlyList<GccQuoteablePage>? FoundFor(
        IReadOnlyList<GccFaqEvidence>? searched, string question)
    {
        if (searched is null) return null;

        var entries = searched
            .Where(e => string.Equals(e.Question, question, StringComparison.Ordinal))
            .ToList();
        if (entries.Count == 0) return null;

        // One entry per partner searched, so their pages add.
        return [.. entries.SelectMany(e => e.Pages)];
    }

    /// <summary>
    /// The questions divided into calls, in the order given: a call takes questions until it holds
    /// <paramref name="maxQuestions"/> or the next one's passages would take its evidence past
    /// <see cref="MaxEvidenceCharsPerCall"/>. A question is never divided, so one whose own passages
    /// exceed the limit is a call by itself.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<(string Question, IReadOnlyList<GccQuoteablePage> Pages)>> Calls(
        IReadOnlyList<(string Question, IReadOnlyList<GccQuoteablePage> Pages)> toAsk, int maxQuestions)
    {
        var calls = new List<IReadOnlyList<(string Question, IReadOnlyList<GccQuoteablePage> Pages)>>();
        var call = new List<(string Question, IReadOnlyList<GccQuoteablePage> Pages)>();
        var chars = 0;
        foreach (var item in toAsk)
        {
            var size = CharsOf(item.Pages);
            if (call.Count > 0 && (call.Count >= maxQuestions || chars + size > MaxEvidenceCharsPerCall))
            {
                calls.Add(call);
                call = [];
                chars = 0;
            }

            call.Add(item);
            chars += size;
        }

        if (call.Count > 0) calls.Add(call);
        return calls;
    }

    /// <summary>What <paramref name="pages"/> cost a prompt: each page's title and address, and its passages.</summary>
    private static int CharsOf(IReadOnlyList<GccQuoteablePage> pages) =>
        pages.Sum(page => page.Title.Length + page.Url.Length + page.Paragraphs.Sum(paragraph => paragraph.Length));
}
