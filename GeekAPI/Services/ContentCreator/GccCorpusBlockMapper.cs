using System.Text.Json;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Maps the crawler's typed corpus blocks onto <see cref="Paragraph"/> node types, kind for kind.
/// </summary>
/// <remarks>
/// <para>This is the join the pipeline was missing. The crawler emits typed blocks
/// (<c>Geek-Crawler-v2/src/crawl/extract-content.ts:418-425</c>) and RAG retrieval flattens them to
/// a plaintext projection that discards every tag, href and heading marker by design. So a
/// retrieved glossary arrived as prose and a retrieved code sample as a paragraph. Reading the
/// blocks directly keeps the boundary in the data, which is the same fix the crawler applied when
/// it dropped Readability.</para>
/// <c>SectionHtmlRenderer</c>.</para>
/// </remarks>
public static class GccCorpusBlockMapper
{
    /// <summary>
    /// Converts one page's blocks into paragraphs. <paramref name="citeUrl"/> becomes the
    /// <see cref="QuoteParagraph.Cite"/> on any quote, which is what makes a retrieved quote
    /// attributable rather than anonymous prose.
    /// </summary>
    public static IReadOnlyList<Paragraph> MapBlocks(JsonElement? blocks, string? citeUrl)
    {
        if (blocks is not { ValueKind: JsonValueKind.Array } array)
        {
            return [];
        }

        var paragraphs = new List<Paragraph>();
        var pendingList = new List<IReadOnlyList<Run>>();
        var pendingListOrdered = false;
        var pendingDefinitions = new List<DefinitionItem>();
        IReadOnlyList<Run>? pendingTerm = null;

        void FlushList()
        {
            if (pendingList.Count > 0)
            {
                paragraphs.Add(new ListParagraph(pendingListOrdered, pendingList.ToList()));
                pendingList.Clear();
            }
        }

        void FlushDefinitions()
        {
            // A term with no definition is still a term; emitting it with an empty definition keeps
            // it visible rather than dropping it silently.
            if (pendingTerm is not null)
            {
                pendingDefinitions.Add(new DefinitionItem(pendingTerm, []));
                pendingTerm = null;
            }
            if (pendingDefinitions.Count > 0)
            {
                paragraphs.Add(new DefinitionParagraph(pendingDefinitions.ToList()));
                pendingDefinitions.Clear();
            }
        }

        void FlushAll()
        {
            FlushList();
            FlushDefinitions();
        }

        foreach (var block in array.EnumerateArray())
        {
            var kind = ReadString(block, "kind");
            var text = ReadString(block, "text") ?? string.Empty;

            switch (kind)
            {
                case "listItem":
                    FlushDefinitions();
                    var ordered = ReadBool(block, "ordered");
                    if (pendingList.Count > 0 && ordered != pendingListOrdered)
                    {
                        FlushList();
                    }
                    pendingListOrdered = ordered;
                    if (text.Length > 0)
                    {
                        pendingList.Add(BuildRuns(text, block));
                    }
                    break;

                case "term":
                    FlushList();
                    if (pendingTerm is not null)
                    {
                        pendingDefinitions.Add(new DefinitionItem(pendingTerm, []));
                    }
                    pendingTerm = text.Length > 0 ? BuildRuns(text, block) : null;
                    break;

                case "definition":
                    FlushList();
                    if (pendingTerm is not null)
                    {
                        pendingDefinitions.Add(new DefinitionItem(pendingTerm, BuildRuns(text, block)));
                        pendingTerm = null;
                    }
                    break;

                case "quote":
                    FlushAll();
                    if (text.Length > 0)
                    {
                        paragraphs.Add(new QuoteParagraph(BuildRuns(text, block), citeUrl));
                    }
                    break;

                case "code":
                    FlushAll();
                    if (text.Length > 0)
                    {
                        // Raw text: run formatting has no meaning in code, and the language is not
                        // carried on the block.
                        paragraphs.Add(new CodeParagraph(text));
                    }
                    break;

                case "row":
                    FlushAll();
                    // ContentDocument has no table node. Cells are joined rather than dropped —
                    // losing a comparison table entirely is worse than losing its grid.
                    var cells = ReadCells(block);
                    if (cells.Count > 0)
                    {
                        paragraphs.Add(new TextParagraph([new Run(string.Join(" | ", cells))]));
                    }
                    break;

                case "heading":
                    // Headings bound sections, not paragraphs. A caller slicing a page into
                    // sections reads them via ReadHeading; here they simply close open runs.
                    FlushAll();
                    break;

                case "paragraph":
                default:
                    FlushAll();
                    if (text.Length > 0)
                    {
                        paragraphs.Add(new TextParagraph(BuildRuns(text, block)));
                    }
                    break;
            }
        }

        FlushAll();
        return paragraphs;
    }

    /// <summary>
    /// The heading text and level of a block, or null when it is not a heading. Lets a caller cut a
    /// page into <see cref="Section"/>s without re-inspecting the JSON shape.
    /// </summary>
    public static (string Text, int Level)? ReadHeading(JsonElement block)
    {
        if (ReadString(block, "kind") != "heading")
        {
            return null;
        }

        var text = ReadString(block, "text");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var level = block.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number
            ? l.GetInt32()
            : 2;
        return (text.Trim(), Math.Clamp(level, 1, 6));
    }

    /// <summary>
    /// One run for the block's text, carrying an href when the block's sole anchor labels the whole
    /// text. Anchors are offsets-free, so a partial anchor cannot be placed without guessing where
    /// it belongs — and guessing at structure already in the data is the failure this mapper exists
    /// to avoid.
    /// </summary>
    private static IReadOnlyList<Run> BuildRuns(string text, JsonElement block)
    {
        var trimmed = text.Trim();
        if (!block.TryGetProperty("anchors", out var anchors) || anchors.ValueKind != JsonValueKind.Array)
        {
            return [new Run(trimmed)];
        }

        string? href = null;
        var count = 0;
        foreach (var anchor in anchors.EnumerateArray())
        {
            count++;
            if (count > 1)
            {
                return [new Run(trimmed)];
            }

            var label = ReadString(anchor, "label")?.Trim();
            var candidate = ReadString(anchor, "href");
            if (!string.IsNullOrWhiteSpace(candidate)
                && !string.IsNullOrWhiteSpace(label)
                && string.Equals(label, trimmed, StringComparison.Ordinal))
            {
                href = candidate;
            }
        }

        return [new Run(trimmed, Href: href)];
    }

    private static IReadOnlyList<string> ReadCells(JsonElement block)
    {
        if (!block.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var cell in cells.EnumerateArray())
        {
            var value = cell.ValueKind == JsonValueKind.String
                ? cell.GetString()
                : ReadString(cell, "text");
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }
        return values;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.True;
}
