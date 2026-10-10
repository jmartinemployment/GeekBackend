using System.Text.RegularExpressions;
using GeekApplication.Models.ContentCreator;
using HtmlAgilityPack;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>
/// Copied/adapted from Content Writer v2 KeywordHtmlParserService article path.
/// Canonical Content Creator extract — not synced upstream to CWV2.
/// </summary>
public static class GccV2ArticleHtmlExtractor
{
    public static GccQuoteablePage Extract(string url, string html) =>
        Extract(url, html, GccResearchCaps.MaxTitleChars, GccResearchCaps.MaxHeadingChars,
            GccResearchCaps.MaxParagraphChars, GccResearchCaps.MaxHeadingsPerPage,
            GccResearchCaps.MaxParagraphsPerPage, charBudget: null);

    /// <summary>
    /// Partner-tool page extract: higher per-field caps and an optional total character budget
    /// so WRITE gets real page content rather than a short synopsis.
    /// </summary>
    public static GccQuoteablePage ExtractPartnerPage(string url, string html) =>
        Extract(url, html, GccPartnerResearchCaps.MaxTitleChars, GccPartnerResearchCaps.MaxHeadingChars,
            GccPartnerResearchCaps.MaxParagraphChars, GccPartnerResearchCaps.MaxHeadingsPerPage,
            GccPartnerResearchCaps.MaxParagraphsPerPage, GccPartnerResearchCaps.MaxCharsPerPage);

    public static GccQuoteablePage Extract(
        string url,
        string html,
        int maxTitleChars,
        int maxHeadingChars,
        int maxParagraphChars,
        int maxHeadings,
        int maxParagraphs,
        int? charBudget)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = CleanText(HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//title")?.InnerText));
        if (string.IsNullOrWhiteSpace(title))
            title = CleanText(HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//h1")?.InnerText));
        if (string.IsNullOrWhiteSpace(title))
            title = url;

        title = Truncate(title, maxTitleChars);
        var used = title.Length;

        var headings = new List<HeadingDto>();
        var headingNodes = doc.DocumentNode.SelectNodes("//h1 | //h2 | //h3 | //h4 | //h5 | //h6");
        if (headingNodes is not null)
        {
            foreach (var node in headingNodes)
            {
                var text = Truncate(CleanText(node.InnerText), maxHeadingChars);
                if (text.Length <= 2) continue;
                if (charBudget is int budget && used + text.Length > budget) break;
                var level = int.Parse(node.Name[1..]);
                headings.Add(new HeadingDto(level, text));
                used += text.Length;
                if (headings.Count >= maxHeadings) break;
            }
        }

        var paragraphs = new List<string>();
        var paragraphNodes = doc.DocumentNode.SelectNodes("//p");
        if (paragraphNodes is not null)
        {
            foreach (var node in paragraphNodes)
            {
                var text = Truncate(CleanText(node.InnerText), maxParagraphChars);
                if (text.Length <= 20) continue;
                if (charBudget is int budget && used + text.Length > budget) break;
                paragraphs.Add(text);
                used += text.Length;
                if (paragraphs.Count >= maxParagraphs) break;
            }
        }

        if (paragraphs.Count < maxParagraphs)
        {
            AddTextNodes(doc, "//blockquote", paragraphs, ref used, maxParagraphChars, maxParagraphs, charBudget);
        }

        if (paragraphs.Count < maxParagraphs)
        {
            AddTextNodes(doc, "//li", paragraphs, ref used, maxParagraphChars, maxParagraphs, charBudget);
        }

        return new GccQuoteablePage(url, title, headings, paragraphs);
    }

    private static void AddTextNodes(
        HtmlDocument doc,
        string xpath,
        List<string> paragraphs,
        ref int used,
        int maxParagraphChars,
        int maxParagraphs,
        int? charBudget)
    {
        var nodes = doc.DocumentNode.SelectNodes(xpath);
        if (nodes is null) return;
        foreach (var node in nodes)
        {
            var text = Truncate(CleanText(node.InnerText), maxParagraphChars);
            if (text.Length <= 20) continue;
            if (paragraphs.Contains(text, StringComparer.Ordinal)) continue;
            if (charBudget is int budget && used + text.Length > budget) break;
            paragraphs.Add(text);
            used += text.Length;
            if (paragraphs.Count >= maxParagraphs) break;
        }
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
