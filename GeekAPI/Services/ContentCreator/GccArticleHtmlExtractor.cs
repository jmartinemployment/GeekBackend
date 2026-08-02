using System.Text.RegularExpressions;
using GeekApplication.Models.ContentCreator;
using HtmlAgilityPack;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Copied/adapted from Content Writer v2 KeywordHtmlParserService article path.
/// Canonical Content Creator extract — not synced upstream to CWV2.
/// </summary>
public static class GccArticleHtmlExtractor
{
    public static GccQuoteablePage Extract(string url, string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = CleanText(HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//title")?.InnerText));
        if (string.IsNullOrWhiteSpace(title))
            title = CleanText(HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//h1")?.InnerText));
        if (string.IsNullOrWhiteSpace(title))
            title = url;

        title = Truncate(title, GccResearchCaps.MaxTitleChars);

        var headings = new List<string>();
        var headingNodes = doc.DocumentNode.SelectNodes("//h1 | //h2 | //h3");
        if (headingNodes is not null)
        {
            foreach (var node in headingNodes)
            {
                var text = Truncate(CleanText(node.InnerText), GccResearchCaps.MaxHeadingChars);
                if (text.Length > 2)
                    headings.Add(text);
                if (headings.Count >= GccResearchCaps.MaxHeadingsPerPage)
                    break;
            }
        }

        var paragraphs = new List<string>();
        var paragraphNodes = doc.DocumentNode.SelectNodes("//p");
        if (paragraphNodes is not null)
        {
            foreach (var node in paragraphNodes)
            {
                var text = Truncate(CleanText(node.InnerText), GccResearchCaps.MaxParagraphChars);
                if (text.Length > 20)
                    paragraphs.Add(text);
                if (paragraphs.Count >= GccResearchCaps.MaxParagraphsPerPage)
                    break;
            }
        }

        return new GccQuoteablePage(url, title, headings, paragraphs);
    }

    public static bool IsEmpty(GccQuoteablePage page) =>
        page.Headings.Count == 0 && page.Paragraphs.Count == 0;

    private static string CleanText(string? raw)
    {
        var decoded = HtmlEntity.DeEntitize(raw) ?? string.Empty;
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
