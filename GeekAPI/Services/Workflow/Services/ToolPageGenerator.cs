using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GeekAPI.Services.Workflow.Services;

public interface IToolPageGenerator
{
    Task<ToolGenerationResult> GenerateToolPagesAsync(
        Project project,
        GeneratedContent articleRow,
        ArticleMetadataDraft metadata,
        ProjectGenerationContext context,
        IContentGenerationProvider provider,
        string pillarArticleUrl,
        string? revisionNotes = null,
        IReadOnlySet<string>? toolSlugsToRegenerate = null,
        CancellationToken cancellationToken = default);
}

public sealed record ToolGenerationResult(
    ToolGenerationOutcome Outcome,
    IReadOnlyList<GeneratedContent> ToolPosts);

public sealed class ToolPageGenerator : IToolPageGenerator
{
    private const int MaxTools = 5;
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISoftwareApplicationSchemaBuilder _softwareApplicationSchemaBuilder;
    private readonly IContentPromptBuilder _promptBuilder;
    private readonly IToolContentCacheStore _toolContentCacheStore;
    private readonly ILogger<ToolPageGenerator> _logger;

    public ToolPageGenerator(
        ISoftwareApplicationSchemaBuilder softwareApplicationSchemaBuilder,
        IContentPromptBuilder promptBuilder,
        IToolContentCacheStore toolContentCacheStore,
        ILogger<ToolPageGenerator> logger)
    {
        _softwareApplicationSchemaBuilder = softwareApplicationSchemaBuilder;
        _promptBuilder = promptBuilder;
        _toolContentCacheStore = toolContentCacheStore;
        _logger = logger;
    }

    public async Task<ToolGenerationResult> GenerateToolPagesAsync(
        Project project,
        GeneratedContent articleRow,
        ArticleMetadataDraft metadata,
        ProjectGenerationContext context,
        IContentGenerationProvider provider,
        string pillarArticleUrl,
        string? revisionNotes = null,
        IReadOnlySet<string>? toolSlugsToRegenerate = null,
        CancellationToken cancellationToken = default)
    {
        var toolSlots = ResolveToolSlots(project);
        if (toolSlots.Count == 0)
        {
            return new ToolGenerationResult(ToolGenerationOutcome.ToolsSectionEmpty, []);
        }

        var applications = toolSlots
            .Take(MaxTools)
            .Select(s => new SoftwareApplicationDescriptor(
                s.Name,
                s.Description,
                Url: null))
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
                "None of the requested tool slugs match the project's hierarchy/uploaded tool set.");
        }

        var rows = (await Task.WhenAll(slotsToGenerate.Select(slot => GenerateOneToolAsync(
                project, metadata, context, provider, pillarArticleUrl,
                slot.App, slot.ResearchJson, slot.Slug, slot.Order, revisionNotes, cancellationToken))))
            .ToList();

        // Roundup only on full runs (not targeted rewrite of a single tool slug).
        if (toolSlugsToRegenerate is null or { Count: 0 })
        {
            var roundup = await GenerateRoundupAsync(
                project, metadata, context, provider, pillarArticleUrl, slotted, cancellationToken);
            rows.Add(roundup);
        }

        return new ToolGenerationResult(ToolGenerationOutcome.Success, rows);
    }

    private sealed record ToolSlot(string Name, string? Description, string? ResearchJson);

    private static List<ToolSlot> ResolveToolSlots(Project project)
    {
        var hierarchyNames = (project.HierarchyToolsByHeading ?? [])
            .SelectMany(g => g.Tools)
            .Select(t => t.Name.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var uploaded = project.KeywordSources
            .Where(k => k.Category == KeywordSourceCategory.Tools)
            .ToList();

        if (hierarchyNames.Count > 0)
        {
            return hierarchyNames.Select(name =>
            {
                var match = uploaded.FirstOrDefault(u =>
                    string.Equals(u.ExtractedTitle?.Trim(), name, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(u.ExtractedToolResearchJson)
                        && u.ExtractedToolResearchJson.Contains(name, StringComparison.OrdinalIgnoreCase)));
                return new ToolSlot(
                    name,
                    DescriptionFromResearch(match?.ExtractedToolResearchJson),
                    match?.ExtractedToolResearchJson);
            }).ToList();
        }

        return uploaded.Select(u =>
        {
            var name = !string.IsNullOrWhiteSpace(u.ExtractedTitle)
                ? u.ExtractedTitle!.Trim()
                : Path.GetFileNameWithoutExtension(u.OriginalFileName);
            return new ToolSlot(name, DescriptionFromResearch(u.ExtractedToolResearchJson), u.ExtractedToolResearchJson);
        })
        .Where(s => !string.IsNullOrWhiteSpace(s.Name))
        .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .ToList();
    }

    private static string? DescriptionFromResearch(string? researchJson)
    {
        if (string.IsNullOrWhiteSpace(researchJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(researchJson);
            if (doc.RootElement.TryGetProperty("summary", out var summary)
                && summary.ValueKind == JsonValueKind.String)
            {
                return summary.GetString();
            }

            if (doc.RootElement.TryGetProperty("whatItDoes", out var what)
                && what.ValueKind == JsonValueKind.String)
            {
                return what.GetString();
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return researchJson.Length > 240 ? researchJson[..240] : researchJson;
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
            RelatedArticleUrl = pillarArticleUrl,
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
        var slug = SlugHelper.Slugify($"top-ai-tools-for-{topic}");
        if (string.IsNullOrWhiteSpace(slug) || slug == "top-ai-tools-for")
        {
            slug = "top-ai-tools-roundup";
        }

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
        var now = DateTime.UtcNow;
        var roundupUrl = $"{context.ToolBaseUrl.TrimEnd('/')}/{context.Department}/{slug}";

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
            RelatedArticleUrl = pillarArticleUrl,
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
    /// becomes the document's lede, the rest become its top-level sections. Overview + Key
    /// Capabilities (sections 0-1, tool-intrinsic, not department-specific) are cached across
    /// projects/departments via <see cref="_toolContentCacheStore"/> — a cache hit only calls the
    /// LLM for the two remaining project-specific sections (Implementation Considerations, When to
    /// Use). Skips the cache entirely on a targeted revision (revisionNotes present) so feedback
    /// always regenerates fresh content, which then refreshes the cache for future reuse.</summary>
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
        List<Section> sections;

        // The cache is purely an optimization — any failure reading or writing it (malformed
        // entry, the store itself unreachable/erroring, e.g. before its backing table exists)
        // must fall back to full generation rather than break tool generation entirely.
        CachedToolContent? cached = null;
        if (string.IsNullOrWhiteSpace(revisionNotes))
        {
            try
            {
                cached = await _toolContentCacheStore.GetAsync(app.Name, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tool content cache lookup failed for '{App}' — generating fully.", app.Name);
            }
        }

        List<Section>? cachedSections = null;
        if (cached is not null)
        {
            try
            {
                cachedSections = JsonSerializer.Deserialize<List<Section>>(cached.OverviewJson, CacheJsonOptions);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Tool content cache entry for '{App}' was malformed — regenerating fully.", app.Name);
            }
        }

        if (cachedSections is { Count: 2 })
        {
            _logger.LogInformation("Tool content cache hit for '{App}' — reusing Overview/Key Capabilities.", app.Name);
            var remainderResult = await provider.CompleteAsync(
                _promptBuilder.BuildToolBodyRemainderPrompt(context, pillarMetadata, app, toolSlug, revisionNotes, researchJson),
                cancellationToken);
            var remainderSections = LlmResponseJsonParser.ParseSections(remainderResult.Content, $"tool page remainder '{app.Name}'");
            sections = cachedSections.Concat(remainderSections).ToList();
        }
        else
        {
            sections = await GenerateFullToolBodyAsync(provider, context, pillarMetadata, app, researchJson, toolSlug, revisionNotes, cancellationToken);

            if (sections.Count >= 2)
            {
                try
                {
                    var overviewAndCapabilities = JsonSerializer.Serialize(sections.Take(2).ToList(), CacheJsonOptions);
                    await _toolContentCacheStore.SaveAsync(app.Name, app.Name, overviewAndCapabilities, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Tool content cache save failed for '{App}' — this generation still succeeded, just not cached.", app.Name);
                }
            }
        }

        var wordCount = ContentDocumentText.CountWords(sections);

        // Soft gate (matches blog): out-of-range drafts still save so the user can review/regenerate.
        // A hard throw here used to 502 the whole Step 6 batch via Task.WhenAll and discard siblings.
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
