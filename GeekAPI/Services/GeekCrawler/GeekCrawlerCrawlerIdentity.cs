using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Playwright;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Mobile Pixel 7 bot identity for Geek-Crawler — never desktop.</summary>
public static class GeekCrawlerCrawlerIdentity
{
    public static BrowserNewContextOptions MobileContext()
    {
        var ctx = GccV2CrawlerIdentity.MobileContext();
        ctx.UserAgent = GeekCrawlerCaps.UserAgent;
        return ctx;
    }

    public static Task WaitForRenderedAsync(IPage page) =>
        GccV2CrawlerIdentity.WaitForRenderedAsync(page);
}
