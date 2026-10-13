using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Which competitor pages a request shows, and the heading outline of each.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two readings of a competitor, joined by address.</b> A competitor's site is searched once for
/// the topic and returns passages on some of its pages (<c>GccResearchDocument.CompetitorQuoteables</c>).
/// Its heading outlines are not from a search: <see cref="GccCompetitorAnalysisResolver"/> reads one
/// for every page of every competitor's crawl. Until 2026-10-12 the writer was shown the first five of
/// each list, cut by position: five of the forty pages the searches returned, and the outlines of the
/// first five pages of the first competitor's crawl, whatever they were. The two fives were not the
/// same pages.
/// </para>
/// <para>
/// Every page the searches returned is shown now, and the outlines shown are those pages' own, found
/// here by address. Not every crawled page's outline: that is the competitors' whole sites, most of it
/// not on the topic, and on one project it was 1,777 pages. The check of a heading's
/// <c>competitor:</c> tag still reads every outline.
/// </para>
/// <para>
/// <b>One comparison of two addresses,</b> written once: the scheme, a leading <c>www.</c>, a trailing
/// slash, a fragment and case are ignored. A search result and an outline both carry the page's
/// address after a redirect when it has one (<c>HttpGeekCrawlerRagClient.MapChunksToQuoteable</c>,
/// <c>GeekCrawlerSiteStructure.Build</c>), so a miss is a page whose crawl row holds no headings, and
/// it is put on the run's record by its address, never passed over in silence.
/// </para>
/// <para>
/// <b>Competitor pages give way first.</b> When a request is more than its model accepts, the
/// competitor pages beyond <see cref="KeptWhenOver"/> are left out of it, each with its outline, before
/// any partner research is (<c>GccGenerateService.RequestThatFitsAsync</c>). The ones kept are the
/// search's first page for each competitor in turn, so the five are not one competitor's.
/// </para>
/// </remarks>
internal static class GccCompetitorOutlines
{
    /// <summary>How many competitor pages a request keeps when it is more than its model accepts.</summary>
    internal const int KeptWhenOver = 5;

    /// <summary>
    /// An address as two readings of one page are compared by: no scheme, no leading <c>www.</c>, no
    /// fragment, no trailing slash, lower case. Empty for an address that is not one.
    /// </summary>
    public static string AddressKey(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)) return string.Empty;
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        if (host.Length == 0) return string.Empty;

        var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        return $"{host}{path}{uri.Query.ToLowerInvariant()}";
    }

    /// <summary>
    /// The outline of each of <paramref name="returned"/>, in the same order: the outline whose address
    /// is that page's, or null when the crawl holds none for it.
    /// </summary>
    public static IReadOnlyList<GccCompetitorPageAnalysis?> For(
        IReadOnlyList<GccQuoteablePage>? returned, IReadOnlyList<GccCompetitorPageAnalysis> analyses)
    {
        var byAddress = new Dictionary<string, GccCompetitorPageAnalysis>(StringComparer.Ordinal);
        foreach (var analysis in analyses)
        {
            var key = AddressKey(analysis.Url);
            if (key.Length > 0) byAddress.TryAdd(key, analysis);
        }

        var outlines = new List<GccCompetitorPageAnalysis?>();
        foreach (var page in returned ?? [])
        {
            var key = AddressKey(page?.Url);
            outlines.Add(key.Length > 0 && byAddress.TryGetValue(key, out var outline) ? outline : null);
        }

        return outlines;
    }

    /// <summary>The addresses of the returned pages the crawl holds no outline for, in the order returned.</summary>
    public static IReadOnlyList<string> WithoutOutline(
        IReadOnlyList<GccQuoteablePage>? returned, IReadOnlyList<GccCompetitorPageAnalysis> analyses)
    {
        var outlines = For(returned, analyses);
        var missing = new List<string>();
        for (var i = 0; i < outlines.Count; i++)
        {
            if (outlines[i] is null && returned![i]?.Url is { Length: > 0 } url) missing.Add(url);
        }

        return missing;
    }

    /// <summary>
    /// Which of <paramref name="returned"/> a request shows, by place in the list: every one when
    /// <paramref name="all"/>, and otherwise <see cref="KeptWhenOver"/> of them, taken a page at a time
    /// from each competitor in turn, in the order the searches returned them.
    /// </summary>
    public static IReadOnlySet<int> Shown(IReadOnlyList<GccQuoteablePage>? returned, bool all)
    {
        var pages = returned ?? [];
        var shown = new HashSet<int>();
        if (all || pages.Count <= KeptWhenOver)
        {
            for (var i = 0; i < pages.Count; i++) shown.Add(i);
            return shown;
        }

        // Each competitor's pages, in the order returned, the competitors in the order first met.
        var hosts = new List<string>();
        var byHost = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        for (var i = 0; i < pages.Count; i++)
        {
            var host = GccRequiredToolMentions.HostKeyOf(pages[i]?.Url ?? string.Empty);
            if (!byHost.TryGetValue(host, out var queue))
            {
                queue = new Queue<int>();
                byHost[host] = queue;
                hosts.Add(host);
            }

            queue.Enqueue(i);
        }

        while (shown.Count < KeptWhenOver)
        {
            var took = false;
            foreach (var host in hosts)
            {
                if (shown.Count >= KeptWhenOver) break;
                if (!byHost[host].TryDequeue(out var index)) continue;
                shown.Add(index);
                took = true;
            }

            if (!took) break;
        }

        return shown;
    }

    /// <summary>The addresses of the returned pages a request leaves out when it does not show them all.</summary>
    public static IReadOnlyList<string> GivenWay(IReadOnlyList<GccQuoteablePage>? returned)
    {
        var pages = returned ?? [];
        var kept = Shown(pages, all: false);
        var left = new List<string>();
        for (var i = 0; i < pages.Count; i++)
        {
            if (!kept.Contains(i)) left.Add(pages[i]?.Url ?? string.Empty);
        }

        return left;
    }
}
