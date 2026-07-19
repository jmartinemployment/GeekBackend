using System.Text;
using HtmlAgilityPack;

namespace GeekApplication.Blog;

/// <summary>One flat HTML section, matching a geek_blog.post_sections row.</summary>
public sealed record HtmlSection(
    int SortOrder,
    string? HeadingTag,
    string? HeadingText,
    string BodyContent,
    string? MediaUrl = null,
    string? MediaAlt = null);

/// <summary>
/// Production ingestion engine that parses a raw HTML document or fragment into ordered,
/// independent <see cref="HtmlSection"/> rows for geek_blog.post_sections.
///
/// Every &lt;h2&gt; is a definitive section boundary: its inner text becomes heading_text, and
/// every sibling node up to the next &lt;h2&gt; is captured as clean outer HTML for body_content.
/// Each section is then scanned for an immediate-child &lt;img&gt;, whose src/alt populate
/// media_url/media_alt (left null when no image is present).
///
/// Resolves to the &lt;body&gt; subtree when given a full document, so &lt;head&gt;/&lt;title&gt;/JSON-LD
/// &lt;script&gt; content never leaks into a section and &lt;h2&gt; boundaries nested under &lt;body&gt; are
/// still found. Falls back to the document root for bare content fragments.
///
/// Leading content before the first &lt;h2&gt; is preserved as sort_order 0 with a null heading,
/// so no raw copy is lost.
/// </summary>
public static class HtmlContentIngestionEngine
{
    public static IReadOnlyList<HtmlSection> Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var document = new HtmlDocument();
        document.LoadHtml(html);

        var root = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;

        var sections = new List<HtmlSection>();
        var sortOrder = 0;
        string? headingTag = null;
        string? headingText = null;
        string? mediaUrl = null;
        string? mediaAlt = null;
        var body = new StringBuilder();

        void EmitSection()
        {
            var bodyContent = body.ToString().Trim();
            if (bodyContent.Length == 0 && headingTag is null)
            {
                body.Clear();
                return;
            }

            sections.Add(new HtmlSection(sortOrder, headingTag, headingText, bodyContent, mediaUrl, mediaAlt));

            sortOrder++;
            body.Clear();
            mediaUrl = null;
            mediaAlt = null;
        }

        foreach (var node in root.ChildNodes.ToList())
        {
            if (IsSectionBoundary(node))
            {
                EmitSection();
                headingTag = "h2";
                headingText = HtmlEntity.DeEntitize(node.InnerText)?.Trim() ?? string.Empty;
                continue;
            }

            if (mediaUrl is null && TryExtractMedia(node, out var src, out var alt))
            {
                mediaUrl = src;
                mediaAlt = alt;
            }

            body.Append(node.OuterHtml);
        }

        EmitSection();

        return sections;
    }

    private static bool IsSectionBoundary(HtmlNode node) =>
        node.NodeType == HtmlNodeType.Element
        && string.Equals(node.Name, "h2", StringComparison.OrdinalIgnoreCase);

    private static bool TryExtractMedia(HtmlNode node, out string? src, out string? alt)
    {
        src = null;
        alt = null;

        if (node.NodeType != HtmlNodeType.Element
            || !string.Equals(node.Name, "img", StringComparison.OrdinalIgnoreCase))
            return false;

        var srcValue = node.GetAttributeValue("src", string.Empty);
        if (string.IsNullOrWhiteSpace(srcValue))
            return false;

        src = srcValue;
        var altValue = node.GetAttributeValue("alt", string.Empty);
        alt = string.IsNullOrEmpty(altValue) ? null : altValue;
        return true;
    }
}
