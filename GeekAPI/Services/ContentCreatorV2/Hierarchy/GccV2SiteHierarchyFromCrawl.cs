using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Hierarchy;

/// <summary>
/// Build structured <see cref="GccV2SiteHierarchy"/> from owned project-site crawl pages.
/// Keeps per-page heading→link trees (never flattened). Includes homepage plus pages with
/// tool/use-case link groups for anchor discovery; drops 404s and article noise.
/// </summary>
public static class GccV2SiteHierarchyFromCrawl
{
    private const int MinLinksForSignal = 2;

    public static GccV2SiteHierarchy? Build(
        string siteUrl,
        IReadOnlyList<GccV2ProjectSiteCrawlPageDto> pages)
    {
        if (!GccV2HomepageUrl.TryNormalize(siteUrl, out var homepageUrl))
            return null;

        var hierarchyPages = pages
            .Where(p => !string.IsNullOrWhiteSpace(p.Html))
            .Where(p => p.StatusCode is >= 200 and < 300)
            .Where(p => !LooksLikeErrorPage(p))
            .Select(p => new GccV2PageHierarchy(
                p.FinalUrl ?? p.Url,
                GccV2HeadingTreeBuilder.Build(p.Html!)))
            .Where(p => p.Roots.Count > 0)
            .Where(p => !LooksLikeErrorHierarchy(p.Roots))
            .Where(p => IncludePage(p, homepageUrl))
            .OrderByDescending(p => IsHomepage(p.PageUrl, homepageUrl))
            .ThenBy(p => HierarchyPageRank(p.PageUrl), StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.PageUrl, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hierarchyPages.Count == 0)
            return null;

        return new GccV2SiteHierarchy(
            homepageUrl,
            GccV2CrawlerIdentity.ViewportLabel,
            DateTimeOffset.UtcNow,
            hierarchyPages);
    }

    internal static bool IncludePage(GccV2PageHierarchy page, string homepageUrl)
    {
        if (IsHomepage(page.PageUrl, homepageUrl))
            return true;

        if (LooksLikeUseCaseOrToolsHub(page.PageUrl))
            return true;

        return HasRichLinkGroups(page.Roots);
    }

    internal static bool HasRichLinkGroups(IReadOnlyList<GccV2HeadingNode> roots)
    {
        foreach (var node in WalkNodes(roots))
        {
            if (node.Links.Count >= MinLinksForSignal)
                return true;

            foreach (var child in node.Children)
            {
                if (child.Links.Count >= MinLinksForSignal)
                    return true;
            }
        }

        return false;
    }

    internal static bool IsHomepage(string? pageUrl, string homepageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page))
            return false;
        if (!Uri.TryCreate(homepageUrl, UriKind.Absolute, out var home))
            return false;

        return string.Equals(page.Host, home.Host, StringComparison.OrdinalIgnoreCase)
               && IsRootPath(page.AbsolutePath);
    }

    internal static bool LooksLikeErrorPage(GccV2ProjectSiteCrawlPageDto page)
    {
        if (page.StatusCode is 404 or 410 or >= 500)
            return true;

        var url = (page.FinalUrl ?? page.Url).ToLowerInvariant();
        if (url.Contains("/404", StringComparison.Ordinal) || url.EndsWith("/not-found", StringComparison.Ordinal))
            return true;

        return false;
    }

    internal static bool LooksLikeErrorHierarchy(IReadOnlyList<GccV2HeadingNode> roots)
    {
        foreach (var root in roots.Take(3))
        {
            var text = root.HeadingText.Trim();
            if (text.Length == 0) continue;

            if (text.Equals("404", StringComparison.OrdinalIgnoreCase)
                || text.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || text.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static bool LooksLikeUseCaseOrToolsHub(string pageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
            return false;

        var path = uri.AbsolutePath.ToLowerInvariant();
        if (IsRootPath(path)) return false;

        if (path.Contains("use-case", StringComparison.Ordinal)
            || path.Contains("usecase", StringComparison.Ordinal)
            || path.Contains("ai-use", StringComparison.Ordinal)
            || path.Contains("/methodolog", StringComparison.Ordinal))
            return true;

        // Tool category hubs (e.g. /tools/marketing) — not leaf tool pages (/tools/x/y).
        if (!path.Contains("/tools", StringComparison.Ordinal))
            return false;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length <= 2;
    }

    private static IEnumerable<GccV2HeadingNode> WalkNodes(IEnumerable<GccV2HeadingNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in WalkNodes(node.Children))
                yield return child;
        }
    }

    private static bool IsRootPath(string absolutePath) =>
        absolutePath == "/" || string.IsNullOrWhiteSpace(absolutePath.Trim('/'));

    private static string HierarchyPageRank(string pageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
            return "z";

        var path = uri.AbsolutePath.ToLowerInvariant();
        if (IsRootPath(path)) return "0";
        if (path.Contains("/tools", StringComparison.Ordinal)) return "1";
        if (path.Contains("use-case", StringComparison.Ordinal) || path.Contains("usecase", StringComparison.Ordinal))
            return "2";
        return "9" + path;
    }
}
