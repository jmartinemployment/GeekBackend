using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The partner research a page's requests carry: every page the brief's searches found, with what the
/// searches for the operator's questions found on the same crawls added, each passage once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the two are joined here.</b> A partner's crawl is searched with the brief (the problem, then
/// each row of failure and remedy) and then with the operator's questions: that partner's tool page's
/// FAQ, the pillar's People Also Ask and the blog's FAQ. Until 2026-10-12 what a question's search
/// found reached the one request that answers the question and no other, so a page's body was written
/// without passages that had been searched for, returned and paid for on that page's own subject. The
/// only reason was room, in a model that accepted 128,000 tokens (Jeff, 2026-10-12: nothing a search
/// brings back is left out for a count).
/// </para>
/// <para>
/// <b>One list for a page.</b> Its opening and every body request are built from this, the count that
/// decides whether a request fits is made of it (<see cref="GccResearchPerRequest"/>), and the page's
/// checks read it. A tool page's create holds its own partner's searches alone
/// (<see cref="GccPartnerToolSlice.Narrow"/>), so the same method gives a tool page its partner's and
/// a pillar or a blog every partner's.
/// </para>
/// <para>
/// <b>What stays apart.</b> The request that answers a question is still given that question's own
/// passages and nothing else (<see cref="GccToolFaqEvidence"/>, <see cref="GccPageFaqEvidence"/>). The
/// partner data and the quotable sentences are still read from the pages the brief's searches found:
/// a page only a question found is not read for either.
/// </para>
/// <para>
/// <b>Each passage once, in a fixed order.</b> A page both kinds of search found is listed once, where
/// the brief's search put it, with the question's passages after its own; a passage both found is
/// printed once and says both found it. A page only a question found comes after every page the brief
/// found, in the order the questions were searched. The same research gives the same list, so every
/// request of a page carries the same text.
/// </para>
/// </remarks>
internal static class GccPageResearch
{
    /// <summary>The page's research, or none when the research holds no partner page.</summary>
    public static IReadOnlyList<GccQuoteablePage> PagesOf(GccResearchDocument? research)
    {
        var pages = new List<GccQuoteablePage>(research?.Quoteables ?? []);
        if (research?.FaqEvidence is not { Count: > 0 } searched) return pages;

        // By address, as the search results are kept (GccGroundingResolver's own comparison).
        var at = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < pages.Count; i++)
        {
            if (pages[i]?.Url is { Length: > 0 } url) at.TryAdd(url, i);
        }

        foreach (var entry in searched)
        {
            // Read back from stored JSON, an entry or its pages can be missing.
            if (entry?.Pages is null || string.IsNullOrWhiteSpace(entry.Question)) continue;
            var search = new GccPassageSearch(GccPassageSearch.KindQuestion, entry.Question);
            foreach (var found in entry.Pages)
            {
                if (found?.Paragraphs is null || string.IsNullOrWhiteSpace(found.Url)) continue;
                var page = GccGroundingResolver.FoundBy(found, search);
                if (at.TryGetValue(page.Url, out var held) && pages[held]?.Paragraphs is not null)
                {
                    pages[held] = GccGroundingResolver.WithLaterPassages(pages[held], page);
                    continue;
                }

                at[page.Url] = pages.Count;
                pages.Add(page);
            }
        }

        return pages;
    }

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
