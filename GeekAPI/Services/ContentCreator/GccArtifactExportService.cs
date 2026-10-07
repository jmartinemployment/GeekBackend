using System.Text;
using System.Text.Json;
using GeekApplication.Models.ContentCreator;
using GeekAPI.HttpClients;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.Export;
using Microsoft.Extensions.Options;

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
public sealed class GccArtifactExportService(
    HttpGccRepository repo,
    IOptions<CompanyProfileOptions> companyProfile,
    ILogger<GccArtifactExportService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly CompanyProfileOptions _company = companyProfile.Value;

    public async Task<IReadOnlyList<ExportedHtmlDocument>> ExportAsync(Guid createId, CancellationToken ct)
    {
        var create = await repo.GetCreateAsync(createId, ct)
            ?? throw new InvalidOperationException($"Create {createId} was not found.");

        return await ExportArtifactsAsync(create, await repo.ListArtifactsAsync(createId, ct), ct);
    }

    /// <summary>
    /// Every draft on a project, as the same files a create's export gives. The project is what the
    /// operator sees; which create a draft is stored under is not their concern.
    /// </summary>
    /// <remarks>
    /// One file per page, from its one version. A project has one page per type and name, and a
    /// Generate replaces its content, so there is no second draft of a page to export -- until
    /// 2026-10-05 each Generate added one, and the export carried the older text as "-2" beside the
    /// newer with nothing saying which run either came from.
    ///
    /// Two different pages can still reduce to one file name, because a file is named for the title
    /// its page was given. A zip cannot hold two files of one name and keeping one would drop a page
    /// without a word, so the second is numbered.
    /// </remarks>
    public async Task<IReadOnlyList<ExportedHtmlDocument>> ExportProjectAsync(Guid projectId, CancellationToken ct)
    {
        var artifacts = await repo.ListProjectArtifactsAsync(projectId, ct);
        var documents = new List<ExportedHtmlDocument>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in artifacts.OrderByDescending(a => a.CreatedAtUtc))
        {
            var create = await repo.GetCreateAsync(artifact.CreateId, ct)
                ?? throw new InvalidOperationException($"Create {artifact.CreateId} was not found.");
            foreach (var document in await ExportArtifactsAsync(create, [artifact], ct))
            {
                documents.Add(document with { FileName = Unused(document.FileName, taken) });
            }
        }

        return documents;
    }

    /// <summary><paramref name="fileName"/>, or it with "-2", "-3"... before the extension when taken.</summary>
    private static string Unused(string fileName, HashSet<string> taken)
    {
        if (taken.Add(fileName)) return fileName;

        var dot = fileName.LastIndexOf('.');
        var (stem, extension) = dot < 0 ? (fileName, string.Empty) : (fileName[..dot], fileName[dot..]);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem}-{n}{extension}";
            if (taken.Add(candidate)) return candidate;
        }
    }

    private async Task<IReadOnlyList<ExportedHtmlDocument>> ExportArtifactsAsync(
        GccCreateDto create, IReadOnlyList<GccArtifactDto> artifacts, CancellationToken ct)
    {
        var documents = new List<ExportedHtmlDocument>();

        foreach (var artifact in artifacts.OrderBy(a => a.Type).ThenBy(a => a.CreatedAtUtc))
        {
            var versions = await repo.ListVersionsAsync(artifact.Id, ct);
            var latest = versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (latest is null) continue;

            var parsed = GccBodyEnvelope.Read(latest.BodyDocumentJson, Json);
            if (parsed.Document is null)
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

            // The slug, and so the file and the canonical URL, come from the product on a tool page ("ramp"),
            // not from its title ("Ramp: Automated Approval Workflows"): the URL stays /tools/.../ramp.
            var slug = Slug(parsed.ProductName ?? parsed.Title ?? artifact.Name ?? create.Topic, artifact.Id);
            var title = parsed.Title ?? artifact.Name ?? create.Topic;
            var department = string.IsNullOrWhiteSpace(create.Department) ? "marketing" : create.Department.Trim();
            documents.Add(new ExportedHtmlDocument(
                $"{FolderFor(artifact.Type)}/{slug}.html",
                SectionHtmlRenderer.RenderDocument(
                    title: title,
                    description: parsed.MetaDescription,
                    canonicalUrl: CanonicalUrlFor(artifact.Type, create, slug),
                    ogType: OgTypeFor(artifact.Type),
                    ogImage: _company.PublisherLogoUrl,
                    jsonLdSchema: parsed.JsonLdSchema,
                    additionalMeta: MetaFor(parsed, slug, department, latest.CreatedAtUtc),
                    body: parsed.Document,
                    gtmContainerId: _company.GtmContainerId,
                    siteName: _company.PublisherName,
                    authorName: _company.AuthorName,
                    faviconUrl: _company.FaviconUrl,
                    googleSiteVerification: _company.GoogleSiteVerification,
                    yandexVerification: _company.YandexVerification,
                    yahooVerification: _company.YahooVerification)));

            documents.AddRange(ImagePromptFiles(slug, parsed.Document));
        }

        return documents;
    }

    /// <summary>
    /// The canonical URL for this artifact, matching what v1's export puts in the tag and what each
    /// JSON+LD builder puts in its "url" field. A mismatch between the two is the kind of thing
    /// search engines flag, which is why v1 derives both from the same base URL + department + slug.
    /// </summary>
    /// <remarks>
    /// Built by <see cref="GccContentPath"/> rather than here, so the canonical tag and the JSON-LD
    /// <c>url</c> for the same page cannot disagree — they were two hand-assembled strings before.
    /// The create supplies the department and the descriptor directory;
    /// <paramref name="department"/> is no longer read, because <c>GccContentPath.DepartmentFor</c>
    /// derives a better one from the researched taxonomy path than the create's own column, which
    /// nothing sets and which defaults to "marketing" for every live create.
    /// </remarks>
    private string? CanonicalUrlFor(string? contentType, GccCreateDto create, string slug) =>
        (contentType ?? "").Trim().ToLowerInvariant() switch
        {
            "pillar" => GccContentPath.For(_company.ArticleBaseUrl, create, slug),
            "blog" => GccContentPath.For(_company.BlogBaseUrl, create, slug),
            "tool" or "aitool" => GccContentPath.For(_company.ToolBaseUrl, create, slug),
            _ => null,
        };

    /// <summary>v1's mapping: a pillar and a blog are articles, everything else is a website.</summary>
    private static string OgTypeFor(string? contentType) =>
        (contentType ?? "").Trim().ToLowerInvariant() switch
        {
            "pillar" or "blog" => "article",
            _ => "website",
        };

    /// <summary>
    /// The meta block v1 writes into every exported page. The summary variants it carries --
    /// mainSummary, heroSummary, homeSummary, blogSummary, advertisingSummary -- are columns on a v1
    /// GeneratedContent row and have no equivalent on a create artifact, so they are omitted rather
    /// than emitted empty: a meta tag with no value is worse than no tag.
    /// </summary>
    private static Dictionary<string, string?> MetaFor(
        GccBodyEnvelope.Parsed parsed,
        string slug,
        string department,
        DateTime createdAtUtc) =>
        new()
        {
            ["slug"] = slug,
            ["department"] = department,
            ["date"] = createdAtUtc.ToString("O"),
            ["excerpt"] = parsed.MetaDescription,
        };

    /// <summary>
    /// One .txt per image prompt under image-prompts/sections/, named the way v1 names them. A
    /// prompt is something the operator takes to an image generator, never something a reader reads,
    /// which is why it is a separate file and not left in the page (Jeff, 2026-09-23: "Mixed
    /// together would be difficult for me to process").
    ///
    /// <para>
    /// One deliberate departure from v1: the index increments. v1 calls its collector with
    /// <c>sectionIndex: 0</c> for the lede and for every top-level section, so every section's
    /// prompt lands on <c>{slug}-0.txt</c> and they collide inside the archive. Reproducing that
    /// would mean shipping one prompt file per page instead of one per section.
    /// </para>
    /// </summary>
    private static IEnumerable<ExportedHtmlDocument> ImagePromptFiles(string slug, ContentDocument document)
    {
        var index = 0;
        foreach (var file in Collect(document.Lede, slug, ref index))
        {
            yield return file;
        }

        foreach (var section in document.Sections ?? [])
        {
            foreach (var file in Collect(section, slug, ref index))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// Depth-first, so a nested subsection's prompt is numbered where it actually sits in the page.
    /// Not an iterator, because <c>ref</c> cannot cross a <c>yield</c>.
    /// </summary>
    private static List<ExportedHtmlDocument> Collect(Section? section, string slug, ref int index)
    {
        var files = new List<ExportedHtmlDocument>();
        if (section is null) return files;

        if (!string.IsNullOrWhiteSpace(section.ImagePrompt))
        {
            files.Add(new ExportedHtmlDocument($"image-prompts/sections/{slug}-{index}.txt", section.ImagePrompt!));
        }
        index++;

        foreach (var child in section.Children ?? [])
        {
            files.AddRange(Collect(child, slug, ref index));
        }

        return files;
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

}
