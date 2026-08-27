using System.Net;
using HtmlAgilityPack;
using Microsoft.Playwright;

namespace GeekAPI.Services.ContentCreatorV2.Hierarchy;

/// <summary>
/// Mobile Playwright fetch. Phase 1: caller requests homepage only (MaxPages=1 policy lives on the service).
/// Returns HTML + same-origin links for a later BFS unlock — no visibility annotate.
/// </summary>
public sealed class GccV2PageFetcher
{
    private readonly GccV2PlaywrightBrowserHolder _browserHolder;
    private readonly ILogger<GccV2PageFetcher> _logger;

    public GccV2PageFetcher(
        GccV2PlaywrightBrowserHolder browserHolder,
        ILogger<GccV2PageFetcher> logger)
    {
        _browserHolder = browserHolder;
        _logger = logger;
    }

    public async Task<GccV2FetchedPage?> FetchAsync(string url, CancellationToken ct)
    {
        var browser = await _browserHolder.EnsureBrowserAsync(ct);
        if (browser is null)
        {
            _logger.LogWarning("Hierarchy page fetch skipped — Playwright browser unavailable for {Url}", url);
            return null;
        }

        IBrowserContext? context = null;
        IPage? page = null;
        try
        {
            context = await browser.NewContextAsync(GccV2CrawlerIdentity.MobileContext());
            page = await context.NewPageAsync();
            var response = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.Load,
                Timeout = GccV2CrawlerIdentity.NavigationTimeoutMs,
            });

            await GccV2CrawlerIdentity.WaitForRenderedAsync(page);

            var finalUrl = response?.Url ?? page.Url ?? url;
            var status = response?.Status ?? 0;
            var html = await page.EvaluateAsync<string>("() => document.documentElement.outerHTML")
                       ?? string.Empty;

            var links = status is >= 200 and < 300
                ? ExtractSameOriginLinks(html, finalUrl)
                : [];

            return new GccV2FetchedPage(url, finalUrl, html, status, links);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hierarchy page fetch failed for {Url}", url);
            return null;
        }
        finally
        {
            if (page is not null)
                await page.CloseAsync();
            if (context is not null)
                await context.DisposeAsync();
        }
    }

    /// <summary>Same-origin absolute URLs for a future inventory/BFS queue.</summary>
    internal static IReadOnlyList<string> ExtractSameOriginLinks(string html, string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(html) || !Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri))
            return [];

        var origin = pageUri.GetLeftPart(UriPartial.Authority);
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return [];

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var nodes = doc.DocumentNode.SelectNodes(".//a[@href]");
        if (nodes is null)
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var links = new List<string>();
        foreach (var anchor in nodes)
        {
            var href = WebUtility.HtmlDecode(anchor.GetAttributeValue("href", "")).Trim();
            if (!TryResolveSameOrigin(href, pageUri, originUri, out var absolute))
                continue;
            if (seen.Add(absolute))
                links.Add(absolute);
        }

        return links;
    }

    private static bool TryResolveSameOrigin(
        string href,
        Uri pageUri,
        Uri originUri,
        out string absolute)
    {
        absolute = string.Empty;
        if (string.IsNullOrWhiteSpace(href))
            return false;
        if (href.StartsWith('#')
            || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            if (!Uri.TryCreate(pageUri, href, out var resolved))
                return false;
            if (resolved.Scheme is not ("http" or "https"))
                return false;
            if (!string.Equals(resolved.Host, originUri.Host, StringComparison.OrdinalIgnoreCase))
                return false;

            var builder = new UriBuilder(resolved) { Fragment = "" };
            absolute = builder.Uri.AbsoluteUri;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
