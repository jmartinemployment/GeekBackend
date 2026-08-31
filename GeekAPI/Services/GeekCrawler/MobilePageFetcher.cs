using System.Net;
using GeekAPI.Services.GeekCrawler.Polite;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Playwright;

namespace GeekAPI.Services.GeekCrawler;

public sealed class MobilePageFetcher
{
    private readonly GeekCrawlerPlaywrightHolder _browserHolder;
    private readonly GeekCrawlerPoliteGate _polite;
    private readonly ILogger<MobilePageFetcher> _logger;

    public MobilePageFetcher(
        GeekCrawlerPlaywrightHolder browserHolder,
        GeekCrawlerPoliteGate polite,
        ILogger<MobilePageFetcher> logger)
    {
        _browserHolder = browserHolder;
        _polite = polite;
        _logger = logger;
    }

    public sealed record FetchedPage(
        string Url,
        string FinalUrl,
        int StatusCode,
        bool RobotsAllowed,
        string? Html);

    public async Task<FetchedPage> FetchAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new FetchedPage(url, url, 0, true, null);

        var robots = await _polite.PrepareFetchAsync(uri, ct).ConfigureAwait(false);
        if (!robots.Allowed)
        {
            _polite.CompleteFetch(uri, 0);
            return new FetchedPage(url, url, 0, false, null);
        }

        var browser = await _browserHolder.EnsureBrowserAsync(ct).ConfigureAwait(false);
        if (browser is null)
        {
            _logger.LogWarning("Geek-Crawler fetch skipped — Playwright browser unavailable for {Url}", url);
            _polite.CompleteFetch(uri, 0);
            return new FetchedPage(url, url, 0, true, null);
        }

        IBrowserContext? context = null;
        IPage? page = null;
        try
        {
            context = await browser.NewContextAsync(GeekCrawlerMobileIdentity.MobileContext()).ConfigureAwait(false);
            page = await context.NewPageAsync().ConfigureAwait(false);
            var response = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.Load,
                Timeout = GeekCrawlerCaps.NavigationTimeoutMs,
            }).ConfigureAwait(false);

            await GeekCrawlerMobileIdentity.WaitForRenderedAsync(page).ConfigureAwait(false);

            var finalUrl = response?.Url ?? page.Url ?? url;
            var status = response?.Status ?? 0;

            if (status is (int)HttpStatusCode.TooManyRequests or (int)HttpStatusCode.ServiceUnavailable)
            {
                _polite.ApplyRateLimit(uri);
                return new FetchedPage(url, finalUrl, status, true, null);
            }

            string? html = null;
            if (status is >= 200 and < 300)
                html = await SnapshotMobileVisibleHtmlAsync(page).ConfigureAwait(false);

            _polite.CompleteFetch(uri, status);
            return new FetchedPage(url, finalUrl, status, true, html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Geek-Crawler mobile fetch failed for {Url}", url);
            _polite.CompleteFetch(uri, 0);
            return new FetchedPage(url, url, 0, true, null);
        }
        finally
        {
            if (page is not null)
                await page.CloseAsync().ConfigureAwait(false);
            if (context is not null)
                await context.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<string> SnapshotMobileVisibleHtmlAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
              for (const el of document.querySelectorAll('*')) {
                const s = getComputedStyle(el);
                if (s.display === 'none' || s.visibility === 'hidden')
                  el.setAttribute('data-geek-hidden', '1');
              }
            }
            """);

        return await page.EvaluateAsync<string>("() => document.documentElement.outerHTML")
               ?? string.Empty;
    }
}
