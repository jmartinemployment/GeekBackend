using System.Text;
using System.Text.Json;
using GeekApplication.Models.ContentCreator;
using GeekAPI.HttpClients;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.Export;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Exports a create's generated artifacts as standalone files, foldered by content type, with the
/// image prompts lifted out into their own parallel tree.
///
/// Two export services already existed and neither could see this output: the v1 one reads
/// GeneratedContent rows on a Workflow project, and GccV2HtmlExportService reads GccV2 jobs. Create
/// writes GccArtifact + GccArtifactVersion, so exporting a create through either produced an empty
/// archive. The folder scheme here is v1's, which had it right -- content separated by type, and
/// image prompts never mixed in with the prose they belong to (Jeff, 2026-09-23: "Mixed together
/// would be difficult for me to process").
/// </summary>
public sealed class GccArtifactExportService(HttpGccRepository repo, ILogger<GccArtifactExportService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ExportedHtmlDocument>> ExportAsync(Guid createId, CancellationToken ct)
    {
        var create = await repo.GetCreateAsync(createId, ct)
            ?? throw new InvalidOperationException($"Create {createId} was not found.");

        var artifacts = await repo.ListArtifactsAsync(createId, ct);
        var documents = new List<ExportedHtmlDocument>();

        foreach (var artifact in artifacts.OrderBy(a => a.Type).ThenBy(a => a.CreatedAtUtc))
        {
            var versions = await repo.ListVersionsAsync(artifact.Id, ct);
            var latest = versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (latest is null) continue;

            var parsed = Parse(latest.BodyDocumentJson);
            if (parsed.Document is null || IsNotADocument(parsed.Document))
            {
                // A body that will not parse is exported verbatim rather than dropped -- losing an
                // artifact silently is worse than exporting something the operator has to look at.
                logger.LogWarning(
                    "Artifact {ArtifactId} ({Type}) could not be read as a document; exporting raw.",
                    artifact.Id, artifact.Type);
                documents.Add(new ExportedHtmlDocument(
                    $"{FolderFor(artifact.Type)}/{Slug(artifact.Name, artifact.Id)}.json",
                    latest.BodyDocumentJson));
                continue;
            }

            var slug = Slug(parsed.Title ?? artifact.Name ?? create.Topic, artifact.Id);
            documents.Add(new ExportedHtmlDocument(
                $"{FolderFor(artifact.Type)}/{slug}.html",
                SectionHtmlRenderer.RenderDocument(
                    title: parsed.Title ?? artifact.Name ?? create.Topic,
                    description: parsed.MetaDescription,
                    canonicalUrl: null,
                    ogType: "article",
                    ogImage: null,
                    jsonLdSchema: parsed.JsonLdSchema,
                    additionalMeta: new Dictionary<string, string?>(),
                    body: parsed.Document)));

            documents.AddRange(ImagePromptFiles(artifact.Type, slug, parsed.Document));
        }

        return documents;
    }

    /// <summary>
    /// Whether a deserialized body is actually a document.
    ///
    /// <para>
    /// System.Text.Json does not enforce a record's non-nullable parameters, so an artifact that is
    /// not a ContentDocument at all -- an image-prompt pack, a metadata artifact, a body stored as a
    /// string -- deserializes into a ContentDocument with a null Lede and no sections. It passes a
    /// null check on the document itself and then throws inside the renderer, which is the 500 the
    /// export button returned (Jeff, 2026-09-23).
    /// </para>
    ///
    /// <para>
    /// Treated as unparseable, so it exports raw rather than failing the whole archive: one
    /// artifact the operator has to look at beats no export at all.
    /// </para>
    /// </summary>
    private static bool IsNotADocument(ContentDocument document) =>
        document.Lede is null && (document.Sections is null || document.Sections.Count == 0);

    /// <summary>
    /// One file per image prompt, under image-prompts/&lt;type&gt;/, numbered by position with the
    /// heading it belongs to in the body. They are deliberately not left inside the page: a prompt
    /// is something the operator takes to an image generator, not something they read in the prose.
    /// </summary>
    private static IEnumerable<ExportedHtmlDocument> ImagePromptFiles(
        string contentType, string slug, ContentDocument document)
    {
        var folder = $"image-prompts/{FolderFor(contentType)}";

        if (document.Lede is { ImagePrompt: { } ledePrompt } && !string.IsNullOrWhiteSpace(ledePrompt))
        {
            yield return new ExportedHtmlDocument(
                $"{folder}/{slug}-00-hero.txt",
                $"{document.Lede.Heading}{Environment.NewLine}{Environment.NewLine}{ledePrompt}");
        }

        var index = 1;
        foreach (var section in document.Sections ?? [])
        {
            if (!string.IsNullOrWhiteSpace(section.ImagePrompt))
            {
                yield return new ExportedHtmlDocument(
                    $"{folder}/{slug}-{index:D2}-{Slug(section.Heading, Guid.Empty)}.txt",
                    $"{section.Heading}{Environment.NewLine}{Environment.NewLine}{section.ImagePrompt}");
            }
            index++;
        }
    }

    /// <summary>v1's folder scheme, by content type.</summary>
    private static string FolderFor(string? contentType) =>
        (contentType ?? "").Trim().ToLowerInvariant() switch
        {
            "pillar" => "use-cases",
            "blog" => "blog",
            "tool" or "aitool" => "tools",
            "email" or "email-cold-outreach" => "email",
            "linkedin" => "social/linkedin",
            "facebook" => "social/facebook",
            "x" or "instagram" or "social" => "social",
            "ads" or "googleads" or "metaads" => "ads",
            "image-prompt" or "imageprompt" => "image-prompts/standalone",
            _ => "misc",
        };

    private static string Slug(string? value, Guid fallback)
    {
        var text = (value ?? "").Trim().ToLowerInvariant();
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 80) slug = slug[..80].TrimEnd('-');
        return slug.Length > 0 ? slug : fallback.ToString("N")[..8];
    }

    /// <summary>
    /// Accepts both shapes a generator returns: the envelope every long-form type produces now, and
    /// a bare ContentDocument, which is what they returned before tonight and what older artifacts
    /// still hold.
    /// </summary>
    private static (ContentDocument? Document, string? Title, string? MetaDescription, string? JsonLdSchema)
        Parse(string bodyJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(bodyJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object)
            {
                return (
                    body.Deserialize<ContentDocument>(Json),
                    Str(root, "title"),
                    Str(root, "metaDescription"),
                    Str(root, "jsonLdSchema"));
            }

            return (root.Deserialize<ContentDocument>(Json), null, null, null);
        }
        catch (JsonException)
        {
            return (null, null, null, null);
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
