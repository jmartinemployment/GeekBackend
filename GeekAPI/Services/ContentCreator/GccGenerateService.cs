using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Application.Services.SchemaBuilders;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using GeekAPI.Services.Gcw;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.ContentCreator;

public sealed record RelatedPageDto(string Url, string Title, string[] Headings, string Excerpt);

public sealed record SiteSectionContextDto(
    Guid SiteAnalysisId,
    string GapTopic,
    string? GapSectionPath,
    IReadOnlyList<RelatedPageDto> RelatedPages,
    IReadOnlyList<string> TopicalNeighbors);

public sealed record ContentGapDto(
    string Id,
    string Topic,
    string? SectionPath,
    string Reason,
    bool SuggestPillar);

public sealed record SiteAnalysisDto(Guid Id, string Domain, string Status, bool IsDemo);

public class GccGenerateService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IContentGeneratorFactory _generators;
    private readonly IContentPromptBuilder _prompts;
    private readonly IContentProviderFactory _cwProviders;
    private readonly CompanyProfileOptions _company;
    private readonly ILogger<GccGenerateService> _logger;

    public GccGenerateService(
        IContentGeneratorFactory generators,
        IContentPromptBuilder prompts,
        IContentProviderFactory cwProviders,
        IOptions<CompanyProfileOptions> company,
        ILogger<GccGenerateService> logger)
    {
        _generators = generators;
        _prompts = prompts;
        _cwProviders = cwProviders;
        _company = company.Value;
        _logger = logger;
    }

    public static SiteSectionContextDto? ParseSiteSection(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<SiteSectionContextDto>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public static void ValidateSiteSectionGate(Guid? siteAnalysisId, SiteSectionContextDto? section)
    {
        if (siteAnalysisId is null || siteAnalysisId == Guid.Empty) return;
        if (section is null || section.RelatedPages is null || section.RelatedPages.Count == 0)
            throw new InvalidOperationException(
                "Site Analyzer–started Generate requires non-empty relatedPages in site section context.");
    }

    public async Task<string> GenerateStartingContentAsync(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        ValidateSiteSectionGate(create.SiteAnalysisId, section);

        if (string.Equals(create.StartingContentType, "imagePrompt", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(create.Topic) || string.IsNullOrWhiteSpace(create.Notes))
                throw new InvalidOperationException("Standalone image prompt requires topic and notes.");
            return await GenerateImagePromptJsonAsync(create.Topic, create.Notes, null, provider, ct);
        }

        var audience = BuildAudience(create, section);
        var evidence = BuildEvidence(section);
        var generator = _generators.Get(provider);

        if (string.Equals(create.StartingContentType, "aiTool", StringComparison.OrdinalIgnoreCase))
        {
            var name = create.Topic.Trim();
            var body = await generator.GenerateStructuredDraftAsync(
                angle: $"AI tool page for {name}",
                audienceProfile: audience,
                buyingStage: "consideration",
                callToAction: $"Explore {name}",
                supportingEvidence: evidence,
                ct: ct);
            var meta = await generator.GenerateSectionAsync(
                "Tool metadata",
                $"{name}\n{create.Notes}",
                "Return a short JSON object with title, summary, and category for this AI tool.",
                ct);
            return JsonSerializer.Serialize(new { bodyDocument = JsonSerializer.Deserialize<object>(body), metadata = meta }, JsonOpts);
        }

        return await generator.GenerateStructuredDraftAsync(
            angle: create.Topic,
            audienceProfile: audience,
            buyingStage: "awareness",
            callToAction: "Learn more",
            supportingEvidence: evidence,
            ct: ct);
    }

    public async Task<string> ReviseAsync(
        string currentJson,
        string feedback,
        string scope,
        string? sectionPath,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        var fb = feedback.Trim();
        if (string.Equals(scope, "section", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(sectionPath))
                throw new InvalidOperationException("sectionPath is required when scope is section.");
            fb = $"Revise ONLY the section at path “{sectionPath}”. Leave all other sections unchanged.\n\n{fb}";
        }

        var generator = _generators.Get(provider);
        return await generator.ReviseStructuredDraftAsync(currentJson, fb, ct);
    }

    public async Task<string> GenerateImagePromptJsonAsync(
        string topic,
        string? notes,
        string? artifactContext,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        var context = new StringBuilder();
        context.AppendLine($"Topic/title: {topic}");
        if (!string.IsNullOrWhiteSpace(notes)) context.AppendLine($"Notes: {notes}");
        if (!string.IsNullOrWhiteSpace(artifactContext))
            context.AppendLine($"Artifact context:\n{artifactContext[..Math.Min(artifactContext.Length, 6000)]}");

        var generator = _generators.Get(provider);
        var raw = await generator.GenerateSectionAsync(
            "Image prompt",
            context.ToString(),
            "Return ONLY valid JSON: { \"prompt\": string, \"style\": string, \"negativePrompt\": string, \"aspectRatio\": string }. Prompt text only — not pixels.",
            ct);

        try
        {
            using var _ = JsonDocument.Parse(raw);
            return raw;
        }
        catch
        {
            return JsonSerializer.Serialize(new
            {
                prompt = raw,
                style = "editorial",
                negativePrompt = "",
                aspectRatio = "16:9",
            }, JsonOpts);
        }
    }

    public async Task<string> GenerateRepurposePackAsync(
        string sourceJson,
        IReadOnlyList<string> channels,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        if (channels.Count == 0)
            throw new InvalidOperationException("At least one channel required for pack.");
        var brief = $"Produce ONE pack JSON for ONLY these channels: {string.Join(", ", channels)}. Not one call per post. Shape: {{ \"variants\": [ {{ channel, title, headline?, body, cta?, hashtags? }} ] }}.";
        var generator = _generators.Get(provider);
        return await generator.GenerateRepurposePackAsync(sourceJson, brief, ct);
    }

    public async Task<(string Name, string BodyJson)> GenerateToolAsync(
        string toolName,
        string? brief,
        string? sourceContext,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        // Source of truth: Content Writer v2 tool prompts (BuildToolBodyPrompt + BuildToolMetadataPrompt),
        // same stack as POST /api/projects/{id}/generate/tools — not a homemade structured-draft wrapper.
        var llmType = provider == ContentGeneratorProvider.Anthropic
            ? LlmProviderType.Anthropic
            : LlmProviderType.OpenAi;
        var llm = _cwProviders.Get(llmType);

        var name = toolName.Trim();
        var slug = Slugify(name);
        var description = string.Join(
            "\n",
            new[] { brief, sourceContext }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var app = new SoftwareApplicationDescriptor(name, string.IsNullOrWhiteSpace(description) ? null : description);
        var pillarMeta = new ArticleMetadataDraft(
            Title: name,
            MetaDescription: Truncate((brief ?? name).Trim(), 160),
            Keywords: [name],
            SectionOutline:
            [
                "Overview",
                "Key Capabilities",
                "Implementation Considerations",
                "When to Use",
            ]);

        var paragraphs = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourceContext))
            paragraphs.Add(sourceContext.Trim());
        if (!string.IsNullOrWhiteSpace(brief))
            paragraphs.Add(brief.Trim());

        var context = new ProjectGenerationContext(
            ProjectName: name,
            ProjectUrl: _company.ArticleBaseUrl,
            TargetKeyword: name,
            Department: "marketing",
            SiteName: _company.PublisherName,
            DetectedTone: "Professional, consultative",
            DetectedFocus: name,
            CrawledHeadings: [],
            CrawledParagraphs: paragraphs,
            JsonLdStructuredSummary: null,
            KeywordSources: [],
            PeopleAlsoAskQuestions: [],
            PublisherName: _company.PublisherName,
            PublisherLogoUrl: _company.PublisherLogoUrl,
            AuthorName: _company.AuthorName,
            ArticleBaseUrl: _company.ArticleBaseUrl,
            BlogBaseUrl: _company.BlogBaseUrl,
            ToolBaseUrl: _company.ToolBaseUrl,
            ImplementerPositioning: _company.ImplementerPositioning,
            Provider: llmType,
            UseExactKeywordAsTitle: false,
            DesiredHeadings: null,
            MatchedUseCase: null);

        var bodyResult = await llm.CompleteAsync(
            _prompts.BuildToolBodyPrompt(context, pillarMeta, app, slug, brief),
            ct);
        var sections = LlmResponseJsonParser.ParseSections(bodyResult.Content, $"tool page '{name}'");
        if (sections.Count == 0)
            throw new InvalidOperationException($"CWV2 tool body returned no sections for '{name}'.");

        var lede = sections[0] with { Tag = "h2" };
        var document = new ContentDocument(lede, sections.Skip(1).ToList());

        try
        {
            var metaResult = await llm.CompleteAsync(
                _prompts.BuildToolMetadataPrompt(context, pillarMeta, app, document),
                ct);
            var meta = LlmResponseJsonParser.Parse<ToolMetadataDraft>(metaResult.Content, "tool metadata");
            var wrapped = JsonSerializer.Serialize(new
            {
                title = name,
                metaDescription = meta.MetaDescription,
                summary = meta.Summary,
                body = document,
            }, JsonOpts);
            return (name, wrapped);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CWV2 tool metadata failed for {Tool}; saving body only.", name);
            return (name, JsonSerializer.Serialize(document, JsonOpts));
        }
    }

    private static string Slugify(string value)
    {
        var s = value.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"[\s-]+", "-").Trim('-');
        return string.IsNullOrEmpty(s) ? "tool" : s;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    public static List<ContentGapDto> BuildDemoGaps(string? seedTopic)
    {
        var topicBase = string.IsNullOrWhiteSpace(seedTopic) ? "operations automation" : seedTopic.Trim();
        return
        [
            new("gap-1", $"{topicBase} for finance teams", "Finance / Accounting", "No dedicated page for this topic", true),
            new("gap-2", $"How to choose {topicBase}", "Guides", "Heading exists in topical map with no page", false),
            new("gap-3", $"{topicBase} vs spreadsheets", "Comparisons", "Orphan pillar candidate", true),
        ];
    }

    public static string SerializeGaps(IReadOnlyList<ContentGapDto> gaps) =>
        JsonSerializer.Serialize(gaps, JsonOpts);

    public static IReadOnlyList<ContentGapDto> DeserializeGaps(string? gapsJson)
    {
        if (string.IsNullOrWhiteSpace(gapsJson)) return Array.Empty<ContentGapDto>();
        try
        {
            return JsonSerializer.Deserialize<List<ContentGapDto>>(gapsJson, JsonOpts)
                ?? new List<ContentGapDto>();
        }
        catch
        {
            return Array.Empty<ContentGapDto>();
        }
    }

    public static SiteSectionContextDto BuildSectionContext(
        Guid analysisId,
        string domain,
        string? seedTopic,
        IReadOnlyList<ContentGapDto> gaps,
        string gapTopic)
    {
        var seed = string.IsNullOrWhiteSpace(seedTopic) ? domain : seedTopic.Trim();
        var gap = gaps.FirstOrDefault(g =>
            string.Equals(g.Topic, gapTopic, StringComparison.OrdinalIgnoreCase));
        var sectionPath = gap?.SectionPath ?? "General";
        var related = new List<RelatedPageDto>
        {
            new(
                $"https://{domain}/about",
                $"About {domain}",
                new[] { "Our approach", "Who we serve" },
                $"Existing overview content on {domain} relevant to {gapTopic}."),
            new(
                $"https://{domain}/resources",
                "Resources hub",
                new[] { "Guides", sectionPath ?? "Topics" },
                $"Related resources neighboring the {gapTopic} gap in the {sectionPath} section."),
            new(
                $"https://{domain}/blog",
                "Blog index",
                new[] { seed, "Best practices" },
                $"Blog coverage near {seed}; avoid duplicating these angles when writing {gapTopic}."),
        };

        return new SiteSectionContextDto(
            analysisId,
            gapTopic,
            sectionPath,
            related,
            new[] { seed, sectionPath ?? "General", "implementation" });
    }

    public static GcwSeoAnalyzer.SeoReport AnalyzeSeo(string bodyJson, string keyword) =>
        GcwSeoAnalyzer.Analyze(bodyJson, keyword);

    public static GcwPolishAnalyzer.PolishReport AnalyzePolish(string bodyJson) =>
        GcwPolishAnalyzer.Analyze(bodyJson, Array.Empty<string>());

    private static string BuildAudience(GccCreateDto create, SiteSectionContextDto? section)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Starting content type: {create.StartingContentType}");
        sb.AppendLine($"Topic / keyword: {create.Topic}");
        if (!string.IsNullOrWhiteSpace(create.Notes))
            sb.AppendLine($"Operator notes: {create.Notes}");
        if (section is not null)
        {
            sb.AppendLine("SITE SECTION CONTEXT (required — do not generate keyword-only):");
            if (!string.IsNullOrWhiteSpace(section.GapSectionPath))
                sb.AppendLine($"Section path: {section.GapSectionPath}");
            sb.AppendLine($"Gap topic: {section.GapTopic}");
            if (section.TopicalNeighbors.Count > 0)
                sb.AppendLine($"Topical neighbors: {string.Join(", ", section.TopicalNeighbors)}");
            sb.AppendLine("Related existing pages (align voice, avoid duplication, cross-link sensibly):");
            foreach (var p in section.RelatedPages)
            {
                sb.AppendLine($"- {p.Title} ({p.Url})");
                if (p.Headings.Length > 0)
                    sb.AppendLine($"  Headings: {string.Join(" | ", p.Headings)}");
                if (!string.IsNullOrWhiteSpace(p.Excerpt))
                    sb.AppendLine($"  Excerpt: {p.Excerpt}");
            }
        }
        return sb.ToString();
    }

    private static List<string> BuildEvidence(SiteSectionContextDto? section)
    {
        var list = new List<string>();
        if (section is null) return list;
        foreach (var p in section.RelatedPages)
        {
            list.Add($"{p.Title}: {p.Excerpt}");
        }
        return list;
    }
}
