using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.Workflow.Services;

/// <summary>
/// (De)serializes the <see cref="Paragraph"/> discriminated union to/from the wire shape
/// { "type": "text" | "list", ... } that the JSON-schema-constrained provider response uses.
/// </summary>
public sealed class ParagraphJsonConverter : JsonConverter<Paragraph>
{
    public override Paragraph? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

        return type switch
        {
            // A quote is a first-class block: SectionHtmlRenderer already emits <blockquote
            // cite="...">, and partner extraction already captures a verbatim Quote plus its source
            // on every citable. Without this the model had no quote shape to return, so attribution
            // came out as "According to <partner>" inline prose (Jeff, 2026-09-23).
            "quote" => new QuoteParagraph(
                root.TryGetProperty("runs", out var quoteRuns) && quoteRuns.ValueKind == JsonValueKind.Array
                    ? quoteRuns.EnumerateArray().Select(r => r.Deserialize<Run>(options) ?? new Run(string.Empty)).ToList()
                    : [],
                root.TryGetProperty("cite", out var cite) && cite.ValueKind == JsonValueKind.String
                    ? cite.GetString()
                    : null),
            // Read as well as written because these round-trip: GccCorpusBlockMapper builds both from
            // retrieved corpus blocks, so without a case here a stored code block comes back as an
            // empty TextParagraph -- present in storage, gone from the page.
            "code" => new CodeParagraph(
                root.TryGetProperty("code", out var codeText) && codeText.ValueKind == JsonValueKind.String
                    ? codeText.GetString() ?? string.Empty
                    : string.Empty,
                root.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String
                    ? lang.GetString()
                    : null),
            "definition" => new DefinitionParagraph(
                root.TryGetProperty("items", out var defItems) && defItems.ValueKind == JsonValueKind.Array
                    ? defItems.EnumerateArray()
                        .Select(item => new DefinitionItem(
                            item.TryGetProperty("term", out var term) && term.ValueKind == JsonValueKind.Array
                                ? term.EnumerateArray().Select(r => r.Deserialize<Run>(options) ?? new Run(string.Empty)).ToList()
                                : [],
                            item.TryGetProperty("definition", out var def) && def.ValueKind == JsonValueKind.Array
                                ? def.EnumerateArray().Select(r => r.Deserialize<Run>(options) ?? new Run(string.Empty)).ToList()
                                : []))
                        .ToList()
                    : []),
            "list" => new ListParagraph(
                root.TryGetProperty("ordered", out var o) && o.GetBoolean(),
                root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                    ? items.EnumerateArray()
                        .Select(item => (IReadOnlyList<Run>)(item.ValueKind == JsonValueKind.Array
                            ? item.EnumerateArray().Select(r => r.Deserialize<Run>(options) ?? new Run(string.Empty)).ToList()
                            : []))
                        .ToList()
                    : []),
            _ => new TextParagraph(
                root.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array
                    ? runs.EnumerateArray().Select(r => r.Deserialize<Run>(options) ?? new Run(string.Empty)).ToList()
                    : []),
        };
    }

    public override void Write(Utf8JsonWriter writer, Paragraph value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case TextParagraph text:
                writer.WriteString("type", "text");
                writer.WritePropertyName("runs");
                JsonSerializer.Serialize(writer, text.Runs, options);
                break;
            case ListParagraph list:
                writer.WriteString("type", "list");
                writer.WriteBoolean("ordered", list.Ordered);
                writer.WritePropertyName("items");
                JsonSerializer.Serialize(writer, list.Items, options);
                break;
            // Without this case a QuoteParagraph serialized to "{}" -- the quote, its runs and its
            // citation all silently discarded on the way to storage.
            case QuoteParagraph quote:
                writer.WriteString("type", "quote");
                if (!string.IsNullOrWhiteSpace(quote.Cite)) writer.WriteString("cite", quote.Cite);
                writer.WritePropertyName("runs");
                JsonSerializer.Serialize(writer, quote.Runs, options);
                break;
            // Same silent loss the quote case above records, for the two types a model never returns
            // but GccCorpusBlockMapper does build from retrieved corpus blocks. It mattered less while
            // this converter had to be registered by hand; it is declared on Paragraph itself now, so
            // every serialization of a document anywhere goes through this switch.
            case CodeParagraph code:
                writer.WriteString("type", "code");
                if (!string.IsNullOrWhiteSpace(code.Language)) writer.WriteString("language", code.Language);
                writer.WriteString("code", code.Code);
                break;
            case DefinitionParagraph definitions:
                writer.WriteString("type", "definition");
                writer.WritePropertyName("items");
                writer.WriteStartArray();
                foreach (var item in definitions.Items)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("term");
                    JsonSerializer.Serialize(writer, item.Term, options);
                    writer.WritePropertyName("definition");
                    JsonSerializer.Serialize(writer, item.Definition, options);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                break;
            // No default, for the reason SectionHtmlRenderer.AppendParagraph gives: an unhandled
            // subtype writes "{}" and the content is gone with no error. The checklist is on Paragraph.
        }
        writer.WriteEndObject();
    }
}
