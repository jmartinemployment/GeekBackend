using System.Net;
using GeekAPI.Services.GeekCrawler.Polite;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Playwright;

namespace GeekAPI.Services.GeekCrawler;

public sealed class MobilePageFetcher
{
    private readonly GeekCrawlerPlaywrightHolder _browserHolder;
    private readonly GeekCrawlerPoliteGate _polite;
    private readonly GeekCrawlerHostRegistry _registry;
    private readonly ILogger<MobilePageFetcher> _logger;

    public MobilePageFetcher(
        GeekCrawlerPlaywrightHolder browserHolder,
        GeekCrawlerPoliteGate polite,
        GeekCrawlerHostRegistry registry,
        ILogger<MobilePageFetcher> logger)
    {
        _browserHolder = browserHolder;
        _polite = polite;
        _registry = registry;
        _logger = logger;
    }

    public sealed record FetchedPage(
        string Url,
        string FinalUrl,
        int StatusCode,
        bool RobotsAllowed,
        string? Html,
        string? FailureReason = null);

    public Task<FetchedPage> FetchAsync(string url, CancellationToken ct) =>
        !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? Task.FromResult(new FetchedPage(url, url, 0, true, null))
            : FetchCoreAsync(uri, url, ct);

    private async Task<FetchedPage> FetchCoreAsync(Uri uri, string url, CancellationToken ct)
    {
        var origin = uri.GetLeftPart(UriPartial.Authority);
        var controller = _registry.GetController(origin);

        return await controller.ExecutePolitelyAsync(
            () => FetchWithinSlotAsync(uri, url, ct),
            ct).ConfigureAwait(false);
    }

    private async Task<FetchedPage> FetchWithinSlotAsync(Uri uri, string url, CancellationToken ct)
    {
        var robots = await _polite.PrepareFetchAsync(uri, ct).ConfigureAwait(false);
        if (!robots.Allowed)
        {
            _polite.CompleteFetch(uri, 0);
            return new FetchedPage(url, url, 0, false, null);
        }

        var browser = await _browserHolder.EnsureBrowserAsync(ct).ConfigureAwait(false);
        if (browser is null)
            throw new GeekCrawlerPlaywrightUnavailableException(
                "Playwright browser unavailable — cannot fetch pages.");

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

            try
            {
                await GeekCrawlerMobileIdentity.WaitForRenderedAsync(page).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Geek-Crawler render wait failed for {Url} ({ExceptionType}) — snapshotting anyway.",
                    url,
                    ex.GetType().Name);
            }

            var finalUrl = response?.Url ?? page.Url ?? url;
            var status = response?.Status ?? 0;

            if (status is (int)HttpStatusCode.TooManyRequests or (int)HttpStatusCode.ServiceUnavailable)
            {
                _polite.ApplyRateLimit(uri);
                _polite.CompleteFetch(uri, status);
                var rateHtml = await TrySnapshotHtmlAsync(page).ConfigureAwait(false);
                return new FetchedPage(url, finalUrl, status, true, rateHtml);
            }

            string? html = await TrySnapshotHtmlAsync(page).ConfigureAwait(false);

            var headers = response?.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value))
                           ?? new Dictionary<string, string>();
            string? failureReason = null;
            if (GeekCrawlerChallengeDetector.IsCloudflareChallenge(status, html, headers))
            {
                failureReason = GeekCrawlerChallengeDetector.CloudflareChallengeReason;
                _logger.LogWarning(
                    "Geek-Crawler fetch hit Cloudflare challenge for {Url} (HTTP {Status}).",
                    url,
                    status);
            }

            _polite.CompleteFetch(uri, status);
            return new FetchedPage(url, finalUrl, status, true, html, failureReason);
        }
        catch (Exception ex) when (ex is not GeekCrawlerPlaywrightUnavailableException
                                   and not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Geek-Crawler mobile fetch failed for {Url} (status 0, {ExceptionType}).",
                url,
                ex.GetType().Name);
            _polite.CompleteFetch(uri, 0);
            return new FetchedPage(
                url,
                url,
                0,
                true,
                null,
                $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (page is not null)
                await page.CloseAsync().ConfigureAwait(false);
            if (context is not null)
                await context.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<string?> TrySnapshotHtmlAsync(IPage page)
    {
        try
        {
            return await SnapshotMobileVisibleHtmlAsync(page).ConfigureAwait(false);
        }
        catch
        {
            return null;
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
