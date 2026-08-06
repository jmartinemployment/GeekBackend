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
using GeekAPI.Services.ContentCreator.Guardrail;
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
    IReadOnlyList<string> TopicalNeighbors,
    InformationGainNote? InformationGain = null);

public sealed record ContentGapDto(
    string Id,
    string Topic,
    string? SectionPath,
    string Reason,
    bool SuggestPillar);

public sealed record SiteAnalysisDto(Guid Id, string Domain, string Status);

public sealed record SiteAnalysisStoredPayload(
    IReadOnlyList<ContentGapDto> Gaps,
    IReadOnlyList<RelatedPageDto> SitePages,
    IReadOnlyList<string> TopicalNeighbors,
    Guid? SeoProfileId = null,
    Guid? SeoProjectId = null);

/// <summary>
/// Content Creator generation helpers. Source of truth = Content Writer v2 only
/// (prompt builders + LLM providers). Do not call Content Writer v3 generators.
/// </summary>
public class GccGenerateService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>CWV2 ContentDocument wire format (Paragraph discriminator uses "type").</summary>
    private static readonly JsonSerializerOptions CwDocumentJson = CreateCwDocumentJson();

    private static JsonSerializerOptions CreateCwDocumentJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    private readonly IContentPromptBuilder _prompts;
    private readonly IContentProviderFactory _cwProviders;
    private readonly ISoftwareApplicationSchemaBuilder _softwareApplicationSchemaBuilder;
    private readonly CompanyProfileOptions _company;
    private readonly ILogger<GccGenerateService> _logger;

    public GccGenerateService(
        IContentPromptBuilder prompts,
        IContentProviderFactory cwProviders,
        ISoftwareApplicationSchemaBuilder softwareApplicationSchemaBuilder,
        IOptions<CompanyProfileOptions> company,
        ILogger<GccGenerateService> logger)
    {
        _prompts = prompts;
        _cwProviders = cwProviders;
        _softwareApplicationSchemaBuilder = softwareApplicationSchemaBuilder;
        _company = company.Value;
        _logger = logger;
    }

    public static SiteSectionContextDto? ParseSiteSection(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<SiteSectionContextDto>(json, JsonOpts)
                ?? throw new InvalidOperationException("Site section JSON deserialized to null.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Site section JSON could not be parsed.", ex);
        }
    }

    /// <summary>
    /// Site Analyzer <em>handoff</em> creates carry a site section with relatedPages — those must
    /// stay non-empty. Domain-only grounding (SiteAnalysisId with no section) is allowed: Generate
    /// uses page-section trees for "must mention" injection, not relatedPages.
    /// </summary>
    public static void ValidateSiteSectionGate(Guid? siteAnalysisId, SiteSectionContextDto? section)
    {
        if (siteAnalysisId is null || siteAnalysisId == Guid.Empty) return;
        if (section is null) return;
        if (section.RelatedPages is null || section.RelatedPages.Count == 0)
            throw new InvalidOperationException(
                "Site Analyzer–started Generate requires non-empty relatedPages in site section context.");
    }

    /// <summary>
    /// Generate reads persisted BriefJson from the create only — not client request bodies.
    /// </summary>
    public static void ValidateBriefRequired(GccCreateDto create)
    {
        if (string.IsNullOrWhiteSpace(create.BriefJson))
            throw new InvalidOperationException("brief required");

        using var doc = JsonDocument.Parse(create.BriefJson);
        var root = doc.RootElement;
        static string? S(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        // Compat-first: accept the new Google-aligned field names OR the legacy
        // names during the migration window. Prefer the first non-empty value.
        static string? Any(JsonElement el, params string[] names)
        {
            foreach (var n in names)
            {
                var v = S(el, n);
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return null;
        }
        static bool HasArrayItem(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array && p.GetArrayLength() > 0;

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Any(root, "primaryIntent", "intent"))) missing.Add("primaryIntent");
        if (string.IsNullOrWhiteSpace(S(root, "buyingStage"))) missing.Add("buyingStage");
        if (string.IsNullOrWhiteSpace(Any(root, "audienceSegment", "audiencePrimary"))) missing.Add("audienceSegment");
        if (string.IsNullOrWhiteSpace(Any(root, "audienceNotes", "audienceDetail"))) missing.Add("audienceNotes");
        if (string.IsNullOrWhiteSpace(S(root, "angle"))) missing.Add("angle");
        if (string.IsNullOrWhiteSpace(S(root, "ctaType"))) missing.Add("ctaType");
        // toneOfVoice/eeatSignals are new; only enforce when the brief has already
        // been migrated (legacy briefs carry a numeric toneOfVoice object, no eeatSignals).
        var isNewBrief = S(root, "toneOfVoice") is not null
            || root.TryGetProperty("eeatSignals", out _)
            || root.TryGetProperty("primaryIntent", out _);
        if (isNewBrief)
        {
            if (string.IsNullOrWhiteSpace(S(root, "toneOfVoice"))) missing.Add("toneOfVoice");
            if (!HasArrayItem(root, "eeatSignals")) missing.Add("eeatSignals");
        }
        if (string.IsNullOrWhiteSpace(S(root, "lengthBand"))) missing.Add("lengthBand");
        if (missing.Count > 0)
            throw new InvalidOperationException($"brief required: missing {string.Join(", ", missing)}");
    }

    public static string BuildBriefAndResearchBlock(GccCreateDto create)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== BRIEF ===");
        sb.AppendLine("(Persisted Content Brief — follow these controls; if audience detail conflicts with primary, follow detail.)");
        sb.AppendLine(create.BriefJson!.Trim());
        sb.AppendLine();

        var research = GccResearchFetchService.Deserialize(create.ResearchJson);
        if (research?.Quoteables is { Count: > 0 })
        {
            sb.AppendLine("=== QUOTEABLE RESEARCH (destination pages — quote/paraphrase; do not invent) ===");
            // Uploaded research is unlimited — read every quoteable (per-page heading/paragraph
            // trimming below still bounds prompt size).
            foreach (var q in research.Quoteables)
            {
                sb.AppendLine($"[{q.Title}] ({q.Url})");
                foreach (var h in q.Headings.Take(GccResearchCaps.MaxHeadingsPerPage))
                    sb.AppendLine($"- {h}");
                foreach (var p in q.Paragraphs.Take(GccResearchCaps.MaxParagraphsPerPage))
                    sb.AppendLine($"- {p}");
                sb.AppendLine();
            }
        }

        if (research?.SerpPages is { Count: > 0 } serpPages)
        {
            // One labeled block per uploaded Keyword SERP file: title→URL + related searches only.
            // No PAA (always discarded from these uploads) and no Shape.Guidance (advisory —
            // surfaced in the UI only; the operator adds it to writing notes themselves).
            foreach (var page in serpPages)
            {
                sb.AppendLine($"=== KEYWORD SERP: {page.FileName} ===");
                foreach (var o in page.Organics)
                    sb.AppendLine($"- {o.Title} ({o.Url})");
                if (page.RelatedSearches.Count > 0)
                {
                    sb.AppendLine("Related searches:");
                    foreach (var r in page.RelatedSearches) sb.AppendLine($"- {r}");
                }
                sb.AppendLine();
            }
        }

        if (research?.SerpIndex is { } serp)
        {
            if (serp.OrganicTitles.Count > 0)
            {
                sb.AppendLine("SERP organic titles (index):");
                foreach (var t in serp.OrganicTitles.Take(12)) sb.AppendLine($"- {t}");
            }
            if (serp.PeopleAlsoAsk.Count > 0)
            {
                sb.AppendLine("People Also Ask (index):");
                foreach (var t in serp.PeopleAlsoAsk.Take(15)) sb.AppendLine($"- {t}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Part 4 — Consultant / four-phase methodology system-appendix, injected at the
    /// GeekAPI call site (NOT by editing the external content-writer-v2 prompt builder).
    /// Applied when toneOfVoice == consultant_professional, or the angle is the
    /// comprehensive ultimate-guide. Returns "" when it should not apply.
    /// </summary>
    public static string BuildConsultantAppendix(GccCreateDto create)
    {
        if (string.IsNullOrWhiteSpace(create.BriefJson)) return string.Empty;
        string? tone = null, angle = null;
        try
        {
            using var doc = JsonDocument.Parse(create.BriefJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("toneOfVoice", out var t) && t.ValueKind == JsonValueKind.String)
                tone = t.GetString();
            if (root.TryGetProperty("angle", out var a) && a.ValueKind == JsonValueKind.String)
                angle = a.GetString();
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        var isConsultant = string.Equals(tone, "consultant_professional", StringComparison.OrdinalIgnoreCase);
        var isUltimateGuide = string.Equals(angle, "ultimate_guide", StringComparison.OrdinalIgnoreCase);
        if (!isConsultant && !isUltimateGuide) return string.Empty;

        return string.Join('\n', new[]
        {
            "=== ROLE & METHOD (consultant appendix) ===",
            "Write as a Senior IT Consultant advising local SMBs on AI implementation and business-process",
            "automation. Voice: objective, authoritative, technical, analytical (newspaper-style). Use first-person",
            "plural or objective third-person advisor. Assume peer-level technical knowledge; high scannability.",
            "Weave these four phases into the narrative (do not label them mechanically):",
            "1. Business Objectives Alignment — the measurable goal / pain point (ROI, bottlenecks, cost of inaction).",
            "2. Data Quality Assessment — integrity, schema, storage (pooling, JSONB, validation).",
            "3. Tech Selection & Architecture — specific tools over generics (decoupled services, routing, benchmarks).",
            "4. Pilot Implementation Strategy — execution, smoke tests, validation (local integration, TDD, sandboxed rollout).",
            "Constraints: ban AI filler / clichés; Markdown ##/### outline; close with an FAQ drawn from the",
            "People Also Ask / related searches in the brief. Keep temperature low.",
        });
    }

    /// <summary>
    /// Finds the heading node matching <paramref name="topic"/> anywhere in the site's persisted
    /// page-section trees and renders its real sub-topics as a "must mention" prompt block.
    /// Matching is deterministic, not probabilistic "fuzzy": (1) exact normalized-slug match,
    /// (2) one slug containing the other. Neither hit → empty string, no injection — a wrong
    /// match would actively misdirect Generate with confidently-wrong context, which is worse
    /// than no grounding at all.
    /// </summary>
    public static string BuildMustMentionSubtopicsBlock(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees,
        string topic)
    {
        if (string.IsNullOrWhiteSpace(topic) || pageTrees.Count == 0)
            return string.Empty;

        var topicSlug = Slugify(topic);
        HttpGeekSeoSiteAnalyzerClient.PageSectionDto? exactMatch = null;
        HttpGeekSeoSiteAnalyzerClient.PageSectionDto? containsMatch = null;

        foreach (var page in pageTrees)
        {
            List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>? roots;
            try
            {
                roots = JsonSerializer.Deserialize<List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>>(page.TreeJson, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }
            if (roots is null) continue;

            foreach (var node in FlattenSections(roots))
            {
                var nodeSlug = Slugify(node.HeadingText);
                if (string.Equals(nodeSlug, topicSlug, StringComparison.OrdinalIgnoreCase))
                {
                    exactMatch = node;
                    break;
                }

                if (containsMatch is null &&
                    (nodeSlug.Contains(topicSlug, StringComparison.OrdinalIgnoreCase)
                     || topicSlug.Contains(nodeSlug, StringComparison.OrdinalIgnoreCase)))
                {
                    containsMatch = node;
                }
            }

            if (exactMatch is not null) break;
        }

        var matched = exactMatch ?? containsMatch;
        if (matched is null || matched.Children is null || matched.Children.Count == 0)
            return string.Empty;

        var subtopics = matched.Children
            .Select(c => c.HeadingText)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (subtopics.Count == 0)
            return string.Empty;

        var lines = new List<string>
        {
            "=== MUST MENTION (real sub-topics from the analyzed site) ===",
            $"This topic corresponds to a real page section (\"{matched.HeadingText}\") with the following real",
            "sub-topics on the analyzed site. The draft must mention each of these:",
        };
        lines.AddRange(subtopics.Select(s => $"- {s}"));
        return string.Join('\n', lines);
    }

    private static IEnumerable<HttpGeekSeoSiteAnalyzerClient.PageSectionDto> FlattenSections(
        IEnumerable<HttpGeekSeoSiteAnalyzerClient.PageSectionDto> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node.Children is null) continue;
            foreach (var child in FlattenSections(node.Children))
                yield return child;
        }
    }

    public async Task<string> GenerateStartingContentAsync(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        CancellationToken ct,
        string? mustMentionBlock = null)
    {
        ValidateSiteSectionGate(create.SiteAnalysisId, section);
        ValidateBriefRequired(create);
        var briefBlock = BuildBriefAndResearchBlock(create);
        if (!string.IsNullOrWhiteSpace(mustMentionBlock))
            briefBlock = $"{briefBlock}\n\n{mustMentionBlock}";

        if (string.Equals(create.StartingContentType, "imagePrompt", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(create.Topic) || string.IsNullOrWhiteSpace(create.Notes))
                throw new InvalidOperationException("Standalone image prompt requires topic and notes.");
            return await GenerateImagePromptJsonAsync(
                create.Topic,
                $"{briefBlock}\n\n{create.Notes}",
                null,
                provider,
                ct);
        }

        if (string.Equals(create.StartingContentType, "aiTool", StringComparison.OrdinalIgnoreCase))
        {
            var tool = await GenerateToolPageAsync(
                toolName: create.Topic,
                brief: create.Notes,
                sourceContext: $"{briefBlock}\n\n{BuildAudience(create, section)}",
                department: string.IsNullOrWhiteSpace(create.Department) ? "marketing" : create.Department,
                relatedArticleUrl: null,
                provider: provider,
                ct: ct);
            return JsonSerializer.Serialize(new
            {
                title = tool.Name,
                metaDescription = tool.Metadata.MetaDescription,
                summary = tool.Metadata.Summary,
                body = tool.Document,
                jsonLdSchema = tool.JsonLdSchema,
            }, CwDocumentJson);
        }

        // Content Creator long-form: CWV2 standalone blog body + persisted brief/research.
        var llm = GetLlm(provider);
        var consultantAppendix = BuildConsultantAppendix(create);
        var sourceContext = $"{briefBlock}\n\n{BuildAudience(create, section)}";
        if (consultantAppendix.Length > 0)
            sourceContext = $"{sourceContext}\n\n{consultantAppendix}";
        var context = BuildMinimalContext(
            create.Topic,
            sourceContext,
            ToLlm(provider),
            create.Department);
        var metadata = new BlogMetadataDraft(
            Title: create.Topic.Trim(),
            MetaDescription: Truncate((create.Notes ?? create.Topic).Trim(), 160),
            Keywords: [create.Topic.Trim()],
            SectionOutline: ["Overview", "Key considerations", "Next steps"]);
        var bodyResult = await llm.CompleteAsync(
            _prompts.BuildStandaloneBlogBodyPrompt(context, metadata, revisionNotes: null),
            ct);
        var sections = LlmResponseJsonParser.ParseSections(bodyResult.Content, "standalone blog body");
        if (sections.Count == 0)
            throw new InvalidOperationException("CWV2 blog body returned no sections.");
        var lede = sections[0] with { Tag = "h2" };
        var blogDocument = new ContentDocument(lede, sections.Skip(1).ToList());
        blogDocument = ContentGuardrail.Apply(blogDocument).Document;
        return JsonSerializer.Serialize(blogDocument, CwDocumentJson);
    }

    /// <summary>
    /// Legacy GCC artifact revise. Prefer project revise via CWV2 orchestrator.
    /// Uses CWV2 section JSON + revision notes — not CWV3 ReviseStructuredDraftAsync.
    /// </summary>
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

        var document = JsonSerializer.Deserialize<ContentDocument>(currentJson, CwDocumentJson)
            ?? throw new InvalidOperationException("Current body is not a CWV2 ContentDocument.");

        var llm = GetLlm(provider);
        var context = BuildMinimalContext(document.Lede.Heading, FlattenDocument(document), ToLlm(provider));
        var metadata = new BlogMetadataDraft(
            Title: document.Lede.Heading,
            MetaDescription: Truncate(document.Lede.Heading, 160),
            Keywords: [document.Lede.Heading],
            SectionOutline: document.Sections.Select(s => s.Heading).Where(h => !string.IsNullOrWhiteSpace(h)).ToList());
        var bodyResult = await llm.CompleteAsync(
            _prompts.BuildStandaloneBlogBodyPrompt(context, metadata, revisionNotes: fb),
            ct);
        var sections = LlmResponseJsonParser.ParseSections(bodyResult.Content, "revised blog body");
        if (sections.Count == 0)
            throw new InvalidOperationException("CWV2 revise returned no sections.");
        var lede = sections[0] with { Tag = "h2" };
        var revised = new ContentDocument(lede, sections.Skip(1).ToList());
        revised = ContentGuardrail.Apply(revised).Document;
        return JsonSerializer.Serialize(revised, CwDocumentJson);
    }

    public async Task<string> GenerateImagePromptJsonAsync(
        string topic,
        string? notes,
        string? artifactContext,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        var llm = GetLlm(provider);
        var result = await llm.CompleteAsync(
            _prompts.BuildStandaloneImagePrompt(topic, notes, artifactContext),
            ct);
        var raw = result.Content?.Trim() ?? string.Empty;

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
                style = "Illustration",
                negativePrompt = "readable text, logos, watermarks",
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

        // Plan §7: one LLM call for chosen channels (not one call per post).
        var llm = GetLlm(provider);
        var channelList = string.Join(", ", channels);
        var brief =
            $"Produce ONE pack JSON for ONLY these channel slots (one variant object per slot, same order): {channelList}. " +
            "Shape: { \"variants\": [ { \"channel\": string, \"title\": string, \"headline\": string|null, \"body\": string, \"cta\": string|null, \"hashtags\": string[]|null } ] }. " +
            "Reply with valid JSON only.\nSource content:\n" + sourceJson;
        var request = new ChatCompletionRequest(
            Messages:
            [
                new ChatMessage(ChatRole.System, "You write marketing channel packs as strict JSON only."),
                new ChatMessage(ChatRole.User, brief),
            ],
            Temperature: 0.4);
        var result = await llm.CompleteAsync(request, ct);
        var raw = result.Content?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Social/ads pack LLM returned empty content.");

        // Strip markdown fences if the model wraps JSON.
        if (raw.StartsWith("```", StringComparison.Ordinal))
        {
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start < 0 || end <= start)
                throw new InvalidOperationException("Social/ads pack LLM returned non-JSON content.");
            raw = raw[start..(end + 1)];
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("variants", out var variants)
                || variants.ValueKind != JsonValueKind.Array
                || variants.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("Social/ads pack JSON missing non-empty variants array.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Social/ads pack LLM returned invalid JSON.", ex);
        }

        return raw;
    }

    public static IReadOnlyList<PackVariant> ParsePackVariants(string packJson)
    {
        using var doc = JsonDocument.Parse(packJson);
        var list = new List<PackVariant>();
        foreach (var el in doc.RootElement.GetProperty("variants").EnumerateArray())
        {
            var channel = el.TryGetProperty("channel", out var c) ? c.GetString() ?? "" : "";
            var title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var body = el.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var headline = el.TryGetProperty("headline", out var h) ? h.GetString() : null;
            var cta = el.TryGetProperty("cta", out var ct) ? ct.GetString() : null;
            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("Social/ads pack variant missing channel or body.");
            list.Add(new PackVariant(channel.Trim(), title.Trim(), headline, body.Trim(), cta));
        }

        if (list.Count == 0)
            throw new InvalidOperationException("Social/ads pack has no variants.");
        return list;
    }

    public sealed record PackVariant(
        string Channel,
        string Title,
        string? Headline,
        string Body,
        string? Cta);

    /// <summary>
    /// CWV2 tool page: body + ToolMetadataDraft + SoftwareApplication JSON-LD
    /// (same contract as ToolPageGenerator.GenerateOneToolAsync).
    /// </summary>
    public sealed record ToolPageResult(
        string Name,
        string Slug,
        ContentDocument Document,
        ToolMetadataDraft Metadata,
        string JsonLdSchema,
        string? RelatedArticleUrl,
        int WordCount);

    public async Task<ToolPageResult> GenerateToolPageAsync(
        string toolName,
        string? brief,
        string? sourceContext,
        string department,
        string? relatedArticleUrl,
        ContentGeneratorProvider provider,
        CancellationToken ct,
        string? preferredSlug = null)
    {
        var llmType = ToLlm(provider);
        var llm = _cwProviders.Get(llmType);

        var name = toolName.Trim();
        var slug = string.IsNullOrWhiteSpace(preferredSlug) ? Slugify(name) : preferredSlug.Trim();
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

        var dept = string.IsNullOrWhiteSpace(department) ? "marketing" : department.Trim();
        var context = new ProjectGenerationContext(
            ProjectName: name,
            ProjectUrl: _company.ArticleBaseUrl,
            TargetKeyword: name,
            Department: dept,
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
        var wordCount = ContentDocumentText.CountWords(document);

        var metaResult = await llm.CompleteAsync(
            _prompts.BuildToolMetadataPrompt(context, pillarMeta, app, document),
            ct);
        var metadata = LlmResponseJsonParser.Parse<ToolMetadataDraft>(metaResult.Content, "tool metadata");

        var metaDescription = metadata.MetaDescription.Length > 160
            ? metadata.MetaDescription[..160]
            : metadata.MetaDescription;
        metadata = metadata with { MetaDescription = metaDescription };

        var toolUrl = $"{_company.ToolBaseUrl.TrimEnd('/')}/{dept}/{slug}";
        var now = DateTime.UtcNow;
        var schemaMeta = new ContentMetadata(
            name,
            metaDescription,
            context.AuthorName,
            context.PublisherName,
            context.PublisherLogoUrl,
            toolUrl,
            context.PublisherLogoUrl,
            now,
            now,
            pillarMeta.Keywords,
            wordCount);

        var pillarUrl = string.IsNullOrWhiteSpace(relatedArticleUrl)
            ? $"{_company.ArticleBaseUrl.TrimEnd('/')}/{dept}"
            : relatedArticleUrl;
        var jsonLd = _softwareApplicationSchemaBuilder.BuildToolPage(schemaMeta, pillarUrl, app);
        if (string.IsNullOrWhiteSpace(jsonLd))
            throw new InvalidOperationException($"CWV2 tool JSON-LD schema builder returned empty for '{name}'.");

        return new ToolPageResult(name, slug, document, metadata, jsonLd, pillarUrl, wordCount);
    }

    /// <summary>Legacy alias — prefer <see cref="GenerateToolPageAsync"/>.</summary>
    public async Task<(string Name, ContentDocument Document, string? MetaDescription, string? Summary)> GenerateToolAsync(
        string toolName,
        string? brief,
        string? sourceContext,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        var tool = await GenerateToolPageAsync(
            toolName, brief, sourceContext, "marketing", null, provider, ct);
        return (tool.Name, tool.Document, tool.Metadata.MetaDescription, tool.Metadata.Summary);
    }

    /// <summary>Serialize a CWV2 ContentDocument for legacy GCC artifact storage.</summary>
    public static string SerializeDocument(ContentDocument document) =>
        JsonSerializer.Serialize(document, CwDocumentJson);

    private IContentGenerationProvider GetLlm(ContentGeneratorProvider provider) =>
        _cwProviders.Get(ToLlm(provider));

    private static LlmProviderType ToLlm(ContentGeneratorProvider provider) =>
        provider == ContentGeneratorProvider.Anthropic ? LlmProviderType.Anthropic : LlmProviderType.OpenAi;

    private ProjectGenerationContext BuildMinimalContext(
        string topic,
        string notes,
        LlmProviderType llmType,
        string? department = null)
    {
        var paragraphs = string.IsNullOrWhiteSpace(notes)
            ? new List<string>()
            : new List<string> { notes };
        var dept = string.IsNullOrWhiteSpace(department) ? "marketing" : department.Trim();
        return new ProjectGenerationContext(
            ProjectName: topic,
            ProjectUrl: _company.ArticleBaseUrl,
            TargetKeyword: topic,
            Department: dept,
            SiteName: _company.PublisherName,
            DetectedTone: "Professional, consultative",
            DetectedFocus: topic,
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
    }

    private static string FlattenDocument(ContentDocument document)
    {
        var sb = new StringBuilder();
        sb.AppendLine(document.Lede.Heading);
        foreach (var p in document.Lede.Paragraphs)
            AppendParagraph(sb, p);
        foreach (var section in document.Sections)
        {
            sb.AppendLine(section.Heading);
            foreach (var p in section.Paragraphs)
                AppendParagraph(sb, p);
        }
        return sb.ToString();
    }

    private static void AppendParagraph(StringBuilder sb, Paragraph paragraph)
    {
        switch (paragraph)
        {
            case TextParagraph text:
                sb.AppendLine(string.Join("", text.Runs.Select(r => r.Text)));
                break;
            case ListParagraph list:
                foreach (var item in list.Items)
                    sb.AppendLine("- " + string.Join("", item.Select(r => r.Text)));
                break;
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

    public static string SerializeAnalysisPayload(SiteAnalysisStoredPayload payload) =>
        JsonSerializer.Serialize(payload, JsonOpts);

    public static SiteAnalysisStoredPayload ParseAnalysisPayload(string? gapsJson)
    {
        if (string.IsNullOrWhiteSpace(gapsJson))
            throw new InvalidOperationException("Site analysis payload is missing.");

        try
        {
            using var doc = JsonDocument.Parse(gapsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var legacyGaps = JsonSerializer.Deserialize<List<ContentGapDto>>(gapsJson, JsonOpts)
                    ?? throw new InvalidOperationException("Site analysis gaps JSON is invalid.");
                // Legacy analyses stored gaps only — no site pages. Callers must fail closed for section context.
                return new SiteAnalysisStoredPayload(legacyGaps, [], []);
            }

            return JsonSerializer.Deserialize<SiteAnalysisStoredPayload>(gapsJson, JsonOpts)
                ?? throw new InvalidOperationException("Site analysis payload JSON is invalid.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Site analysis payload JSON could not be parsed.", ex);
        }
    }

    public static IReadOnlyList<ContentGapDto> DeserializeGaps(string? gapsJson) =>
        ParseAnalysisPayload(gapsJson).Gaps;

    /// <summary>
    /// Builds section context from stored Geek-SEO site pages for the chosen gap.
    /// Returns null when related pages cannot be resolved (caller must fail closed).
    /// </summary>
    public static SiteSectionContextDto? TryBuildSectionContext(
        Guid analysisId,
        SiteAnalysisStoredPayload payload,
        string gapTopic)
    {
        if (string.IsNullOrWhiteSpace(gapTopic)) return null;

        var gap = payload.Gaps.FirstOrDefault(g =>
            string.Equals(g.Topic, gapTopic, StringComparison.OrdinalIgnoreCase));
        var sectionPath = gap?.SectionPath;

        IEnumerable<RelatedPageDto> candidates = payload.SitePages
            .Where(p => !string.IsNullOrWhiteSpace(p.Url));

        if (!string.IsNullOrWhiteSpace(sectionPath))
        {
            candidates = candidates.Where(p =>
                string.Equals(p.Title, sectionPath, StringComparison.OrdinalIgnoreCase)
                || (p.Excerpt?.Contains(sectionPath, StringComparison.OrdinalIgnoreCase) ?? false)
                || p.Headings.Any(h =>
                    h.Contains(sectionPath, StringComparison.OrdinalIgnoreCase)));
        }

        var related = candidates
            .GroupBy(p => p.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(12)
            .ToList();

        if (related.Count == 0) return null;

        if (payload.TopicalNeighbors.Count == 0) return null;
        var neighbors = payload.TopicalNeighbors;

        var informationGain = GccSavedSerpParser.BuildPartialInformationGain(gapTopic, related);

        return new SiteSectionContextDto(
            analysisId,
            gapTopic,
            sectionPath,
            related,
            neighbors,
            informationGain);
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
