using Microsoft.Playwright;

namespace GeekAPI.Services.ContentCreatorV2.Hierarchy;

/// <summary>
/// Bot identity plus Playwright <c>devices['Pixel 7']</c> — mobile-only, like Site Analyzer / Google.
/// </summary>
internal static class GccV2CrawlerIdentity
{
    public const string PlaywrightDeviceName = "Pixel 7";
    public const string ViewportLabel = "mobile";

    public const string UserAgent =
        "Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/134.0.6998.35 Mobile Safari/537.36 (compatible; GeekContentCreator/2.0; +https://geekatyourspot.com)";

    public static int MobileViewportWidth { get; private set; } = 412;
    public static int MobileViewportHeight { get; private set; } = 839;
    public static float DeviceScaleFactor { get; private set; } = 2.625f;
    public static int MobileScreenWidth { get; private set; } = 412;
    public static int MobileScreenHeight { get; private set; } = 915;

    public const int RenderQuiescenceCapMs = 5000;
    public const int NavigationTimeoutMs = 30_000;

    private static BrowserNewContextOptions? _playwrightPixel7;

    public static void UsePlaywrightDevices(IPlaywright playwright)
    {
        if (!playwright.Devices.TryGetValue(PlaywrightDeviceName, out var device))
        {
            throw new InvalidOperationException(
                $"Playwright.Devices has no '{PlaywrightDeviceName}'");
        }

        _playwrightPixel7 = ClonePixel7(device);
        if (device.ViewportSize is { } vp)
        {
            MobileViewportWidth = vp.Width;
            MobileViewportHeight = vp.Height;
        }

        if (device.ScreenSize is { } screen)
        {
            MobileScreenWidth = screen.Width;
            MobileScreenHeight = screen.Height;
        }

        if (device.DeviceScaleFactor is { } dpr)
            DeviceScaleFactor = (float)dpr;
    }

    public static BrowserNewContextOptions MobileContext() =>
        ClonePixel7(_playwrightPixel7) ?? FallbackPixel7();

    public static async Task WaitForRenderedAsync(IPage page)
    {
        try
        {
            await page.WaitForLoadStateAsync(
                LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = RenderQuiescenceCapMs });
        }
        catch (Exception)
        {
            // Cap reached or render wait failed — snapshot anyway.
        }
    }

    private static BrowserNewContextOptions FallbackPixel7() => new()
    {
        UserAgent = UserAgent,
        ViewportSize = new ViewportSize
        {
            Width = MobileViewportWidth,
            Height = MobileViewportHeight,
        },
        ScreenSize = new ScreenSize
        {
            Width = MobileScreenWidth,
            Height = MobileScreenHeight,
        },
        IsMobile = true,
        HasTouch = true,
        DeviceScaleFactor = DeviceScaleFactor,
        Locale = "en-US",
        ExtraHTTPHeaders = MobileHeaders(),
    };

    private static BrowserNewContextOptions ClonePixel7(BrowserNewContextOptions? source)
    {
        if (source is null)
            return FallbackPixel7();

        return new BrowserNewContextOptions
        {
            UserAgent = WithBotToken(source.UserAgent),
            ViewportSize = source.ViewportSize is { } vp
                ? new ViewportSize { Width = vp.Width, Height = vp.Height }
                : new ViewportSize { Width = MobileViewportWidth, Height = MobileViewportHeight },
            ScreenSize = source.ScreenSize is { } screen
                ? new ScreenSize { Width = screen.Width, Height = screen.Height }
                : new ScreenSize { Width = MobileScreenWidth, Height = MobileScreenHeight },
            IsMobile = source.IsMobile ?? true,
            HasTouch = source.HasTouch ?? true,
            DeviceScaleFactor = source.DeviceScaleFactor ?? DeviceScaleFactor,
            Locale = "en-US",
            ExtraHTTPHeaders = MobileHeaders(),
        };
    }

    private static string WithBotToken(string? deviceUserAgent)
    {
        if (string.IsNullOrWhiteSpace(deviceUserAgent))
            return UserAgent;
        if (deviceUserAgent.Contains("GeekContentCreator", StringComparison.Ordinal))
            return deviceUserAgent;
        return $"{deviceUserAgent} (compatible; GeekContentCreator/2.0; +https://geekatyourspot.com)";
    }

    private static Dictionary<string, string> MobileHeaders() => new()
    {
        ["Sec-Ch-Ua-Mobile"] = "?1",
        ["Accept-Language"] = "en-US,en;q=0.9",
    };
}
