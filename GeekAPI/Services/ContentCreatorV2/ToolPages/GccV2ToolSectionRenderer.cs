using HtmlAgilityPack;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

/// <summary>
/// v2-only HTML helpers for partner tool pages — blockquote cite attribution copied from the
/// workflow export pattern but kept separate from <c>SectionHtmlRenderer</c>.
/// </summary>
public static class GccV2ToolSectionRenderer
{
    public static string RenderSourceAttribution(string sourceUrl, string quoteText, string toolName, bool includeVisitLink = true)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl) || string.IsNullOrWhiteSpace(quoteText))
            return "";

        var formattedQuote = FormatBlockQuoteText(quoteText);
        var doc = new HtmlDocument();
        var container = doc.CreateElement("div");
        container.SetAttributeValue("class", "gcc-tool-source-attribution");
        doc.DocumentNode.AppendChild(container);

        var blockquote = doc.CreateElement("blockquote");
        blockquote.SetAttributeValue("cite", EncodeAttribute(sourceUrl));
        blockquote.SetAttributeValue("class", "gcc-source-blockquote");
        var p = doc.CreateElement("p");
        p.AppendChild(CreateEncodedTextNode(doc, formattedQuote));
        blockquote.AppendChild(p);
        container.AppendChild(blockquote);

        if (includeVisitLink)
        {
            var visitP = doc.CreateElement("p");
            visitP.SetAttributeValue("class", "gcc-source-visit-link");
            var anchor = doc.CreateElement("a");
            anchor.SetAttributeValue("href", EncodeAttribute(sourceUrl));
            anchor.SetAttributeValue("target", "_blank");
            anchor.SetAttributeValue("rel", "noopener noreferrer");
            anchor.AppendChild(CreateEncodedTextNode(doc, $"Visit {toolName.Trim()}"));
            visitP.AppendChild(anchor);
            container.AppendChild(visitP);
        }

        return container.InnerHtml;
    }

    /// <summary>Wraps verbatim source text in typographic quotation marks for display.</summary>
    public static string FormatBlockQuoteText(string text)
    {
        var inner = GccV2ToolResearchExtractor.StripWrappingQuotes(text);
        if (string.IsNullOrWhiteSpace(inner)) return "";
        return $"\u201C{inner}\u201D";
    }

    public static string InjectBeforeBodyClose(string fullDocumentHtml, string trailingBodyHtml)
    {
        if (string.IsNullOrWhiteSpace(trailingBodyHtml)) return fullDocumentHtml;
        const string closeBody = "</body>";
        var idx = fullDocumentHtml.LastIndexOf(closeBody, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return fullDocumentHtml + trailingBodyHtml;
        return fullDocumentHtml[..idx] + trailingBodyHtml + fullDocumentHtml[idx..];
    }

    private static HtmlNode CreateEncodedTextNode(HtmlDocument doc, string text) =>
        doc.CreateTextNode(System.Net.WebUtility.HtmlEncode(text));

    private static string EncodeAttribute(string value) => System.Net.WebUtility.HtmlEncode(value);
}
