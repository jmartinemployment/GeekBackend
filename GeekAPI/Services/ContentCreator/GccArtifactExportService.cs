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
/// An export service already existed and could not see this output: it reads GeneratedContent rows
/// on a Workflow project. Create writes GccArtifact + GccArtifactVersion, so exporting a create
/// through it produced an empty archive. The folder scheme here is that one's, which had it right
/// -- content separated by type, and
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
    ///
    /// A piece written from a page by Repurpose carries its page's name, and a pillar and a blog are
    /// both named for the keyword, so the file of a piece also says which page it came from
    /// (<see cref="FromPage"/>): "email/accounts-payable-from-pillar.json". Numbered, the pillar's
    /// email and the blog's would be two files with nothing saying which was which.
    /// </remarks>
    public async Task<IReadOnlyList<ExportedHtmlDocument>> ExportProjectAsync(Guid projectId, CancellationToken ct)
    {
        var artifacts = await repo.ListProjectArtifactsAsync(projectId, ct);
        var documents = new List<ExportedHtmlDocument>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onProject = artifacts.ToDictionary(a => a.Id);

        foreach (var artifact in artifacts.OrderByDescending(a => a.CreatedAtUtc))
        {
            var create = await repo.GetCreateAsync(artifact.CreateId, ct)
                ?? throw new InvalidOperationException($"Create {artifact.CreateId} was not found.");
            foreach (var document in await ExportArtifactsAsync(create, [artifact], ct, onProject))
            {
                documents.Add(document with { FileName = Unused(document.FileName, taken) });
            }
        }

        return documents;
    }

    /// <summary>
    /// One page -- the pillar, a tool page, the blog -- as the single HTML file the project zip would
    /// hold for it, from its newest version. Null when the artifact does not exist or has no version.
    /// A deck's one file is its PDF.
    /// </summary>
    /// <remarks>
    /// Jeff, 2026-10-09: "Why can't I export just Pillar from this run?" The only export was the whole
    /// project as a zip. The image prompts stay with the zip; this is the page.
    /// </remarks>
    public async Task<ExportedHtmlDocument?> ExportArtifactAsync(Guid artifactId, CancellationToken ct)
    {
        var artifact = await repo.GetArtifactAsync(artifactId, ct);
        if (artifact is null) return null;
        var create = await repo.GetCreateAsync(artifact.CreateId, ct)
            ?? throw new InvalidOperationException($"Create {artifact.CreateId} was not found.");

        var documents = await ExportArtifactsAsync(create, [artifact], ct);
        return documents.FirstOrDefault(d => !d.FileName.StartsWith("image-prompts/", StringComparison.Ordinal));
    }

    /// <summary>What asking for a version's PDF came to: exactly one of the three is set.</summary>
    /// <param name="NotFound">True when there is no such version, or its page is gone.</param>
    /// <param name="Refusal">Why this version has no PDF: it is not a deck, or was not saved as one.</param>
    /// <param name="FileName">The PDF's file name, when it was made.</param>
    /// <param name="Pdf">The PDF.</param>
    public sealed record DeckPdf(bool NotFound = false, string? Refusal = null, string? FileName = null, byte[]? Pdf = null);

    /// <summary>
    /// A "PDF / LinkedIn document" as the PDF that is posted, made now from the saved deck. No bytes
    /// are stored: stored bytes could differ from the stored words.
    /// </summary>
    public async Task<DeckPdf> DeckPdfAsync(Guid versionId, CancellationToken ct)
    {
        var version = await repo.GetVersionAsync(versionId, ct);
        if (version is null) return new DeckPdf(NotFound: true);
        var artifact = await repo.GetArtifactAsync(version.ArtifactId, ct);
        if (artifact is null) return new DeckPdf(NotFound: true);

        if (!GccLinkedInDocument.IsType(artifact.Type))
        {
            return new DeckPdf(Refusal:
                $"Only a {GccLinkedInDocument.Label} has a PDF, and this page is not one: it is saved as \"{artifact.Type}\".");
        }

        if (GccLinkedInDocument.ReadSaved(version.BodyDocumentJson, Json) is not { } deck)
        {
            return new DeckPdf(Refusal:
                $"This {GccLinkedInDocument.Label} is not saved as a deck of slides, so no PDF can be made from it. "
                + "Write it again with Generate.");
        }

        return new DeckPdf(
            FileName: $"{Slug(deck.Title, artifact.Id)}.pdf",
            Pdf: GccLinkedInDocumentPdfRenderer.Render(deck, _company));
    }

    /// <summary>
    /// A deck's files: the PDF that is posted, the post it is attached to, and the links to paste as
    /// the first comment under it. No HTML: a deck is not a page of the site.
    /// </summary>
    /// <remarks>
    /// A deck whose PDF cannot be made is not left out of the archive without a word and does not
    /// cost the archive its other pages: a text file in the PDF's place says why, beside the post and
    /// the comment.
    /// </remarks>
    private List<ExportedHtmlDocument> DeckFiles(GccArtifactDto artifact, GccArtifactVersionDto latest)
    {
        var folder = FolderFor(artifact.Type);
        if (GccLinkedInDocument.ReadSaved(latest.BodyDocumentJson, Json) is not { } deck)
        {
            // As for any page whose body cannot be read: exported as it is saved, not dropped.
            logger.LogWarning(
                "Artifact {ArtifactId} ({Type}) could not be read as a deck; exporting raw.",
                artifact.Id, artifact.Type);
            return [new ExportedHtmlDocument($"{folder}/{Slug(artifact.Name, artifact.Id)}.json", latest.BodyDocumentJson)];
        }

        var slug = Slug(deck.Title, artifact.Id);
        var files = new List<ExportedHtmlDocument>();
        try
        {
            files.Add(new ExportedHtmlDocument(
                $"{folder}/{slug}.pdf", BinaryContent: GccLinkedInDocumentPdfRenderer.Render(deck, _company)));
        }
        catch (Exception fault) when (fault is not OperationCanceledException)
        {
            logger.LogError(fault, "The PDF of artifact {ArtifactId} could not be made for the export.", artifact.Id);
            files.Add(new ExportedHtmlDocument(
                $"{folder}/{slug}.pdf-was-not-made.txt",
                $"The PDF of this deck could not be made: {fault.GetBaseException().Message}"));
        }

        files.Add(new ExportedHtmlDocument($"{folder}/{slug}.caption.txt", deck.Post));
        if (!string.IsNullOrWhiteSpace(deck.FirstComment))
        {
            files.Add(new ExportedHtmlDocument($"{folder}/{slug}.first-comment.txt", deck.FirstComment));
        }

        return files;
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

    /// <param name="known">Pages already read, by id, so the page a piece was written from is not read again.</param>
    private async Task<IReadOnlyList<ExportedHtmlDocument>> ExportArtifactsAsync(
        GccCreateDto create, IReadOnlyList<GccArtifactDto> artifacts, CancellationToken ct,
        IReadOnlyDictionary<Guid, GccArtifactDto>? known = null)
    {
        var documents = new List<ExportedHtmlDocument>();

        foreach (var artifact in artifacts.OrderBy(a => a.Type).ThenBy(a => a.CreatedAtUtc))
        {
            var versions = await repo.ListVersionsAsync(artifact.Id, ct);
            var latest = versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (latest is null) continue;

            if (GccLinkedInDocument.IsType(artifact.Type))
            {
                documents.AddRange(DeckFiles(artifact, latest));
                continue;
            }

            var parsed = GccBodyEnvelope.Read(latest.BodyDocumentJson, Json);
            if (parsed.Document is null)
            {
                // A body that will not parse is exported verbatim rather than dropped -- losing an
                // artifact silently is worse than exporting something the operator has to look at.
                logger.LogWarning(
                    "Artifact {ArtifactId} ({Type}) could not be read as a document; exporting raw.",
                    artifact.Id, artifact.Type);
                var fromPage = FromPage(await PageOfAsync(artifact, artifacts, known, ct));
                documents.Add(new ExportedHtmlDocument(
                    $"{FolderFor(artifact.Type)}/{Slug(artifact.Name, artifact.Id)}{fromPage}.json",
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
                    yahooVerification: _company.YahooVerification,
                    summary: parsed.Summary)));

            documents.AddRange(ImagePromptFiles(slug, parsed.Document));
        }

        return documents;
    }

    /// <summary>
    /// The page a piece was written from, or null for a page of the project's own: from the pages
    /// already in hand, else read from the repository.
    /// </summary>
    private async Task<GccArtifactDto?> PageOfAsync(
        GccArtifactDto artifact,
        IReadOnlyList<GccArtifactDto> beside,
        IReadOnlyDictionary<Guid, GccArtifactDto>? known,
        CancellationToken ct)
    {
        if (artifact.ParentArtifactId is not Guid pageId) return null;
        if (known is not null && known.TryGetValue(pageId, out var page)) return page;
        return beside.FirstOrDefault(a => a.Id == pageId) ?? await repo.GetArtifactAsync(pageId, ct);
    }

    /// <summary>
    /// What a piece's file name ends with to say which page it was written from: "-from-pillar",
    /// "-from-blog", "-from-tool". Empty for a page of the project's own.
    /// </summary>
    internal static string FromPage(GccArtifactDto? page) =>
        page is null
            ? string.Empty
            : $"-from-{(GccGenerationCoordinator.WritesToolPages(page.Type) ? "tool" : Slug(page.Type, page.Id))}";

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

    /// <summary>v1's folder scheme, by content type, and a folder of its own for a deck.</summary>
    private static string FolderFor(string? contentType) =>
        GccLinkedInDocument.IsType(contentType)
            ? "linkedin-document"
            : (contentType ?? "").Trim().ToLowerInvariant() switch
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
