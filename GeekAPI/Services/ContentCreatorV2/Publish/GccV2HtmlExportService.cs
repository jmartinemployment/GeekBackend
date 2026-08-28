using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.Export;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.ContentCreatorV2.Publish;

public sealed class GccV2HtmlExportService
{
    private static readonly JsonSerializerOptions ResultJsonOpts = CreateResultJsonOpts();

    private static JsonSerializerOptions CreateResultJsonOpts()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    private readonly HttpGccV2Repository _repo;
    private readonly CompanyProfileOptions _company;

    public GccV2HtmlExportService(HttpGccV2Repository repo, IOptions<CompanyProfileOptions> company)
    {
        _repo = repo;
        _company = company.Value;
    }

    public async Task<IReadOnlyList<ExportedHtmlDocument>> ExportCreateAsync(Guid createId, CancellationToken ct)
    {
        var create = await _repo.GetCreateAsync(createId, ct)
            ?? throw new ContentGenerationException($"Create {createId} was not found.");

        var jobs = await _repo.ListJobsByCreateAsync(createId, ct);
        var documents = new List<ExportedHtmlDocument>();

        foreach (var job in jobs.OrderBy(j => j.ContentType).ThenBy(j => j.CreatedAtUtc))
        {
            if (string.IsNullOrWhiteSpace(job.ResultJson)) continue;
            JobResultPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<JobResultPayload>(job.ResultJson, ResultJsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }

            if (payload?.Document is not { } document) continue;

            var contentType = string.IsNullOrWhiteSpace(job.ContentType) ? "blog" : job.ContentType.Trim().ToLowerInvariant();
            var title = string.IsNullOrWhiteSpace(payload.Title) ? create.Title : payload.Title!;
            var slug = SlugHelper.Slugify(title);
            var folder = FolderFor(contentType);
            var metaDescription = payload.MetaDescription;

            if (contentType == "image-prompt")
            {
                var text = PlainTextOf(document);
                documents.Add(new ExportedHtmlDocument($"{folder}/{slug}.txt", text));
                continue;
            }

            if (contentType is "email" or "social" or "ads")
            {
                var text = PlainTextOf(document);
                documents.Add(new ExportedHtmlDocument($"{folder}/{slug}.txt", text));
                continue;
            }

            var canonicalUrl = CanonicalUrlFor(contentType, slug);
            var html = SectionHtmlRenderer.RenderDocument(
                title,
                metaDescription,
                canonicalUrl,
                contentType is "blog" or "pillar" ? "article" : "website",
                _company.PublisherLogoUrl,
                jsonLdSchema: null,
                additionalMeta: new Dictionary<string, string?>
                {
                    ["slug"] = slug,
                    ["department"] = "marketing",
                    ["keywords"] = create.Title,
                },
                body: document,
                gtmContainerId: _company.GtmContainerId,
                siteName: _company.PublisherName,
                authorName: _company.AuthorName,
                faviconUrl: _company.FaviconUrl,
                googleSiteVerification: _company.GoogleSiteVerification,
                yandexVerification: _company.YandexVerification,
                yahooVerification: _company.YahooVerification);

            documents.Add(new ExportedHtmlDocument($"{folder}/{slug}.html", html));
        }

        if (documents.Count == 0)
            throw new ContentGenerationException("Nothing to export — no completed job documents on this create.");

        return documents;
    }

    private static string PlainTextOf(ContentDocument document)
    {
        var parts = new List<string> { document.Lede.Heading };
        foreach (var p in document.Lede.Paragraphs)
            parts.AddRange(FlattenParagraph(p));
        foreach (var section in document.Sections)
            parts.AddRange(FlattenSection(section));
        return string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static IEnumerable<string> FlattenSection(Section section)
    {
        yield return section.Heading;
        foreach (var p in section.Paragraphs)
            foreach (var t in FlattenParagraph(p))
                yield return t;
        foreach (var child in section.Children)
            foreach (var t in FlattenSection(child))
                yield return t;
    }

    private static IEnumerable<string> FlattenParagraph(Paragraph paragraph) => paragraph switch
    {
        TextParagraph text => [string.Join("", text.Runs.Select(r => r.Text))],
        ListParagraph list => list.Items.Select(item => string.Join("", item.Select(r => r.Text))),
        _ => [],
    };

    private string? CanonicalUrlFor(string contentType, string slug) => contentType switch
    {
        "pillar" => CombineUrl(_company.ArticleBaseUrl, "marketing", slug),
        "blog" => CombineUrl(_company.BlogBaseUrl, "marketing", slug),
        "tool" => CombineUrl(_company.ToolBaseUrl, "marketing", slug),
        _ => null,
    };

    private static string CombineUrl(string baseUrl, string department, string slug) =>
        $"{baseUrl.TrimEnd('/')}/{department}/{slug}";

    private static string FolderFor(string contentType) => contentType switch
    {
        "pillar" => "use-cases",
        "blog" => "blog",
        "tool" => "tools",
        "email" => "email",
        "social" => "social/linkedin",
        "ads" => "ads",
        "image-prompt" => "image-prompts/sections",
        _ => "misc",
    };

    private sealed record JobResultPayload(string? Title, string? MetaDescription, ContentDocument? Document);
}
