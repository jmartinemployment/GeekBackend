using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The research the search step leaves for a pillar's People Also Ask and a blog's FAQ: every
/// question the brief asks of those pages, searched for in a partner's crawl, one passage found for
/// each. For a test that writes one of those pages and needs its question section written, which
/// happens only for a question a search found passages for.
/// </summary>
internal static class PageFaqResearch
{
    internal const string Host = "partner.test";

    /// <summary>Null when the brief asks neither page a question, as research is when nothing was searched for.</summary>
    internal static string? For(string? briefJson)
    {
        var brief = GccGenerateService.ExtractBriefFields(briefJson);
        var questions = (brief.PaaQuestions ?? []).Concat(brief.BlogFaqQuestions ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (questions.Count == 0) return null;

        return GccResearchFetchService.Serialize(new GccResearchDocument(SerpIndex: null, Quoteables: [])
        {
            FaqEvidence =
            [
                .. questions.Select(question => new GccFaqEvidence(
                    Host,
                    question,
                    [new GccQuoteablePage($"https://{Host}/answers", "Answers", [], [$"What the partner's pages say about: {question}"])])),
            ],
        });
    }
}
