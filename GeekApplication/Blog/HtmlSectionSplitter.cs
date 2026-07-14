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
/// Splits a single HTML blob into ordered <see cref="HtmlSection"/> rows on &lt;h2&gt; boundaries.
/// Any content before the first &lt;h2&gt; becomes sort_order 0 with a null heading.
/// Reused by ImportBlogContent and mirrored (same splitting rule) in content-writer's publish service.
/// </summary>
public static class HtmlSectionSplitter
{
    public static IReadOnlyList<HtmlSection> Split(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var sections = new List<HtmlSection>();
        var sortOrder = 0;
        string? currentHeadingTag = null;
        string? currentHeadingText = null;
        var buffer = new StringBuilder();

        void Flush()
        {
            var bodyContent = buffer.ToString().Trim();
            if (bodyContent.Length == 0 && currentHeadingTag is null)
            {
                buffer.Clear();
                return;
            }

            sections.Add(new HtmlSection(sortOrder, currentHeadingTag, currentHeadingText, bodyContent));
            sortOrder++;
            buffer.Clear();
        }

        foreach (var node in doc.DocumentNode.ChildNodes.ToList())
        {
            if (node.NodeType == HtmlNodeType.Element
                && string.Equals(node.Name, "h2", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                currentHeadingTag = "h2";
                currentHeadingText = HtmlEntity.DeEntitize(node.InnerText)?.Trim() ?? string.Empty;
                continue;
            }

            buffer.Append(node.OuterHtml);
        }

        Flush();

        return sections;
    }
}
