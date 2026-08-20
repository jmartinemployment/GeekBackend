using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.ContentCreator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GeekAPI.Services.Workflow.Services;

public interface IToolPageGenerator
{
    Task<IReadOnlyList<GccGenerateService.CrawlTool>> ListCrawlToolsAsync(
        Project project,
        CancellationToken cancellationToken = default);

    Task<ToolGenerationResult> GenerateToolPagesAsync(
        Project project,
        ArticleMetadataDraft metadata,
        ProjectGenerationContext context,
        IContentGenerationProvider provider,
        string pillarArticleUrl,
        string? revisionNotes = null,
        IReadOnlySet<string>? toolSlugsToRegenerate = null,
        Func<GeneratedContent, CancellationToken, Task>? onRowReady = null,
        CancellationToken cancellationToken = default);

    Task<GeneratedContent> GenerateHubAsync(
        Project project,
        ArticleMetadataDraft metadata,
        ProjectGenerationContext context,
        IContentGenerationProvider provider,
        string pillarArticleUrl,
        IReadOnlyList<(string Name, string Slug, string? ResearchJson)> tools,
        CancellationToken cancellationToken = default);
}

public sealed record ToolGenerationResult(
    ToolGenerationOutcome Outcome,
    IReadOnlyList<GeneratedContent> ToolPosts);

public sealed class ToolPageGenerator : IToolPageGenerator
{
    private const int MinBodyWordsToKeep = 20;

    private readonly ISoftwareApplicationSchemaBuilder _softwareApplicationSchemaBuilder;
    private readonly IContentPromptBuilder _promptBuilder;
    private readonly HttpGeekSeoSiteAnalyzerClient _seo;
    private readonly IHttpContextAccessor _httpContext;
    private readonly ILogger<ToolPageGenerator> _logger;

    public ToolPageGenerator(
        ISoftwareApplicationSchemaBuilder softwareApplicationSchemaBuilder,
        IContentPromptBuilder promptBuilder,
        HttpGeekSeoSiteAnalyzerClient seo,
        IHttpContextAccessor httpContext,
        ILogger<ToolPageGenerator> logger)
    {
        _softwareApplicationSchemaBuilder = softwareApplicationSchemaBuilder;
        _promptBuilder = promptBuilder;
        _seo = seo;
        _httpContext = httpContext;
        _logger = logger;
    }

    public async Task<ToolGenerationResult> GenerateToolPagesAsync(
        Project project,
        ArticleMetadataDraft metadata,
        ProjectGenerationContext context,
        IContentGenerationProvider provider,
        string pillarArticleUrl,
        string? revisionNotes = null,
        IReadOnlySet<string>? toolSlugsToRegenerate = null,
        Func<GeneratedContent, CancellationToken, Task>? onRowReady = null,
        CancellationToken cancellationToken = default)
    {
        var toolSlots = await ResolveToolSlotsAsync(project, cancellationToken);
        if (toolSlots.Count == 0)
        {
            return new ToolGenerationResult(ToolGenerationOutcome.ToolsSectionEmpty, []);
        }

        var applications = toolSlots
            .Select(s => new SoftwareApplicationDescriptor(
                s.Name,
                s.Description,
                Url: string.IsNullOrWhiteSpace(s.Href) ? null : s.Href))
            .ToList();

        var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slotted = applications
            .Select((app, index) => (
                App: app,
                ResearchJson: toolSlots[index].ResearchJson,
                Slug: SlugHelper.EnsureUniqueSlug(SlugHelper.Slugify(app.Name), usedSlugs),
                Order: index + 1))
            .ToList();

        var slotsToGenerate = toolSlugsToRegenerate is null or { Count: 0 }
            ? slotted
            : slotted.Where(s => toolSlugsToRegenerate.Contains(s.Slug)).ToList();
        if (slotsToGenerate.Count == 0)
        {
            throw new ContentGenerationException(
                "None of the requested tool slugs match the crawl's tools for this hierarchy.");
        }

        var rows = new List<GeneratedContent>();
        foreach (var slot in slotsToGenerate)
        {
            var existing = FindKeepableTool(project, slot.App.Name, slot.Slug);
            GeneratedContent row;
            if (existing is not null)
            {
                existing.SourceAppOrder = slot.Order;
                row = existing;
                _logger.LogInformation(
                    "Keeping existing tool page '{Name}' ({Words} words) for project {ProjectId}",
                    slot.App.Name,
                    ContentDocumentText.CountWords(existing.Body),
                    project.Id);
            }
            else
            {
                row = await GenerateOneToolAsync(
                    project, metadata, context, provider, pillarArticleUrl,
                    slot.App, slot.ResearchJson, slot.Slug, slot.Order, revisionNotes, cancellationToken);
            }

            rows.Add(row);
            if (onRowReady is not null)
                await onRowReady(row, cancellationToken);
        }

        if (toolSlugsToRegenerate is null or { Count: 0 })
        {
            var hubSlug = HubSlug(context.TargetKeyword);
            var existingHub = FindKeepableHub(project, hubSlug);
            GeneratedContent hub;
            if (existingHub is not null)
            {
                hub = existingHub;
            }
            else
            {
                hub = await GenerateRoundupAsync(
                    project, metadata, context, provider, pillarArticleUrl, slotted, cancellationToken);
            }

            rows.Insert(0, hub);
            if (onRowReady is not null)
                await onRowReady(hub, cancellationToken);
        }

        return new ToolGenerationResult(ToolGenerationOutcome.Success, rows);
    }

    public Task<GeneratedContent> GenerateHubAsync(
        Project project,
        ArticleMetadataDraft metadata,
        ProjectGenerationContext context,
        IContentGenerationProvider provider,
        string pillarArticleUrl,
        IReadOnlyList<(string Name, string Slug, string? ResearchJson)> tools,
        CancellationToken cancellationToken = default)
    {
        var slotted = tools
            .Select((t, i) => (
                App: new SoftwareApplicationDescriptor(t.Name, null),
                ResearchJson: t.ResearchJson,
                Slug: t.Slug,
                Order: i + 1))
            .ToList();
        return GenerateRoundupAsync(
            project, metadata, context, provider, pillarArticleUrl, slotted, cancellationToken);
    }

    private sealed record ToolSlot(string Name, string? Description, string? ResearchJson, string? Href);

    public async Task<IReadOnlyList<GccGenerateService.CrawlTool>> ListCrawlToolsAsync(
        Project project,
        CancellationToken cancellationToken = default)
    {
        if (project.SiteAnalysisProfileId is not Guid profileId || profileId == Guid.Empty)
            return [];

        var keyword = project.TargetKeyword?.Trim() ?? "";
        if (keyword.Length == 0)
            return [];

        var bearer = BearerToken();
        var result = await _seo.FindTreesByKeywordAsync(profileId, keyword, bearer, cancellationToken);
        if (!result.Ok)
        {
            throw new ContentGenerationException(
                result.Error ?? "Could not load crawl trees for this site analysis.");
        }

        return GccGenerateService.ExtractToolsFromTrees(
                result.Value ?? [],
                keyword,
                project.HierarchySourcePageUrl,
                project.HierarchyPath)
            .ToList();
    }

    private async Task<List<ToolSlot>> ResolveToolSlotsAsync(Project project, CancellationToken cancellationToken)
    {
        return (await ListCrawlToolsAsync(project, cancellationToken))
            .Select(t => new ToolSlot(t.Name, null, ResearchJsonFor(t), t.Href))
            .ToList();
    }

    private static string? ResearchJsonFor(GccGenerateService.CrawlTool tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Href) && string.IsNullOrWhiteSpace(tool.Name))
            return null;
        return JsonSerializer.Serialize(new { name = tool.Name, href = tool.Href });
    }

    private static GeneratedContent? FindKeepableTool(Project project, string name, string slug)
    {
        var row = project.GeneratedContents.FirstOrDefault(c =>
            c.ContentType == GeneratedContentType.ToolPost
            && (string.Equals(c.SourceAppName, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Title, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase))
            && (c.SourceAppOrder is null or > 0));
        if (row?.Body is null) return null;
        return ContentDocumentText.CountWords(row.Body) >= MinBodyWordsToKeep ? row : null;
    }

    private static GeneratedContent? FindKeepableHub(Project project, string hubSlug)
    {
        var row = project.GeneratedContents.FirstOrDefault(c =>
            c.ContentType == GeneratedContentType.ToolPost
            && (c.SourceAppOrder == 0
                || string.Equals(c.Slug, hubSlug, StringComparison.OrdinalIgnoreCase)
                || (c.Title?.StartsWith("Top AI Tools", StringComparison.OrdinalIgnoreCase) ?? false)));
        if (row?.Body is null) return null;
        return ContentDocumentText.CountWords(row.Body) >= MinBodyWordsToKeep ? row : null;
    }

    private static string HubSlug(string topic)
    {
        var slug = SlugHelper.Slugify($"top-ai-tools-for-{topic.Trim()}");
        if (string.IsNullOrWhiteSpace(slug) || slug == "top-ai-tools-for")
            return "top-ai-tools-roundup";
        return slug;
    }

    private string? BearerToken()
    {
        var auth = _httpContext.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(auth)) return null;
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? auth["Bearer ".Length..].Trim()
            : auth.Trim();
    }

    private async Task<GeneratedContent> GenerateOneToolAsync(
        Project project,
        ArticleMetadataDraft metadata,
        ProjectGenerationContext context,
        IContentGenerationProvider provider,
        string pillarArticleUrl,
        SoftwareApplicationDescriptor app,
        string? researchJson,
        string slug,
        int order,
        string? revisionNotes,
        CancellationToken cancellationToken)
    {
        var toolUrl = $"{context.ToolBaseUrl.TrimEnd('/')}/{context.Department}/{slug}";

        var document = await GenerateToolBodyWithValidationAsync(
            provider, context, metadata, app, researchJson, slug, revisionNotes, cancellationToken);

        var toolMetadata = await GenerateToolMetadataAsync(
            provider, context, metadata, app, document, cancellationToken);

        var wordCount = ContentDocumentText.CountWords(document);
        var displayTitle = app.Name.Trim();
        var now = DateTime.UtcNow;
        var schemaMeta = new ContentMetadata(
            displayTitle,
            toolMetadata.MetaDescription,
            context.AuthorName,
            context.PublisherName,
            context.PublisherLogoUrl,
            toolUrl,
            context.PublisherLogoUrl,
            now,
            now,
            metadata.Keywords,
            wordCount);

        var jsonLd = _softwareApplicationSchemaBuilder.BuildToolPage(schemaMeta, pillarArticleUrl, app with { Url = toolUrl });

        return new GeneratedContent
        {
            ProjectId = project.Id,
            ContentType = GeneratedContentType.ToolPost,
            Title = displayTitle,
            DisplayTitle = displayTitle,
            Slug = slug,
            Summary = toolMetadata.Summary,
            MainSummary = toolMetadata.MainSummary,
            HeroSummary = toolMetadata.HeroSummary,
            HomeSummary = toolMetadata.HomeSummary,
            BlogSummary = toolMetadata.BlogSummary,
            DepartmentListExcerpt = toolMetadata.DepartmentListExcerpt,
            ToolPageExcerpt = toolMetadata.ToolPageExcerpt,
            AdvertisingSummary = toolMetadata.AdvertisingSummary,
            MetaDescription = toolMetadata.MetaDescription.Length > 160
                ? toolMetadata.MetaDescription[..160]
                : toolMetadata.MetaDescription,
            Body = document,
            LedeType = GeekAPI.Services.Workflow.Domain.Entities.LedeType.Summary,
            JsonLdSchema = string.IsNullOrWhiteSpace(jsonLd) ? "{}" : jsonLd,
            RelatedArticleUrl = string.IsNullOrWhiteSpace(pillarArticleUrl) ? null : pillarArticleUrl,
            SourceAppName = app.Name,
            SourceAppOrder = order,
            WordCount = wordCount,
            GeneratedByProvider = provider.ProviderType,
            GeneratedByModel = provider.ProviderType.ToString(),
        };
    }

    private async Task<GeneratedContent> GenerateRoundupAsync(
        Project project,
        ArticleMetadataDraft metadata,
        ProjectGenerationContext context,
        IContentGenerationProvider provider,
        string pillarArticleUrl,
        IReadOnlyList<(SoftwareApplicationDescriptor App, string? ResearchJson, string Slug, int Order)> slotted,
        CancellationToken cancellationToken)
    {
        var topic = context.TargetKeyword.Trim();
        var title = string.IsNullOrWhiteSpace(topic)
            ? "Top AI Tools"
            : $"Top AI Tools for {topic}";
        var slug = HubSlug(topic);

        var toolLines = slotted.Select(s =>
        {
            var url = $"{context.ToolBaseUrl.TrimEnd('/')}/{context.Department}/{s.Slug}";
            var research = string.IsNullOrWhiteSpace(s.ResearchJson) ? "" : s.ResearchJson!;
            if (research.Length > 1200) research = research[..1200] + "…";
            return $"- {s.App.Name} → {url}\n  Research: {research}";
        });

        var result = await provider.CompleteAsync(
            _promptBuilder.BuildToolRoundupPrompt(context, metadata, title, string.Join("\n", toolLines)),
            cancellationToken);
        var sections = LlmResponseJsonParser.ParseSections(result.Content, "tool roundup").ToList();
        var lede = sections[0] with { Tag = "h2" };
        var document = new ContentDocument(lede, sections.Skip(1).ToList());
        var wordCount = ContentDocumentText.CountWords(document);

        return new GeneratedContent
        {
            ProjectId = project.Id,
            ContentType = GeneratedContentType.ToolPost,
            Title = title,
            DisplayTitle = title,
            Slug = slug,
            MetaDescription = $"Overview of tools for {topic}".Length > 160
                ? $"Overview of tools for {topic}"[..160]
                : $"Overview of tools for {topic}",
            Body = document,
            LedeType = GeekAPI.Services.Workflow.Domain.Entities.LedeType.Summary,
            JsonLdSchema = "{}",
            RelatedArticleUrl = string.IsNullOrWhiteSpace(pillarArticleUrl) ? null : pillarArticleUrl,
            SourceAppName = title,
            SourceAppOrder = 0,
            WordCount = wordCount,
            GeneratedByProvider = provider.ProviderType,
            GeneratedByModel = provider.ProviderType.ToString(),
            Summary = title,
        };
    }

    private async Task<ToolMetadataDraft> GenerateToolMetadataAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SoftwareApplicationDescriptor app,
        ContentDocument document,
        CancellationToken cancellationToken)
    {
        var result = await provider.CompleteAsync(
            _promptBuilder.BuildToolMetadataPrompt(context, pillarMetadata, app, document),
            cancellationToken);

        return LlmResponseJsonParser.Parse<ToolMetadataDraft>(result.Content, "tool metadata");
    }

    /// <summary>Generates the tool page as a sections array; the first section (always "Overview")
    /// becomes the document's lede, the rest become its top-level sections.</summary>
    private async Task<ContentDocument> GenerateToolBodyWithValidationAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SoftwareApplicationDescriptor app,
        string? researchJson,
        string toolSlug,
        string? revisionNotes,
        CancellationToken cancellationToken)
    {
        var sections = await GenerateFullToolBodyAsync(
            provider, context, pillarMetadata, app, researchJson, toolSlug, revisionNotes, cancellationToken);

        var wordCount = ContentDocumentText.CountWords(sections);

        if (wordCount < ContentLengthTargets.ToolMinWords || wordCount > ContentLengthTargets.ToolHardMaxWords)
        {
            _logger.LogWarning(
                "Tool page for '{App}' is {Count} words (target {Minimum}-{Maximum}) — no expansion/trim pass, single attempt only; saving anyway.",
                app.Name,
                wordCount,
                ContentLengthTargets.ToolMinWords,
                ContentLengthTargets.ToolHardMaxWords);
        }

        var lede = sections[0] with { Tag = "h2" };
        return new ContentDocument(lede, sections.Skip(1).ToList());
    }

    private async Task<List<Section>> GenerateFullToolBodyAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SoftwareApplicationDescriptor app,
        string? researchJson,
        string toolSlug,
        string? revisionNotes,
        CancellationToken cancellationToken)
    {
        var result = await provider.CompleteAsync(
            _promptBuilder.BuildToolBodyPrompt(context, pillarMetadata, app, toolSlug, revisionNotes, researchJson),
            cancellationToken);
        return LlmResponseJsonParser.ParseSections(result.Content, $"tool page '{app.Name}'").ToList();
    }
}
