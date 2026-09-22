using HtmlAgilityPack;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Reads raw <c>&lt;script type="application/ld+json"&gt;</c> block text out of a page's HTML.
/// </summary>
/// <remarks>
/// Ported from <c>SiteCrawlerService.ExtractJsonLd</c> (Workflow), which is <c>private</c> and
/// scoped to that service's own live-fetch pipeline. This is the same twelve lines made reusable
/// for persisted crawl pages, which is where Stage 8's competitor/partner analysis reads from --
/// not a second implementation of anything <see cref="Workflow.Services.JsonLd.JsonLdParserService"/>
/// already owns; that class starts from the raw block strings this produces.
/// </remarks>
public static class GccJsonLdBlockExtractor
{
    public static IReadOnlyList<string> Extract(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts is null)
        {
            return [];
        }

        var blocks = new List<string>();
        foreach (var script in scripts)
        {
            var content = HtmlEntity.DeEntitize(script.InnerText)?.Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                blocks.Add(content);
            }
        }
        return blocks;
    }
}
