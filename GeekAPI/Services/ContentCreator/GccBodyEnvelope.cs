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
/// took the export button down (fixed there in September) and then took Revise down the same way:
/// <c>POST versions/{id}/revise</c> → 500, on every long-form draft.
/// </para>
///
/// <para>
/// Writing matters as much as reading. Revise rebuilt a bare document and stored that, so a revised
/// blog came back without its title, meta description, summary or JSON-LD -- the envelope was not
/// just unread, it was discarded. <see cref="Write"/> puts the new body back in the envelope it
/// came out of.
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
    /// The new body in the envelope the old one came from. Fields the original did not carry are
    /// left out rather than written empty -- an empty title is not the same as no title, and a
    /// consumer cannot tell the difference once it is stored.
    /// </summary>
    public static string Write(Parsed original, ContentDocument document, JsonSerializerOptions options)
    {
        var hasEnvelope = original.Title is not null
            || original.ProductName is not null
            || original.MetaDescription is not null
            || original.Summary is not null
            || original.JsonLdSchema is not null;

        if (!hasEnvelope) return JsonSerializer.Serialize(document, options);

        var envelope = new Dictionary<string, object?>();
        if (original.Title is not null) envelope["title"] = original.Title;
        if (original.ProductName is not null) envelope["productName"] = original.ProductName;
        if (original.MetaDescription is not null) envelope["metaDescription"] = original.MetaDescription;
        if (original.Summary is not null) envelope["summary"] = original.Summary;
        envelope["body"] = document;
        if (original.JsonLdSchema is not null) envelope["jsonLdSchema"] = original.JsonLdSchema;
        return JsonSerializer.Serialize(envelope, options);
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
