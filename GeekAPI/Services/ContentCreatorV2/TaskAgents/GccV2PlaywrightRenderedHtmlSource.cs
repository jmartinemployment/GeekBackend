using GeekAPI.Services.ContentCreatorV2.Hierarchy;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

/// <summary>Optional rendered-HTML source used when plain HTTP extraction is thin or empty.</summary>
public interface IGccV2RenderedHtmlSource
{
    Task<GccV2RenderedHtml?> TryFetchAsync(string url, CancellationToken ct);
}

public sealed record GccV2RenderedHtml(string FinalUrl, string Html, int StatusCode);

/// <summary>Mobile Playwright adapter for Knowledge / task-agent URL hydrate fallback.</summary>
public sealed class GccV2PlaywrightRenderedHtmlSource(GccV2PageFetcher pageFetcher) : IGccV2RenderedHtmlSource
{
    public async Task<GccV2RenderedHtml?> TryFetchAsync(string url, CancellationToken ct)
    {
        var page = await pageFetcher.FetchAsync(url, ct).ConfigureAwait(false);
        if (page is null || string.IsNullOrWhiteSpace(page.Html))
            return null;
        return new GccV2RenderedHtml(page.FinalUrl, page.Html, page.StatusCode);
    }
}
