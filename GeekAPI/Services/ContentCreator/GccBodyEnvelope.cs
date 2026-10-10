using System.Text.Json;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The envelope a stored body is wrapped in, read and written in one place.
///
/// <para>
/// Every long-form generator returns <c>{ title, metaDescription, summary, body, jsonLdSchema }</c>,
/// where only <c>body</c> is the document. Anything that deserializes the stored string straight
/// into a <see cref="ContentDocument"/> gets one back -- System.Text.Json does not enforce a
/// record's non-nullable parameters, so the result is a document with a null <c>Lede</c> and no
/// sections. It survives a null check and then throws on first use.
/// </para>
///
/// <para>
/// That is a 500, not a 400, because the throw is a NullReferenceException nobody is catching. It
/// took the export button down (fixed there in September).
/// </para>
/// </summary>
public static class GccBodyEnvelope
{
    /// <summary>
    /// A stored body, separated. <paramref name="Document"/> is null when the string is not a
    /// document at all -- an image-prompt pack, a metadata artifact, a body stored as a string --
    /// which callers must treat as a refusal rather than carrying on with an empty shell.
    /// </summary>
    public sealed record Parsed(
        ContentDocument? Document,
        string? Title,
        string? MetaDescription,
        string? Summary,
        string? JsonLdSchema,
        // A tool page's product ("Ramp"), beside its title ("Ramp: Automated Approval Workflows"). Null on
        // every other type and on a tool page written before the title carried the keyword, where the title
        // is the product: read as ProductName ?? Title.
        string? ProductName = null);

    public static Parsed Read(string? bodyJson, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(bodyJson)) return new Parsed(null, null, null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(bodyJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new Parsed(null, null, null, null, null);

            if (root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object)
            {
                return new Parsed(
                    Document(body.Deserialize<ContentDocument>(options)),
                    Str(root, "title"),
                    Str(root, "metaDescription"),
                    Str(root, "summary"),
                    Str(root, "jsonLdSchema"),
                    Str(root, "productName"));
            }

            // A bare document: what older artifacts hold, and what the shorter paths still return.
            return new Parsed(Document(root.Deserialize<ContentDocument>(options)), null, null, null, null);
        }
        catch (JsonException)
        {
            return new Parsed(null, null, null, null, null);
        }
    }

    /// <summary>
    /// Null for a deserialization that produced a shell rather than a document. A ContentDocument
    /// with no lede and no sections is not a short document, it is a different thing entirely.
    /// </summary>
    private static ContentDocument? Document(ContentDocument? candidate) =>
        candidate is null || (candidate.Lede is null && (candidate.Sections is null || candidate.Sections.Count == 0))
            ? null
            : candidate;

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
