using System.Text.Json;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// JSON evidence as the text that is in it: every string value on a line of its own, decoded, and
/// every number as written.
/// </summary>
/// <remarks>
/// <para>
/// The partner extraction and the brief are handed to the writer as JSON, and were handed to the
/// figure and currency checks as that same serialized string. Two things were wrong with reading it
/// raw. It is one line, and <see cref="GccCurrencyGrammar"/> gives a bare <c>$</c> the currency its
/// line names -- so one mention of AUD anywhere in a partner's extraction made every dollar amount in
/// it Australian. And the serializer escapes what is not ASCII, so <c>£12</c> is stored as
/// <c>£12</c> and was not read as pounds at all.
/// </para>
/// <para>
/// Property names are left out: they are the schema's words, not the partner's, and a name with a
/// digit in it licenses no figure.
/// </para>
/// </remarks>
public static class GccJsonEvidence
{
    public static string TextOf(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Not JSON after all. It is still text the writer was shown, so it is read as it stands.
            return json;
        }

        using (document)
        {
            var lines = new List<string>();
            Collect(document.RootElement, lines);
            return string.Join('\n', lines);
        }
    }

    private static void Collect(JsonElement element, List<string> lines)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) Collect(property.Value, lines);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, lines);
                break;
            case JsonValueKind.String:
                if (element.GetString() is { } text && !string.IsNullOrWhiteSpace(text)) lines.Add(text);
                break;
            case JsonValueKind.Number:
                lines.Add(element.GetRawText());
                break;
        }
    }
}
