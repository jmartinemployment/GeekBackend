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

    /// <summary>Tools and the writing assignment from a single tree fetch.</summary>
    Task<ToolPageGenerator.CrawlHierarchy> ListCrawlHierarchyAsync(
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
        Action<int>? reportTotal = null,
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
    private readonly GccProjectSiteStructureReader _siteStructure;
    private readonly ILogger<ToolPageGenerator> _logger;

    public ToolPageGenerator(
        ISoftwareApplicationSchemaBuilder softwareApplicationSchemaBuilder,
        IContentPromptBuilder promptBuilder,
        GccProjectSiteStructureReader siteStructure,
        ILogger<ToolPageGenerator> logger)
    {
        _softwareApplicationSchemaBuilder = softwareApplicationSchemaBuilder;
        _promptBuilder = promptBuilder;
        _siteStructure = siteStructure;
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
        Action<int>? reportTotal = null,
        CancellationToken cancellationToken = default)
    {
        var toolSlots = await ResolveToolSlotsAsync(project, cancellationToken);
        if (toolSlots.Count == 0)
        {
            return new ToolGenerationResult(ToolGenerationOutcome.ToolsSectionEmpty, []);
        }

        // Href is the slot's link on our own pillar, so it is PageUrl. The product's own domain is
        // not carried on a tool slot, and guessing one would assert where a real company lives.
        var applications = toolSlots
            .Select(s => new SoftwareApplicationDescriptor(
                s.Name,
                s.Description,
                Url: null,
                PageUrl: string.IsNullOrWhiteSpace(s.Href) ? null : s.Href))
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

        var includeHub = toolSlugsToRegenerate is null or { Count: 0 };
        reportTotal?.Invoke(slotsToGenerate.Count + (includeHub ? 1 : 0));

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

        if (includeHub)
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

    /// <summary>Tools and the writing assignment, from a single tree fetch.</summary>
    public sealed record CrawlHierarchy(
        IReadOnlyList<GccGenerateService.CrawlTool> Tools,
        HierarchyAssignment? Assignment);

    public async Task<IReadOnlyList<GccGenerateService.CrawlTool>> ListCrawlToolsAsync(
        Project project,
        CancellationToken cancellationToken = default) =>
        (await ListCrawlHierarchyAsync(project, cancellationToken)).Tools;

    /// <summary>
    /// The site section this project's keyword matches, and the tools linked under it, from the crawl.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This asked Site Analyzer until 2026-10-01, and passed it the wrong kind of id:
    /// <c>project.ProjectSiteRunId</c> is a Geek-Crawler-v2 run id and the tree route wanted a Site
    /// Analyzer profile id — the local here was even named <c>profileId</c>. Site Analyzer no longer
    /// exists and its routes were deleted, so the call could only fail; unlike the sibling defect in
    /// <c>GccController</c>, which failed to null, this one <b>threw</b>. A project with a site run and
    /// a keyword — a correctly configured project — therefore failed generation outright, while a
    /// project with no run id returned empty and proceeded. Configuring the project was what broke it.
    /// </para>
    /// <para>
    /// The same read the live <c>project-site/runs/{runId}/hierarchy-match</c> route does, through the
    /// one reader both now share: blocks from the crawl, the structure built from them, v1's own
    /// matcher over the result. Nothing crawls here.
    /// </para>
    /// <para>
    /// <b>Empty is an answer, and it is not an exception.</b> No run id, no keyword, a run with no
    /// pages, or nothing on the site matching the keyword each return an empty hierarchy — the project
    /// writes without site structure, which is what it already did whenever the old call failed to
    /// null. What is gone is throwing on an infrastructure failure that is now impossible to have.
    /// </para>
    /// </remarks>
    public async Task<CrawlHierarchy> ListCrawlHierarchyAsync(
        Project project,
        CancellationToken cancellationToken = default)
    {
        if (project.ProjectSiteRunId is not Guid runId || runId == Guid.Empty)
            return new CrawlHierarchy([], null);

        var keyword = project.TargetKeyword?.Trim() ?? "";
        if (keyword.Length == 0)
            return new CrawlHierarchy([], null);

        var structure = await _siteStructure.ReadAsync(runId, cancellationToken).ConfigureAwait(false);
        if (structure is null)
        {
            _logger.LogInformation(
                "Site structure: run {RunId} returned no pages, so project {ProjectId} writes without it.",
                runId, project.Id);
            return new CrawlHierarchy([], null);
        }

        var matches = GccSiteStructureMatch.MatchAll(structure, [keyword]);

        // The match with children is preferred over a bare heading match, the same choice the
        // must-mention read makes: a section with subheadings is the one that describes the topic,
        // where a lone matching heading is often a nav label or a card title.
        var matched = matches.FirstOrDefault(m => m.ChildHeadings.Length > 0) ?? matches.FirstOrDefault();
        if (matched is null)
        {
            _logger.LogInformation(
                "Site structure: nothing on run {RunId} matches \"{Keyword}\" for project {ProjectId}.",
                runId, keyword, project.Id);
            return new CrawlHierarchy([], null);
        }

        // Named anchors under the matched heading. A tool without its href cites nothing, so an
        // anchor with no href is not a tool here -- the same rule the old extraction applied.
        var tools = matched.RecommendedTools
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(t => new GccGenerateService.CrawlTool(
                t.Name.Trim(), string.IsNullOrWhiteSpace(t.Href) ? null : t.Href.Trim()))
            .ToList();

        _logger.LogInformation(
            "Site structure: project {ProjectId} matched \"{Heading}\" (h{Level}, {Kind}) on {PageUrl} "
            + "with {ChildCount} child heading(s) and {ToolCount} linked tool(s).",
            project.Id, matched.MatchedHeading, matched.Level, matched.Kind,
            matched.SourcePageUrl ?? "(no page url)", matched.ChildHeadings.Length, tools.Count);

        var assignment = new HierarchyAssignment
        {
            Heading = matched.MatchedHeading.Trim(),
            Level = matched.Level,
            // Deliberately empty. The project-site crawl is read for structure -- heading levels, the
            // anchors under a heading, what the site already covers -- and its paragraphs are not
            // consumed anywhere: this piece is written from partner evidence, not from the operator's
            // own prose, which it must not repeat.
            Paragraphs = [],
            Links = tools.Select(t => new ToolInfo { Name = t.Name, Href = t.Href }).ToList(),
            // The child headings as they stand. The old tree projection recursed with each child's own
            // links and paragraphs; the matcher reports children as headings, and the anchors under the
            // matched section are already collected above rather than split across the subtree.
            Children = matched.ChildHeadings
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => new HierarchyAssignment { Heading = h.Trim() })
                .ToList(),
        };

        return new CrawlHierarchy(tools, assignment);
    }

    private async Task<List<ToolSlot>> ResolveToolSlotsAsync(Project project, CancellationToken cancellationToken)
    {
        var fromHierarchy = project.HierarchyToolsByHeading
            .SelectMany(g => g.Tools ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(t => new ToolSlot(t.Name.Trim(), null, null, string.IsNullOrWhiteSpace(t.Href) ? null : t.Href.Trim()))
            .ToList();
        // Single-name hierarchy snapshot is not a tool set (often one category heading).
        if (fromHierarchy.Count >= 2)
        {
            // #region agent log
            _logger.LogInformation(
                "ResolveToolSlots using HierarchyToolsByHeading count={Count} for project {ProjectId}",
                fromHierarchy.Count,
                project.Id);
            // #endregion
            return fromHierarchy;
        }

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

    /// <summary>
    /// Generates the tool page: the shared 12-type hook, then the body sections it opens.
    ///
    /// The hook used to be the body's own first section promoted into the lede slot, which cost the
    /// page a section and meant it never got a purpose-written opening -- the same defect the
    /// Create path carried until 2026-09-23. It also ran the wrong way round: writing the body
    /// first and fitting an opening to the front of it is how a page reads well for three
    /// paragraphs and then turns into a chore.
    /// </summary>
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
        var ledeResult = await provider.CompleteAsync(
            _promptBuilder.BuildArticleLedePrompt(context, pillarMetadata), cancellationToken);
        var (lede, _) = LlmResponseJsonParser.ParseLede(ledeResult.Content, $"tool page '{app.Name}' lede");

        var sections = await GenerateFullToolBodyAsync(
            provider, context, pillarMetadata, app, researchJson, toolSlug, revisionNotes, lede, cancellationToken);

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

        return new ContentDocument(lede with { Tag = "h2" }, sections);
    }

    private async Task<List<Section>> GenerateFullToolBodyAsync(
        IContentGenerationProvider provider,
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SoftwareApplicationDescriptor app,
        string? researchJson,
        string toolSlug,
        string? revisionNotes,
        Section? lede,
        CancellationToken cancellationToken)
    {
        var result = await provider.CompleteAsync(
            _promptBuilder.BuildToolBodyPrompt(
                context, pillarMetadata, app, toolSlug,
                // The tool outline, from the one place it is defined. Not pillarMetadata's outline
                // -- that is the pillar's planned sections, a different page.
                GeekAPI.Services.ContentCreator.ContentTypes.ToolPrompts.Outline(context, app.Name),
                revisionNotes, researchJson, lede),
            cancellationToken);
        return LlmResponseJsonParser.ParseSections(result.Content, $"tool page '{app.Name}'").ToList();
    }
}
