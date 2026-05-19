using Microsoft.Playwright;

namespace GeekRepository.Infrastructure;

public sealed class PlaywrightBrowserHolder : IAsyncDisposable
{
    private IPlaywright? _playwright;
    public IBrowser? Browser { get; private set; }

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
            await Browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
