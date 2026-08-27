using Microsoft.Playwright;

namespace GeekAPI.Services.ContentCreatorV2.Hierarchy;

/// <summary>Process-lifetime Chromium for Content Creator hierarchy crawl. Fail closed if launch fails.</summary>
public sealed class GccV2PlaywrightBrowserHolder : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _playwright;

    public IBrowser? Browser { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            await LaunchCoreAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"ERROR: Content Creator Playwright/Chromium launch failed. Hierarchy crawl soft-fails until retry. {ex}");
            Browser = null;
        }
    }

    public async Task<IBrowser?> EnsureBrowserAsync(CancellationToken ct = default)
    {
        if (Browser is not null)
            return Browser;

        await _gate.WaitAsync(ct);
        try
        {
            if (Browser is not null)
                return Browser;

            try
            {
                await LaunchCoreAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"ERROR: Content Creator Playwright/Chromium retry launch failed. {ex}");
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
        _playwright ??= await Playwright.CreateAsync();
        GccV2CrawlerIdentity.UsePlaywrightDevices(_playwright);
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
            await Browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
