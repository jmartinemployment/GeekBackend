using HtmlAgilityPack;

namespace GeekAPI.Services.ContentCreatorV2.Hierarchy;

/// <summary>
/// DOM → nested heading tree from a <b>mobile</b> viewport snapshot.
/// Skips nodes marked <c>data-gcc-hidden</c> (CSS-hidden at capture time) so responsive twins
/// that are not shown on mobile are not walked. There is no desktop crawl.
/// </summary>
public static class GccV2HeadingTreeBuilder
{
    public static IReadOnlyList<GccV2HeadingNode> Build(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html ?? string.Empty);

        var roots = new List<MutableNode>();
        var stack = new List<MutableNode>();

        foreach (var node in doc.DocumentNode.ChildNodes)
            ProcessNode(node, roots, stack);

        return roots.ConvertAll(r => r.Seal());
    }

    private static void ProcessNode(HtmlNode node, List<MutableNode> roots, List<MutableNode> stack)
    {
        if (node.NodeType != HtmlNodeType.Element)
            return;

        // Not displayed in the mobile viewport at snapshot time — do not walk (avoids twin copies).
        if (IsHiddenAtMobileViewport(node))
            return;

        var tagName = node.Name.ToLowerInvariant();
        if (tagName is "script" or "style" or "template" or "noscript")
            return;

        if (tagName.Length == 2 && tagName[0] == 'h' && char.IsDigit(tagName[1]))
        {
            if (!int.TryParse(tagName[1].ToString(), out var level) || level is < 1 or > 6)
                return;

            var text = GccV2TextExtractor.Extract(node);
            var newNode = new MutableNode { Level = level, HeadingText = text };

            while (stack.Count > 0 && stack[^1].Level >= level)
                stack.RemoveAt(stack.Count - 1);

            var parent = stack.Count == 0 ? null : stack[^1];
            var siblings = parent?.Children ?? roots;

            siblings.Add(newNode);
            stack.Add(newNode);

            foreach (var child in node.ChildNodes)
            {
                if (child.NodeType != HtmlNodeType.Element) continue;
                if (child.Name.Equals("a", StringComparison.OrdinalIgnoreCase))
                    AttachLink(child, stack);
                else
                {
                    foreach (var anchor in child.Descendants("a"))
                        AttachLink(anchor, stack);
                }
            }

            return;
        }

        if (tagName == "p")
        {
            AttachContent(node, stack);
            return;
        }

        if (tagName == "li")
        {
            if (ContainsHeading(node))
            {
                foreach (var child in node.ChildNodes)
                    ProcessNode(child, roots, stack);
                return;
            }

            AttachContent(node, stack);
            return;
        }

        if (tagName == "a")
        {
            if (ContainsHeading(node))
            {
                foreach (var child in node.ChildNodes)
                    ProcessNode(child, roots, stack);
            }

            AttachLink(node, stack);
            return;
        }

        foreach (var child in node.ChildNodes)
            ProcessNode(child, roots, stack);
    }

    private static void AttachContent(HtmlNode node, List<MutableNode> stack)
    {
        if (stack.Count == 0)
            return;

        var (paragraphText, links) = CleanAndExtractLinks(node);
        if (!string.IsNullOrWhiteSpace(paragraphText))
            stack[^1].Paragraphs.Add(paragraphText);
        stack[^1].Links.AddRange(links);
    }

    private static void AttachLink(HtmlNode node, List<MutableNode> stack)
    {
        if (stack.Count == 0)
            return;

        var href = node.GetAttributeValue("href", "").Trim();
        var text = GccV2TextExtractor.Extract(node);
        if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(text))
            return;

        stack[^1].Links.Add(new GccV2HeadingLink(
            text,
            href,
            (node.GetAttributeValue("rel", "") ?? "").Trim()));
    }

    private static bool ContainsHeading(HtmlNode node)
    {
        foreach (var child in node.Descendants())
        {
            if (child.NodeType != HtmlNodeType.Element)
                continue;
            var name = child.Name;
            if (name.Length == 2 && name[0] is 'h' or 'H' && char.IsDigit(name[1]))
                return true;
        }

        return false;
    }

    private static (string cleanText, List<GccV2HeadingLink> links) CleanAndExtractLinks(HtmlNode node)
    {
        var links = new List<GccV2HeadingLink>();
        var anchors = node.SelectNodes(".//a[@href]");
        if (anchors != null)
        {
            foreach (var anchor in anchors)
            {
                var href = anchor.GetAttributeValue("href", "").Trim();
                var text = GccV2TextExtractor.Extract(anchor);
                if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(text))
                    continue;
                links.Add(new GccV2HeadingLink(
                    text,
                    href,
                    (anchor.GetAttributeValue("rel", "") ?? "").Trim()));
            }
        }

        var cleanText = GccV2TextExtractor.Extract(node);
        return (cleanText, links);
    }

    private static bool IsHiddenAtMobileViewport(HtmlNode node)
    {
        var gcc = node.GetAttributeValue("data-gcc-hidden", "");
        if (gcc == "1" || gcc.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;

        var geek = node.GetAttributeValue("data-geek-hidden", "");
        return geek == "1" || geek.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MutableNode
    {
        public required int Level { get; init; }
        public required string HeadingText { get; init; }
        public List<string> Paragraphs { get; } = [];
        public List<GccV2HeadingLink> Links { get; } = [];
        public List<MutableNode> Children { get; } = [];

        public GccV2HeadingNode Seal() => new(
            Level,
            HeadingText,
            Paragraphs,
            Links,
            Children.ConvertAll(c => c.Seal()));
    }
}
