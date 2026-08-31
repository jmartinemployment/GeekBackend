using GeekApplication.Models.GeekCrawler;
using Microsoft.Playwright;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Mobile Pixel 7 bot identity for Geek-Crawler — never desktop.</summary>
internal static class GeekCrawlerMobileIdentity
{
    public const string PlaywrightDeviceName = "Pixel 7";

    private static BrowserNewContextOptions? _playwrightPixel7;

    public static void UsePlaywrightDevices(IPlaywright playwright)
    {
        if (!playwright.Devices.TryGetValue(PlaywrightDeviceName, out var device))
        {
            throw new InvalidOperationException(
                $"Playwright.Devices has no '{PlaywrightDeviceName}'");
        }

        _playwrightPixel7 = ClonePixel7(device);
    }

    public static BrowserNewContextOptions MobileContext()
    {
        var ctx = ClonePixel7(_playwrightPixel7) ?? FallbackPixel7();
        ctx.UserAgent = GeekCrawlerCaps.UserAgent;
        return ctx;
    }

    public static async Task WaitForRenderedAsync(IPage page)
    {
        try
        {
            await page.WaitForLoadStateAsync(
                LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = GeekCrawlerCaps.RenderQuiescenceCapMs })
                .ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            // Cap reached — snapshot anyway.
        }
    }

    private static BrowserNewContextOptions FallbackPixel7() => new()
    {
        UserAgent = GeekCrawlerCaps.UserAgent,
        ViewportSize = new ViewportSize { Width = 412, Height = 839 },
        ScreenSize = new ScreenSize { Width = 412, Height = 915 },
        IsMobile = true,
        HasTouch = true,
        DeviceScaleFactor = 2.625f,
        Locale = "en-US",
        ExtraHTTPHeaders = MobileHeaders(),
    };

    private static BrowserNewContextOptions ClonePixel7(BrowserNewContextOptions? source)
    {
        if (source is null)
            return FallbackPixel7();

        return new BrowserNewContextOptions
        {
            UserAgent = source.UserAgent,
            ViewportSize = source.ViewportSize is { } vp
                ? new ViewportSize { Width = vp.Width, Height = vp.Height }
                : new ViewportSize { Width = 412, Height = 839 },
            ScreenSize = source.ScreenSize is { } screen
                ? new ScreenSize { Width = screen.Width, Height = screen.Height }
                : new ScreenSize { Width = 412, Height = 915 },
            IsMobile = source.IsMobile ?? true,
            HasTouch = source.HasTouch ?? true,
            DeviceScaleFactor = source.DeviceScaleFactor ?? 2.625f,
            Locale = "en-US",
            ExtraHTTPHeaders = MobileHeaders(),
        };
    }

    private static Dictionary<string, string> MobileHeaders() => new()
    {
        ["Sec-Ch-Ua-Mobile"] = "?1",
        ["Accept-Language"] = "en-US,en;q=0.9",
    };
}
