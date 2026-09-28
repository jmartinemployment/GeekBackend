using System.Text.Json;

namespace GeekAPI.Services.Gcw;

/// <summary>
/// Reading a stored body as text, for the analysers that score it.
///
/// <para>
/// The SEO and polish reports came back 0 words, 0 sections on every long-form draft. Three reasons,
/// any one of them enough on its own, and all three the same cause -- the walkers were written
/// against a body shape that has since changed and were never revisited:
/// </para>
///
/// <list type="number">
/// <item>Long-form types return an envelope -- <c>{ title, metaDescription, summary, body, jsonLdSchema }</c>
/// -- and the walkers read <c>lede</c> and <c>sections</c> off the root, which on an envelope holds
/// neither.</item>
/// <item>The lede was read as a string. <c>ContentDocument.Lede</c> is a <c>Section</c>, so even a
/// bare document's opening was skipped.</item>
/// <item>Paragraphs were matched on <c>$type</c>. <c>ContentSectionJsonConverter</c> writes
/// <c>type</c>, so no paragraph text was ever collected.</item>
/// </list>
///
/// <para>
/// One definition, because there were already two hand-rolled copies of this walk and a third in
/// the frontend. <c>GccArtifactExportService</c> unwraps the same envelope for its own reasons.
/// </para>
/// </summary>
public static class GcwBodyDocument
{
    /// <summary>Everything a stored body says, as text.</summary>
    public sealed record Text(string Lede, IReadOnlyList<string> Headings, string PlainText, int SectionCount);

    /// <summary>
    /// Empty when the body will not parse -- never the raw JSON. Returning the JSON as prose is how
    /// a broken body scored as a long draft full of braces.
    /// </summary>
    public static Text Read(string? bodyDocumentJson)
    {
        if (string.IsNullOrWhiteSpace(bodyDocumentJson)) return new Text("", [], "", 0);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bodyDocumentJson);
        }
        catch (JsonException)
        {
            return new Text("", [], "", 0);
        }

        using (doc)
        {
            var root = doc.RootElement;
            // The envelope every long-form type returns, or a bare document from an older artifact.
            var document = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("body", out var body)
                && body.ValueKind == JsonValueKind.Object
                    ? body
                    : root;

            var parts = new List<string>();
            var headings = new List<string>();
            var lede = "";
            var sectionCount = 0;

            if (document.ValueKind == JsonValueKind.Object
                && document.TryGetProperty("lede", out var ledeEl))
            {
                // A Section, not a string. Its own heading is deliberately left out of `headings`:
                // the lede has no heading of its own on the page, so counting it as one would let a
                // draft pass "keyword in a heading" on text no reader sees as a heading.
                var ledeParts = new List<string>();
                if (ledeEl.ValueKind == JsonValueKind.String)
                {
                    ledeParts.Add(ledeEl.GetString() ?? "");
                }
                else if (ledeEl.ValueKind == JsonValueKind.Object)
                {
                    CollectParagraphs(ledeEl, ledeParts);
                }

                lede = string.Join(" ", ledeParts.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
                if (lede.Length > 0) parts.Add(lede);
            }

            if (document.ValueKind == JsonValueKind.Object
                && document.TryGetProperty("sections", out var sections)
                && sections.ValueKind == JsonValueKind.Array)
            {
                foreach (var section in sections.EnumerateArray())
                {
                    sectionCount++;
                    CollectSection(section, parts, headings);
                }
            }

            return new Text(lede, headings, string.Join("\n", parts), sectionCount);
        }
    }

    private static void CollectSection(JsonElement section, List<string> parts, List<string> headings)
    {
        if (section.TryGetProperty("heading", out var heading) && heading.ValueKind == JsonValueKind.String)
        {
            var h = heading.GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(h))
            {
                headings.Add(h);
                parts.Add(h);
            }
        }

        CollectParagraphs(section, parts);

        if (section.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                CollectSection(child, parts, headings);
        }
    }

    private static void CollectParagraphs(JsonElement section, List<string> parts)
    {
        if (!section.TryGetProperty("paragraphs", out var paragraphs)
            || paragraphs.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var paragraph in paragraphs.EnumerateArray())
            CollectParagraph(paragraph, parts);
    }

    /// <summary>
    /// Every paragraph kind the converter writes. A quote counts as words on the page, because it
    /// is words on the page -- a draft is not shorter for having quoted someone.
    /// </summary>
    private static void CollectParagraph(JsonElement paragraph, List<string> parts)
    {
        var kind = paragraph.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;

        switch (kind)
        {
            case "list":
                if (paragraph.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Array) continue;
                        foreach (var run in item.EnumerateArray()) CollectRun(run, parts);
                    }
                }
                break;

            // "text", "quote", and an older paragraph stored with no type at all: all runs.
            default:
                if (paragraph.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var run in runs.EnumerateArray()) CollectRun(run, parts);
                }
                break;
        }
    }

    private static void CollectRun(JsonElement run, List<string> parts)
    {
        if (run.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            var value = text.GetString();
            if (!string.IsNullOrWhiteSpace(value)) parts.Add(value);
        }
    }
}
