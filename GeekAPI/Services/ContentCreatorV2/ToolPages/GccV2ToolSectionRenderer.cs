using HtmlAgilityPack;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

/// <summary>
/// v2-only HTML helpers for partner tool pages — blockquote cite attribution copied from the
/// workflow export pattern but kept separate from <c>SectionHtmlRenderer</c>.
/// </summary>
public static class GccV2ToolSectionRenderer
{
    public static string RenderSourceAttribution(string sourceUrl, string excerpt, string toolName, bool includeVisitLink = true)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl) || string.IsNullOrWhiteSpace(excerpt))
            return "";

        var doc = new HtmlDocument();
        var container = doc.CreateElement("div");
        doc.DocumentNode.AppendChild(container);

        var blockquote = doc.CreateElement("blockquote");
        blockquote.SetAttributeValue("cite", EncodeAttribute(sourceUrl));
        var p = doc.CreateElement("p");
        p.AppendChild(CreateEncodedTextNode(doc, excerpt.Trim()));
        blockquote.AppendChild(p);
        container.AppendChild(blockquote);

        if (includeVisitLink)
        {
            var visitP = doc.CreateElement("p");
            var anchor = doc.CreateElement("a");
            anchor.SetAttributeValue("href", EncodeAttribute(sourceUrl));
            anchor.AppendChild(CreateEncodedTextNode(doc, $"Visit {toolName.Trim()}"));
            visitP.AppendChild(anchor);
            container.AppendChild(visitP);
        }

        return container.InnerHtml;
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
