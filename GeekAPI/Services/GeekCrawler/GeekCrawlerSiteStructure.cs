using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>
/// One anchor, with the prose it sits inside.
///
/// The crawler records an anchor as <c>{ label, href }</c> and nothing else — there is no rel, no
/// title, no target. <see cref="Context"/> is not part of the anchor: it is the text of the block
/// the anchor appeared in, which is the only thing that says what the link is about. A bare
/// "Learn more" is useless without the sentence around it.
/// </summary>
public sealed record SiteStructureLink(string Label, string Href, string Context, string ContextKind);

public sealed record SiteStructureNode(
    int Level,
    string HeadingText,
    IReadOnlyList<string> Paragraphs,
    IReadOnlyList<SiteStructureLink> Links,
    IReadOnlyList<SiteStructureNode> Children);

public sealed record SiteStructurePage(string PageUrl, IReadOnlyList<SiteStructureNode> Roots);

/// <summary>
/// A run's site structure, plus what was left out and why.
/// </summary>
/// <param name="PagesWithoutBlocks">
/// Pages excluded because extraction produced no blocks. Reported rather than hidden: a run whose
/// pages carry Html and no blocks must read as exactly that, never as an empty tree.
/// </param>
public sealed record SiteStructure(
    string RunId,
    DateTimeOffset BuiltAtUtc,
    int PagesConsidered,
    int PagesWithoutBlocks,
    IReadOnlyList<SiteStructurePage> Pages);

/// <summary>
/// Builds a page's heading tree from the crawler's typed <c>blocks</c>.
///
/// The crawler already derived this structure once, in extract-content.ts, and kept
/// <c>heading.level</c>, per-block <c>html</c> and per-block <c>anchors</c> on every block. Reading
/// those is the whole job. Re-parsing raw Html here would be a second derivation of the same thing
/// from a different representation, which is how the two halves drift apart.
///
/// Blocks arrive as opaque JSON: GeekAPI carries <c>GeekCrawlerPageDto.Blocks</c> through as a
/// <see cref="JsonElement"/> without a schema, so every field is read defensively. A block missing
/// <c>kind</c> or a heading missing <c>level</c> is a defect in extraction, and it shows up here as
/// a page with no structure rather than as a guess.
/// </summary>
public static class GeekCrawlerSiteStructure
{
    public static SiteStructure Build(Guid runId, IReadOnlyList<GeekCrawlerPageDto> pages)
    {
        var built = new List<SiteStructurePage>();
        var withoutBlocks = 0;

        foreach (var page in pages)
        {
            var blocks = ReadBlocks(page.Blocks);
            if (blocks.Count == 0)
            {
                withoutBlocks++;
                continue;
            }

            var roots = BuildTree(blocks);
            if (roots.Count == 0) continue;

            built.Add(new SiteStructurePage(
                string.IsNullOrWhiteSpace(page.FinalUrl) ? page.Url : page.FinalUrl,
                roots));
        }

        return new SiteStructure(
            runId.ToString("D"),
            DateTimeOffset.UtcNow,
            pages.Count,
            withoutBlocks,
            built);
    }

    /// <summary>
    /// Nest blocks into headings.
    ///
    /// A heading opens a node at its own level, closing every open node at that level or deeper.
    /// Non-heading blocks attach their text and anchors to whichever heading is open, which is what
    /// makes "the links under this section" answerable — the flat text projection cannot express it.
    /// Blocks before the first heading belong to the page rather than to a section, and are dropped.
    /// </summary>
    private static IReadOnlyList<SiteStructureNode> BuildTree(IReadOnlyList<JsonElement> blocks)
    {
        var roots = new List<MutableNode>();
        var stack = new List<MutableNode>();

        foreach (var block in blocks)
        {
            var kind = ReadString(block, "kind");

            if (string.Equals(kind, "heading", StringComparison.OrdinalIgnoreCase))
            {
                var level = ReadInt(block, "level");
                if (level is null or < 1 or > 6) continue;

                var node = new MutableNode
                {
                    Level = level.Value,
                    HeadingText = ReadString(block, "text")?.Trim() ?? string.Empty,
                };
                node.Links.AddRange(ReadAnchors(block, ReadText(block), "heading"));

                while (stack.Count > 0 && stack[^1].Level >= node.Level)
                    stack.RemoveAt(stack.Count - 1);

                if (stack.Count == 0) roots.Add(node);
                else stack[^1].Children.Add(node);

                stack.Add(node);
                continue;
            }

            if (stack.Count == 0) continue;

            var open = stack[^1];
            var text = ReadText(block);
            if (!string.IsNullOrWhiteSpace(text)) open.Paragraphs.Add(text);
            open.Links.AddRange(ReadAnchors(block, text, ReadString(block, "kind")?.Trim() ?? ""));
        }

        return roots.ConvertAll(r => r.Seal());
    }

    /// <summary>Row blocks carry <c>cells</c> instead of <c>text</c>; everything else carries text.</summary>
    private static string ReadText(JsonElement block)
    {
        var text = ReadString(block, "text");
        if (!string.IsNullOrWhiteSpace(text)) return text.Trim();

        if (block.ValueKind == JsonValueKind.Object
            && block.TryGetProperty("cells", out var cells)
            && cells.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var cell in cells.EnumerateArray())
            {
                if (cell.ValueKind == JsonValueKind.String)
                {
                    var value = cell.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) parts.Add(value.Trim());
                }
            }

            if (parts.Count > 0) return string.Join(" | ", parts);
        }

        return string.Empty;
    }

    private static IReadOnlyList<JsonElement> ReadBlocks(JsonElement? blocks)
    {
        if (blocks is not { ValueKind: JsonValueKind.Array } array) return [];

        var list = new List<JsonElement>();
        foreach (var block in array.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object) list.Add(block);
        }

        return list;
    }

    /// <param name="context">The containing block's text — what the link is about.</param>
    /// <param name="contextKind">That block's kind: paragraph, listItem, heading, row, and so on.</param>
    private static List<SiteStructureLink> ReadAnchors(JsonElement block, string context, string contextKind)
    {
        var links = new List<SiteStructureLink>();
        if (block.ValueKind != JsonValueKind.Object) return links;
        if (!block.TryGetProperty("anchors", out var anchors)) return links;
        if (anchors.ValueKind != JsonValueKind.Array) return links;

        foreach (var anchor in anchors.EnumerateArray())
        {
            if (anchor.ValueKind != JsonValueKind.Object) continue;

            var href = ReadString(anchor, "href");
            if (string.IsNullOrWhiteSpace(href)) continue;

            // The crawler writes "label". "text" is accepted too so a page stored by an older
            // extractor still yields a name rather than a blank row.
            var label = ReadString(anchor, "label") ?? ReadString(anchor, "text");

            links.Add(new SiteStructureLink(
                label?.Trim() ?? string.Empty,
                href.Trim(),
                context,
                contextKind));
        }

        return links;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
            return parsed;
        return null;
    }

    private sealed class MutableNode
    {
        public int Level { get; init; }
        public string HeadingText { get; init; } = string.Empty;
        public List<string> Paragraphs { get; } = [];
        public List<SiteStructureLink> Links { get; } = [];
        public List<MutableNode> Children { get; } = [];

        public SiteStructureNode Seal() => new(
            Level,
            HeadingText,
            Paragraphs,
            Links,
            Children.ConvertAll(c => c.Seal()));
    }
}
