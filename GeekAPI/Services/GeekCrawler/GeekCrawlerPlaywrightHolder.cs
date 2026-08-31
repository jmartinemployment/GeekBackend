using Microsoft.Playwright;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Process-lifetime Chromium for Geek-Crawler. Fail closed if launch fails.</summary>
public sealed class GeekCrawlerPlaywrightHolder : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _playwright;

    public IBrowser? Browser { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            await LaunchCoreAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"ERROR: Geek-Crawler Playwright/Chromium launch failed. Crawl fetch soft-fails until retry. {ex}");
            Browser = null;
        }
    }

    public async Task<IBrowser?> EnsureBrowserAsync(CancellationToken ct = default)
    {
        if (Browser is not null)
            return Browser;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Browser is not null)
                return Browser;

            try
            {
                await LaunchCoreAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: Geek-Crawler Playwright/Chromium retry launch failed. {ex}");
                Browser = null;
            }

            return Browser;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LaunchCoreAsync()
    {
        _playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);
        GeekCrawlerMobileIdentity.UsePlaywrightDevices(_playwright);
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true })
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
            await Browser.DisposeAsync().ConfigureAwait(false);
        _playwright?.Dispose();
    }
}
