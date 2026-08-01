using System.Text;
using System.Text.Json;
using GeekAPI.Services.Gcw;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;

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
    private readonly ILogger<GccGenerateService> _logger;

    public GccGenerateService(IContentGeneratorFactory generators, ILogger<GccGenerateService> logger)
    {
        _generators = generators;
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
        var generator = _generators.Get(provider);
        var audience = $"AI tool page for {toolName}. Brief: {brief}. Context: {sourceContext}";
        var body = await generator.GenerateStructuredDraftAsync(
            angle: toolName,
            audienceProfile: audience,
            buyingStage: "consideration",
            callToAction: $"Try {toolName}",
            supportingEvidence: new List<string>(),
            ct: ct);
        var meta = await generator.GenerateSectionAsync(
            "metadata",
            toolName,
            "Short metadata JSON: title, summary, category.",
            ct);
        var wrapped = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["lede"] = $"Overview of {toolName}.",
            ["sections"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["heading"] = toolName,
                    ["paragraphs"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["$type"] = "text",
                            ["runs"] = new[] { new Dictionary<string, string?> { ["text"] = body } },
                        },
                    },
                },
            },
        }, JsonOpts);
        // Prefer storing structured body when valid ContentDocument
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("sections", out _) || doc.RootElement.TryGetProperty("lede", out _))
                return (toolName, body);
        }
        catch { /* fall through */ }
        return (toolName, wrapped);
    }

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
