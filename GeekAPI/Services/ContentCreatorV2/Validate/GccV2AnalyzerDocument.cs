using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreatorV2.Validate;

/// <summary>
/// <c>GcwSeoAnalyzer</c> / <c>GcwPolishAnalyzer</c> read a body-document JSON shape that predates
/// the Workflow <see cref="ContentDocument"/> wire format: <c>lede</c> is a flat string (not a
/// Section), and paragraphs are discriminated by <c>"$type"</c> (not Workflow's <c>"type"</c>) —
/// see <c>GcwSeoAnalyzer.ExtractPlainText</c> / <c>GcwPolishAnalyzer.ExtractPlainText</c>. This
/// file is the one place v2 serializes into that exact shape, so both analyzers work unmodified
/// against a WRITE-produced <see cref="ContentDocument"/>.
/// </summary>
public static class GccV2AnalyzerDocument
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static string Serialize(ContentDocument document)
    {
        var analyzerDoc = new AnalyzerDocument(FlattenSectionText(document.Lede), document.Sections.Select(ToAnalyzerSection).ToList());
        return JsonSerializer.Serialize(analyzerDoc, JsonOpts);
    }

    private static AnalyzerSection ToAnalyzerSection(Section section) => new(
        section.Heading,
        section.Paragraphs.Select(ToAnalyzerParagraph).ToList(),
        section.Children.Select(ToAnalyzerSection).ToList());

    private static AnalyzerParagraph ToAnalyzerParagraph(Paragraph paragraph) => paragraph switch
    {
        TextParagraph text => new AnalyzerParagraph("text", text.Runs.Select(r => new AnalyzerRun(r.Text)).ToList(), null, null),
        ListParagraph list => new AnalyzerParagraph(
            "list", null, list.Ordered,
            list.Items.Select(item => (IReadOnlyList<AnalyzerRun>)item.Select(r => new AnalyzerRun(r.Text)).ToList()).ToList()),
        _ => new AnalyzerParagraph("text", [], null, null),
    };

    /// <summary>Heading + all paragraph text, flattened into a single string — matches what the
    /// analyzers' own "lede" field is used for (a keyword-search haystack), not a Section object.</summary>
    private static string FlattenSectionText(Section section)
    {
        var parts = new List<string> { section.Heading };
        foreach (var paragraph in section.Paragraphs)
        {
            parts.AddRange(FlattenParagraphText(paragraph));
        }

        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static IEnumerable<string> FlattenParagraphText(Paragraph paragraph) => paragraph switch
    {
        TextParagraph text => [string.Join(" ", text.Runs.Select(r => r.Text))],
        ListParagraph list => list.Items.Select(item => string.Join(" ", item.Select(r => r.Text))),
        _ => [],
    };

    private sealed record AnalyzerDocument(string Lede, IReadOnlyList<AnalyzerSection> Sections);

    private sealed record AnalyzerSection(string Heading, IReadOnlyList<AnalyzerParagraph> Paragraphs, IReadOnlyList<AnalyzerSection> Children);

    private sealed record AnalyzerParagraph(
        [property: JsonPropertyName("$type")] string Type,
        IReadOnlyList<AnalyzerRun>? Runs,
        bool? Ordered,
        IReadOnlyList<IReadOnlyList<AnalyzerRun>>? Items);

    private sealed record AnalyzerRun(string Text);
}
