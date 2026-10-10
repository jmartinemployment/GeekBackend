using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace GeekAPI.Services.ContentCreatorV2.Hierarchy;

/// <summary>
/// Plain visible text from a DOM node. No data-gsv / twin filtering — mobile HTML is already one viewport.
/// </summary>
internal static class GccV2TextExtractor
{
    private static readonly HashSet<string> SkipTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "template", "noscript",
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "br", "dd", "div", "dl", "dt",
        "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6",
        "header", "hr", "li", "main", "nav", "ol", "p", "pre", "section", "table", "tbody",
        "td", "th", "thead", "tr", "ul",
    };

    public static string Extract(HtmlNode node)
    {
        var sb = new StringBuilder();
        Walk(node, sb);
        return Collapse(WebUtility.HtmlDecode(sb.ToString()));
    }

    public static string Collapse(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void Walk(HtmlNode node, StringBuilder sb)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            sb.Append(node.InnerText);
            return;
        }

        if (node.NodeType != HtmlNodeType.Element && node.NodeType != HtmlNodeType.Document)
            return;

        if (SkipTags.Contains(node.Name))
            return;

        var hidden = node.GetAttributeValue("data-gcc-hidden", "");
        if (hidden == "1" || hidden.Equals("true", StringComparison.OrdinalIgnoreCase))
            return;

        hidden = node.GetAttributeValue("data-geek-hidden", "");
        if (hidden == "1" || hidden.Equals("true", StringComparison.OrdinalIgnoreCase))
            return;

        var block = BlockTags.Contains(node.Name);
        if (block)
            sb.Append(' ');

        foreach (var child in node.ChildNodes)
            Walk(child, sb);

        if (block)
            sb.Append(' ');
    }
}
