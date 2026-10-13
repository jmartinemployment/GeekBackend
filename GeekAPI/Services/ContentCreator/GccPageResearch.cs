using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The partner research a page's requests carry: every page the brief's searches of the partners'
/// crawls found, each passage once.
/// </summary>
/// <remarks>
/// <para>
/// <b>One list for a page.</b> Its opening and every body request are built from this, the count that
/// decides whether a request fits is made of it (<see cref="GccResearchPerRequest"/>), and the page's
/// checks read it. A tool page's create holds its own partner's searches alone
/// (<see cref="GccPartnerToolSlice.Narrow"/>), so the same method gives a tool page its partner's and
/// a pillar or a blog every partner's.
/// </para>
/// <para>
/// <b>A partner's crawl is searched with the brief and with nothing else:</b> the problem, then each
/// row of failure and remedy. From 2026-10-10 to 2026-10-13 it was also searched with the operator's
/// FAQ questions, and from 2026-10-12 what those found was joined to this list. The operator supplies
/// each question's answer now (<see cref="GccFaqAnswers"/>), so no question is searched for and there
/// is nothing to join.
/// </para>
/// </remarks>
internal static class GccPageResearch
{
    /// <summary>The page's research, or none when the research holds no partner page.</summary>
    public static IReadOnlyList<GccQuoteablePage> PagesOf(GccResearchDocument? research) =>
        [.. research?.Quoteables ?? []];

    /// <summary>
    /// The searches that found the passages of <paramref name="pages"/>, each once, in the order they
    /// are first met: the list the research block numbers, so a search has the same number in every
    /// request of a page, whichever of its passages a request carries.
    /// </summary>
    public static IReadOnlyList<GccPassageSearch> SearchesOf(IReadOnlyList<GccQuoteablePage> pages)
    {
        var searches = new List<GccPassageSearch>();
        foreach (var page in pages)
        {
            if (page?.Paragraphs is null) continue;
            for (var position = 0; position < page.Paragraphs.Count; position++)
            {
                foreach (var search in page.SearchesThatFound(position))
                {
                    if (!searches.Contains(search)) searches.Add(search);
                }
            }
        }

        return searches;
    }
}
