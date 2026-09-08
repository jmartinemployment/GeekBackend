using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.Rag;

/// <summary>
/// Intent-routed drafts grounded on partner + competitor Geek-Crawler-Rag chunks.
/// Soft-disabled when RAG URL unset or <c>GEEK_RAG_GENERATE_ENABLED=false</c>.
/// Phase F: long-form uses o1/o3 via <see cref="RagModelRouter"/>.
/// Phase D: slides/strategy + ad-template few-shot (soft-disable when Rag index missing).
/// </summary>
public sealed class RagGenerateService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IGeekCrawlerRagClient _rag;
    private readonly HttpGeekCrawlerRepository _crawlerRepo;
    private readonly IContentProviderFactory _providers;
    private readonly ILogger<RagGenerateService> _logger;
    private readonly bool _generateEnabled;
    private readonly bool _graphEnabled;
    private readonly bool _adTemplateIndexEnabled;
    private readonly bool _citeableGenerateEnabled;

    public RagGenerateService(
        IGeekCrawlerRagClient rag,
        HttpGeekCrawlerRepository crawlerRepo,
        IContentProviderFactory providers,
        ILogger<RagGenerateService> logger)
    {
        _rag = rag;
        _crawlerRepo = crawlerRepo;
        _providers = providers;
        _logger = logger;
        _generateEnabled = ParseEnabledFlag(Environment.GetEnvironmentVariable("GEEK_RAG_GENERATE_ENABLED"));
        // Default ON now that Geek-Crawler-Rag Phase D1/D2 ships; set =false to soft-disable.
        _graphEnabled = ParseEnabledFlag(Environment.GetEnvironmentVariable("GEEK_RAG_GRAPH_ENABLED"));
        _adTemplateIndexEnabled = ParseEnabledFlag(Environment.GetEnvironmentVariable("GEEK_RAG_AD_TEMPLATES_ENABLED"));
        // Default ON — Rag multi-step citeable generate; set =false to force GeekAPI one-shot.
        _citeableGenerateEnabled = ParseEnabledFlag(
            Environment.GetEnvironmentVariable("GEEK_RAG_CITEABLE_GENERATE_ENABLED"));
    }

    public RagGenerateStatusDto GetStatus()
    {
        var ragOn = _rag.IsEnabled;
        var genOn = _generateEnabled && ragOn;
        string? reason = null;
        if (!_generateEnabled)
            reason = "RAG generate soft-disabled (GEEK_RAG_GENERATE_ENABLED=false).";
        else if (!ragOn)
            reason = "Geek-Crawler-Rag client disabled (GEEK_CRAWLER_RAG_URL unset).";

        return new RagGenerateStatusDto
        {
            Available = genOn,
            RagClientEnabled = ragOn,
            GenerateEnabled = _generateEnabled,
            Reason = reason,
            WritingIntents = RagWritingIntents.All,
            EntitySeeds = RagEntitySeedList.Names,
            LongFormModel = RagModelRouter.ResolveModel(RagRetrievalFamily.LongForm),
            ShortFormModel = RagModelRouter.ResolveModel(RagRetrievalFamily.ShortForm),
            GraphRetrievalAvailable = genOn && _graphEnabled,
            AdTemplateIndexAvailable = genOn && _adTemplateIndexEnabled,
            CiteableGenerateAvailable = genOn && _citeableGenerateEnabled && ragOn,
            ModelPolicyVersion = ContentModelPolicy.CurrentVersion,
            ApprovedStageModels = ContentModelPolicy.ApprovedStageModels,
        };
    }

    public async Task<RagGenerateResponse> GenerateAsync(
        string ownerUserId,
        RagGenerateRequest request,
        CancellationToken ct)
    {
        var status = GetStatus();
        if (!status.Available)
        {
            if (request.RequireCiteable)
                throw new InvalidOperationException(
                    $"{status.Reason ?? "RAG generate unavailable."} Canonical PLAN/WRITE cannot continue without citeable RAG.");
            return new RagGenerateResponse
            {
                Intent = request.WritingIntent?.Trim() ?? "",
                SoftDisabled = true,
                Warnings = [status.Reason ?? "RAG generate unavailable."],
            };
        }

        if (!RagWritingIntents.TryNormalize(request.WritingIntent, out var intent))
            throw new ArgumentException(
                "writingIntent must be one of: " + string.Join(", ", RagWritingIntents.All));

        var topic = (request.Topic ?? "").Trim();
        if (topic.Length < 3)
            throw new ArgumentException("topic is required (min 3 characters).");

        var entities = NormalizeEntities(request.TargetEntities);
        var templates = NormalizeTemplates(request.AdTemplates).ToList();
        var family = RagWritingIntents.FamilyOf(intent);
        var warnings = new List<string>();
        var model = string.IsNullOrWhiteSpace(request.RequestedModel)
            ? RagModelRouter.ResolveModel(family)
            : request.RequestedModel.Trim();
        var stage = NormalizeGenerationStage(request.GenerationStage);
        if (request.RequireCiteable)
        {
            if (request.SkillExecution is null)
                throw new InvalidOperationException("Canonical generation requires an immutable skill execution snapshot.");
            GccV2SkillCatalog.ForStage(request.SkillExecution, stage);
            var capabilities = await _rag.GetCapabilitiesAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("RAG producer capabilities are unavailable; strict execution cannot be negotiated.");
            if (!capabilities.ExecutionVersions.Contains(request.ExecutionVersion, StringComparer.Ordinal)
                || !capabilities.SkillEnvelopeVersions.Contains(request.SkillExecution.EnvelopeVersion, StringComparer.Ordinal)
                || !capabilities.GenerationStages.Contains(stage, StringComparer.Ordinal)
                || !string.Equals(
                    capabilities.SpecialistExecutorVersion,
                    RagProducerCapabilities.RequiredSpecialistExecutorVersion,
                    StringComparison.Ordinal)
                || RagProducerCapabilities.RequiredSpecialists.Any(required =>
                    !capabilities.SpecialistExecutors.Contains(required, StringComparer.Ordinal))
                || capabilities.ToolsAllowed)
                throw new InvalidOperationException(
                    $"RAG producer does not support execution '{request.ExecutionVersion}', skill envelope " +
                    $"'{request.SkillExecution.EnvelopeVersion}', stage '{stage}', and the required tool-free specialist boundary.");
        }
        if (stage == "section" && string.IsNullOrWhiteSpace(request.SectionHeading))
            throw new ArgumentException("sectionHeading is required for section generation.");
        if (stage is "validation" or "finalSynthesis")
        {
            if (string.IsNullOrWhiteSpace(request.DraftContent))
                throw new ArgumentException($"draftContent is required for {stage} generation.");
            if (request.CanonicalBrief is null)
                throw new ArgumentException($"canonicalBrief is required for {stage} generation.");
            if (request.Sources is not { Count: > 0 })
                throw new ArgumentException($"sources are required for {stage} generation.");
            if (string.IsNullOrWhiteSpace(request.ModelPolicyPreset))
                throw new ArgumentException($"modelPolicyPreset is required for {stage} generation.");
            if (string.IsNullOrWhiteSpace(request.ModelPolicyVersion))
                throw new ArgumentException($"modelPolicyVersion is required for {stage} generation.");
            if (string.IsNullOrWhiteSpace(request.RequestedModel))
                throw new ArgumentException($"requestedModel is required for {stage} generation.");
        }

        var partnerRunId = request.PartnerRunId;
        var competitorRunId = request.CompetitorRunId;
        if (!request.RequireCiteable)
        {
            partnerRunId ??= (await PickLatestRunAsync(ownerUserId, CrawlTypes.Partner, ct).ConfigureAwait(false))?.Id;
            competitorRunId ??= (await PickLatestRunAsync(ownerUserId, CrawlTypes.Competitors, ct).ConfigureAwait(false))?.Id;
        }

        if (partnerRunId is null && competitorRunId is null)
        {
            warnings.Add(
                request.RequireCiteable
                    ? "No immutable partner or competitor source run IDs were supplied."
                    : "No partner or competitors crawl runs found for this account. Generate continues with empty research.");
        }

        if (_citeableGenerateEnabled)
        {
            var citeable = await TryCiteableGenerateAsync(
                    intent,
                    topic,
                    entities,
                    templates,
                    partnerRunId,
                    competitorRunId,
                    family,
                    model,
                    request,
                    warnings,
                    ct)
                .ConfigureAwait(false);
            if (citeable is not null)
                return citeable;
            if (stage != "complete" || request.RequireCiteable)
            {
                throw new InvalidOperationException(
                    $"Citeable RAG {stage} generation is unavailable. Verify GEEK_CRAWLER_RAG_URL, " +
                    "GEEK_RAG_GENERATE_ENABLED, and GEEK_RAG_CITEABLE_GENERATE_ENABLED, then retry.");
            }
            warnings.Add("Rag citeable generate unavailable; falling back to GeekAPI one-shot.");
        }

        string? retrievalMode = null;
        if (family == RagRetrievalFamily.Slides)
        {
            if (_graphEnabled)
                retrievalMode = "graph";
            else
                warnings.Add(
                    "GraphRAG soft-disabled (GEEK_RAG_GRAPH_ENABLED=false). Using parent hybrid retrieval for slides/strategy.");
        }

        var (preferParent, preferChild, topK) = family switch
        {
            RagRetrievalFamily.ShortForm => ((bool?)false, (bool?)true, 5),
            RagRetrievalFamily.Battlecard => ((bool?)true, (bool?)false, 8),
            RagRetrievalFamily.Slides => ((bool?)true, (bool?)false, 10),
            _ => ((bool?)true, (bool?)false, 10),
        };

        var partnerQuery = await QueryRunAsync(
            partnerRunId,
            BuildNeed(intent, topic, entities, CrawlTypes.Partner),
            CrawlTypes.Partner,
            topK,
            preferParent,
            preferChild,
            entities,
            retrievalMode,
            warnings,
            ct).ConfigureAwait(false);

        var competitorQuery = await QueryRunAsync(
            competitorRunId,
            BuildNeed(intent, topic, entities, CrawlTypes.Competitors),
            CrawlTypes.Competitors,
            topK,
            preferParent,
            preferChild,
            entities,
            retrievalMode,
            warnings,
            ct).ConfigureAwait(false);

        var partnerPages = partnerQuery.Pages;
        var competitorPages = competitorQuery.Pages;

        if (family == RagRetrievalFamily.ShortForm)
        {
            if (_adTemplateIndexEnabled && templates.Count < 3)
            {
                var fromIndex = await _rag.QueryTemplatesAsync(
                    need: $"{intent}; {topic}",
                    topK: 3,
                    entityTags: entities.Count > 0 ? entities : null,
                    ct: ct).ConfigureAwait(false);
                if (fromIndex is not null)
                {
                    if (!string.IsNullOrWhiteSpace(fromIndex.Warning))
                        warnings.Add(fromIndex.Warning);
                    foreach (var t in fromIndex.Templates)
                    {
                        if (templates.Any(x => string.Equals(x.Id, t.Id, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        templates.Add(new RagAdTemplateDto
                        {
                            Id = t.Id,
                            Name = t.Name,
                            Channel = t.Channel,
                            Framework = t.Framework,
                            Body = t.Body,
                        });
                        if (templates.Count >= 3) break;
                    }
                }
            }

            if (templates.Count == 0)
            {
                warnings.Add(
                    "No ad templates supplied and none retrieved from Rag index. Short-form continues without few-shot exemplars.");
            }
        }

        var sources = BuildSources(partnerPages, competitorPages, entities);
        IReadOnlyList<RagThemeSourceDto>? themeSources = null;
        if (family == RagRetrievalFamily.Slides)
        {
            themeSources = MapRagThemes(partnerQuery.Themes.Concat(competitorQuery.Themes).ToList());
            if (themeSources.Count == 0)
                themeSources = BuildThemeSources(partnerPages, competitorPages, entities);
        }

        var effectiveMode = retrievalMode
                            ?? partnerQuery.Retrieval
                            ?? competitorQuery.Retrieval
                            ?? "hybrid";

        return family switch
        {
            RagRetrievalFamily.Battlecard => await WriteBattlecardAsync(
                intent, topic, entities, partnerPages, competitorPages, sources, warnings, model, effectiveMode, ct)
                .ConfigureAwait(false),
            RagRetrievalFamily.ShortForm => await WriteShortFormAsync(
                intent, topic, entities, templates, partnerPages, competitorPages, sources, warnings, model, effectiveMode, ct)
                .ConfigureAwait(false),
            RagRetrievalFamily.Slides => await WriteSlidesAsync(
                intent, topic, entities, partnerPages, competitorPages, sources, themeSources, warnings, model, effectiveMode, ct)
                .ConfigureAwait(false),
            _ => await WriteLongFormAsync(
                intent, topic, entities, partnerPages, competitorPages, sources, warnings, model, effectiveMode, ct)
                .ConfigureAwait(false),
        };
    }

    public async Task<GeekCrawlerRagTemplateIndexResult?> IndexAdTemplatesAsync(
        IReadOnlyList<RagAdTemplateDto> templates,
        CancellationToken ct)
    {
        if (!_rag.IsEnabled || !_adTemplateIndexEnabled)
            return new GeekCrawlerRagTemplateIndexResult
            {
                Upserted = 0,
                Warning = "Ad template index soft-disabled or RAG unavailable.",
            };

        var mapped = templates
            .Select(t => new GeekCrawlerRagTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Channel = t.Channel,
                Framework = t.Framework,
                Body = t.Body,
            })
            .ToList();
        return await _rag.IndexTemplatesAsync(mapped, ct).ConfigureAwait(false);
    }

    // --- helpers continue below (QueryRunAsync signature changed) ---
    private sealed record SeedQueryResult(
        IReadOnlyList<GccQuoteablePage> Pages,
        IReadOnlyList<GeekCrawlerRagThemeDto> Themes,
        string? Retrieval);

    private async Task<SeedQueryResult> QueryRunAsync(
        Guid? runId,
        string need,
        string crawlType,
        int topK,
        bool? preferParent,
        bool? preferChild,
        IReadOnlyList<string> entities,
        string? retrievalMode,
        List<string> warnings,
        CancellationToken ct)
    {
        if (runId is null)
            return new SeedQueryResult([], [], null);

        var result = await _rag.QueryAsync(
            need,
            runId.Value,
            crawlType: crawlType,
            host: null,
            topK: topK,
            preferParent: preferParent,
            preferChild: preferChild,
            entityNames: entities.Count > 0 ? entities : null,
            retrievalMode: retrievalMode,
            ct: ct).ConfigureAwait(false);

        if (result is null)
        {
            warnings.Add($"RAG query skipped for {crawlType} (client returned null).");
            return new SeedQueryResult([], [], null);
        }

        if (!string.IsNullOrWhiteSpace(result.Warning))
            warnings.Add(result.Warning);

        return new SeedQueryResult(result.Pages, result.Themes, result.Retrieval);
    }

    internal static string BuildNeed(
        string intent,
        string topic,
        IReadOnlyList<string> entities,
        string crawlType)
    {
        var role = crawlType switch
        {
            CrawlTypes.Competitors => "competitor differentiation research",
            _ => "partner tool research",
        };
        var parts = new List<string>
        {
            role,
            $"writing intent: {intent}",
            $"topic: {Truncate(topic, 200)}",
        };
        if (entities.Count > 0)
            parts.Add($"entities: {string.Join(", ", entities.Take(8))}");
        return string.Join("; ", parts);
    }

    private static IReadOnlyList<RagThemeSourceDto> MapRagThemes(IReadOnlyList<GeekCrawlerRagThemeDto> themes)
    {
        return themes
            .Where(t => !string.IsNullOrWhiteSpace(t.Label))
            .Select(t => new RagThemeSourceDto
            {
                Label = t.Label,
                Relationship = t.Relationship,
                Entity = t.Entity,
                Url = t.Url,
            })
            .Take(16)
            .ToList();
    }

    private async Task<GeekCrawlerRunDto?> PickLatestRunAsync(
        string ownerUserId,
        string crawlType,
        CancellationToken ct)
    {
        var runs = await _crawlerRepo.ListRunsForUserAsync(ownerUserId, crawlType, limit: 20, ct)
            .ConfigureAwait(false);
        return runs
            .OrderByDescending(r => StatusRank(r.Status))
            .ThenByDescending(r => r.CompletedAtUtc ?? r.StartedAtUtc ?? r.CreatedAtUtc)
            .FirstOrDefault();
    }

    private static int StatusRank(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "complete" => 3,
            "external" => 2,
            "running" => 1,
            _ => 0,
        };

    private async Task<RagGenerateResponse> WriteLongFormAsync(
        string intent,
        string topic,
        IReadOnlyList<string> entities,
        IReadOnlyList<GccQuoteablePage> partner,
        IReadOnlyList<GccQuoteablePage> competitor,
        IReadOnlyList<RagGenerateSourceDto> sources,
        List<string> warnings,
        string model,
        string retrievalMode,
        CancellationToken ct)
    {
        var system = """
            You write grounded long-form marketing/technical content for a partner ecosystem.
            Use PARTNER research to describe capabilities accurately. Use COMPETITOR research only to
            differentiate — never recommend rival products as CTAs or invent features absent from excerpts.
            Cite sources inline lightly by product/site name when helpful; do not dump URLs in the body.
            Return Markdown only (no JSON wrapper).
            """;
        var user = BuildResearchUserPrompt(intent, topic, entities, partner, competitor)
                   + "\n\nWrite a complete draft for this intent.";

        var (text, modelUsed) = await CompleteAsync(system, user, model, temperature: 0.45, maxTokens: 4096, ct)
            .ConfigureAwait(false);
        return new RagGenerateResponse
        {
            Intent = intent,
            Content = text,
            Sources = sources,
            Warnings = warnings,
            ModelUsed = modelUsed,
            RetrievalMode = retrievalMode,
        };
    }

    private async Task<RagGenerateResponse> WriteShortFormAsync(
        string intent,
        string topic,
        IReadOnlyList<string> entities,
        IReadOnlyList<RagAdTemplateDto> templates,
        IReadOnlyList<GccQuoteablePage> partner,
        IReadOnlyList<GccQuoteablePage> competitor,
        IReadOnlyList<RagGenerateSourceDto> sources,
        List<string> warnings,
        string model,
        string retrievalMode,
        CancellationToken ct)
    {
        var system = """
            You write short-form copy (ads, social, blurbs). Prefer punchy impact points from research.
            When FEW-SHOT AD TEMPLATES are provided, mirror their structure/framework while grounding
            claims in the research excerpts — do not copy trademarks or invent unsupported claims.
            Return JSON only: {"variations":["...","...","..."]} with 2–3 distinct options.
            No rival CTAs.
            """;
        var user = BuildResearchUserPrompt(intent, topic, entities, partner, competitor);
        if (templates.Count > 0)
        {
            user += "\n\nFEW-SHOT AD TEMPLATES (structure exemplars — adapt, do not plagiarize):\n";
            foreach (var t in templates.Take(3))
            {
                user += $"- [{t.Name}] channel={t.Channel ?? "n/a"} framework={t.Framework ?? "n/a"}\n{t.Body}\n\n";
            }
        }

        user += "\n\nProduce 2–3 short variations grounded in the top impact points.";

        var (raw, modelUsed) = await CompleteAsync(system, user, model, temperature: 0.55, maxTokens: 1200, ct)
            .ConfigureAwait(false);
        var variations = ParseVariations(raw);
        if (variations.Count == 0 && !string.IsNullOrWhiteSpace(raw))
            variations = [raw.Trim()];

        var sourceList = sources.ToList();
        foreach (var t in templates.Take(3))
        {
            sourceList.Add(new RagGenerateSourceDto
            {
                Url = $"template://{t.Id}",
                Title = t.Name,
                Entity = t.Channel,
                CrawlType = "ad-template",
                Kind = "template",
            });
        }

        return new RagGenerateResponse
        {
            Intent = intent,
            Variations = variations,
            Content = variations.Count > 0 ? variations[0] : null,
            Sources = sourceList,
            AppliedTemplates = templates.Count > 0 ? templates : null,
            Warnings = warnings,
            ModelUsed = modelUsed,
            RetrievalMode = retrievalMode,
        };
    }

    private async Task<RagGenerateResponse> WriteSlidesAsync(
        string intent,
        string topic,
        IReadOnlyList<string> entities,
        IReadOnlyList<GccQuoteablePage> partner,
        IReadOnlyList<GccQuoteablePage> competitor,
        IReadOnlyList<RagGenerateSourceDto> sources,
        IReadOnlyList<RagThemeSourceDto>? themeSources,
        List<string> warnings,
        string model,
        string retrievalMode,
        CancellationToken ct)
    {
        var system = """
            You write pitch-slide / strategy outlines for partner ecosystem storytelling.
            Prefer theme-level structure (problem → insight → proof → differentiation → ask).
            Return Markdown with ## Slide N: Title headings and 2–4 bullets per slide.
            Use COMPETITOR research only for differentiation — no rival CTAs.
            """;
        var user = BuildResearchUserPrompt(intent, topic, entities, partner, competitor);
        if (themeSources is { Count: > 0 })
        {
            user += "\n\nTHEME RELATIONSHIPS (entity-level hints):\n";
            foreach (var t in themeSources.Take(12))
                user += $"- {t.Label}" + (string.IsNullOrWhiteSpace(t.Relationship) ? "" : $" ({t.Relationship})") + "\n";
        }

        user += "\n\nProduce a 6–10 slide outline for this intent.";

        var (text, modelUsed) = await CompleteAsync(system, user, model, temperature: 0.4, maxTokens: 3000, ct)
            .ConfigureAwait(false);

        return new RagGenerateResponse
        {
            Intent = intent,
            Content = text,
            Sources = sources,
            ThemeSources = themeSources,
            Warnings = warnings,
            ModelUsed = modelUsed,
            RetrievalMode = retrievalMode,
        };
    }

    private async Task<RagGenerateResponse> WriteBattlecardAsync(
        string intent,
        string topic,
        IReadOnlyList<string> entities,
        IReadOnlyList<GccQuoteablePage> partner,
        IReadOnlyList<GccQuoteablePage> competitor,
        IReadOnlyList<RagGenerateSourceDto> sources,
        List<string> warnings,
        string model,
        string retrievalMode,
        CancellationToken ct)
    {
        var system = """
            You write competitive battlecards for sales/enablement.
            Return JSON only with keys:
            partnerSummary (string), competitorSummary (string),
            differentiators (string[]), risks (string[]).
            Ground claims in the provided excerpts. Never invent features.
            """;
        var user = BuildResearchUserPrompt(intent, topic, entities, partner, competitor)
                   + "\n\nProduce a battlecard comparing partner strengths vs competitor approaches.";

        var (raw, modelUsed) = await CompleteAsync(system, user, model, temperature: 0.35, maxTokens: 2000, ct)
            .ConfigureAwait(false);
        var battlecard = ParseBattlecard(raw);

        return new RagGenerateResponse
        {
            Intent = intent,
            Battlecard = battlecard,
            Content = battlecard is null
                ? raw
                : FormatBattlecardMarkdown(battlecard),
            Sources = sources,
            Warnings = warnings,
            ModelUsed = modelUsed,
            RetrievalMode = retrievalMode,
        };
    }

    private async Task<(string Content, string ModelUsed)> CompleteAsync(
        string system,
        string user,
        string model,
        double temperature,
        int maxTokens,
        CancellationToken ct)
    {
        List<ChatMessage> messages;
        if (RagModelRouter.IsReasoningModel(model))
        {
            messages =
            [
                new ChatMessage(ChatRole.User, system + "\n\n" + user),
            ];
        }
        else
        {
            messages =
            [
                new ChatMessage(ChatRole.System, system),
                new ChatMessage(ChatRole.User, user),
            ];
        }

        var request = new ChatCompletionRequest(
            Messages: messages,
            Temperature: temperature,
            MaxOutputTokens: maxTokens,
            Model: model);

        IContentGenerationProvider provider;
        try
        {
            provider = _providers.Get(LlmProviderType.OpenAi);
        }
        catch (Exception ex)
        {
            throw new ContentGenerationException(
                $"The configured provider for model '{model}' is unavailable; no fallback was attempted.", ex);
        }

        try
        {
            var result = await provider.CompleteAsync(request, ct).ConfigureAwait(false);
            return (result.Content?.Trim() ?? "", result.ModelUsed ?? model);
        }
        catch (ContentGenerationException) { throw; }
    }

    private static string BuildResearchUserPrompt(
        string intent,
        string topic,
        IReadOnlyList<string> entities,
        IReadOnlyList<GccQuoteablePage> partner,
        IReadOnlyList<GccQuoteablePage> competitor)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Writing intent: {intent}");
        sb.AppendLine($"Topic: {topic}");
        if (entities.Count > 0)
            sb.AppendLine($"Target entities: {string.Join(", ", entities)}");
        sb.AppendLine();
        sb.AppendLine("PARTNER PAGE EXCERPTS:");
        AppendPages(sb, partner, maxPages: 5, maxParas: 4);
        sb.AppendLine();
        sb.AppendLine("COMPETITOR PAGE EXCERPTS (research only — differentiate; no rival CTAs):");
        AppendPages(sb, competitor, maxPages: 5, maxParas: 4);
        return sb.ToString();
    }

    private static void AppendPages(
        StringBuilder sb,
        IReadOnlyList<GccQuoteablePage> pages,
        int maxPages,
        int maxParas)
    {
        if (pages.Count == 0)
        {
            sb.AppendLine("(none)");
            return;
        }

        foreach (var page in pages.Take(maxPages))
        {
            sb.AppendLine($"[{page.Title}] ({page.Url})");
            foreach (var para in page.Paragraphs.Take(maxParas))
                sb.AppendLine($"- {para}");
        }
    }

    private static IReadOnlyList<RagGenerateSourceDto> BuildSources(
        IReadOnlyList<GccQuoteablePage> partner,
        IReadOnlyList<GccQuoteablePage> competitor,
        IReadOnlyList<string> entities)
    {
        var sources = new List<RagGenerateSourceDto>();
        void Add(IEnumerable<GccQuoteablePage> pages, string crawlType)
        {
            foreach (var page in pages.Take(8))
            {
                var entity = entities.FirstOrDefault(e =>
                    page.Title.Contains(e, StringComparison.OrdinalIgnoreCase)
                    || page.Url.Contains(e.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));
                sources.Add(new RagGenerateSourceDto
                {
                    Url = page.Url,
                    Title = page.Title,
                    Entity = entity,
                    CrawlType = crawlType,
                    Kind = "page",
                    PageId = page.PageId,
                });
            }
        }

        Add(partner, CrawlTypes.Partner);
        Add(competitor, CrawlTypes.Competitors);
        return sources;
    }

    internal static IReadOnlyList<RagThemeSourceDto> BuildThemeSources(
        IReadOnlyList<GccQuoteablePage> partner,
        IReadOnlyList<GccQuoteablePage> competitor,
        IReadOnlyList<string> entities)
    {
        var themes = new List<RagThemeSourceDto>();
        foreach (var e in entities.Take(8))
        {
            themes.Add(new RagThemeSourceDto
            {
                Label = e,
                Relationship = "target-entity",
                Entity = e,
            });
        }

        foreach (var page in partner.Take(4))
        {
            themes.Add(new RagThemeSourceDto
            {
                Label = page.Title,
                Relationship = "partner-theme",
                Entity = entities.FirstOrDefault(),
                Url = page.Url,
            });
        }

        foreach (var page in competitor.Take(4))
        {
            themes.Add(new RagThemeSourceDto
            {
                Label = page.Title,
                Relationship = "competitor-contrast",
                Url = page.Url,
            });
        }

        return themes;
    }

    private static IReadOnlyList<string> NormalizeEntities(IEnumerable<string>? raw)
    {
        if (raw is null) return [];
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in raw)
        {
            var t = (item ?? "").Trim();
            if (t.Length == 0 || t.Length > 80) continue;
            if (!seen.Add(t)) continue;
            list.Add(t);
            if (list.Count >= 12) break;
        }

        return list;
    }

    private static IReadOnlyList<RagAdTemplateDto> NormalizeTemplates(IEnumerable<RagAdTemplateDto>? raw)
    {
        if (raw is null) return [];
        var list = new List<RagAdTemplateDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in raw)
        {
            if (t is null) continue;
            var body = (t.Body ?? "").Trim();
            if (body.Length < 8) continue;
            var id = string.IsNullOrWhiteSpace(t.Id) ? Guid.NewGuid().ToString("N")[..12] : t.Id.Trim();
            if (!seen.Add(id)) continue;
            list.Add(new RagAdTemplateDto
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(t.Name) ? id : t.Name.Trim(),
                Channel = t.Channel?.Trim(),
                Framework = t.Framework?.Trim(),
                Body = Truncate(body, 2000),
            });
            if (list.Count >= 5) break;
        }

        return list;
    }

    internal static IReadOnlyList<string> ParseVariations(string raw)
    {
        var json = ExtractJsonObject(raw);
        if (json is null) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("variations", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return [];
            return arr.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!.Trim())
                .Where(s => s.Length > 0)
                .Take(5)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static RagBattlecardDto? ParseBattlecard(string raw)
    {
        var json = ExtractJsonObject(raw);
        if (json is null) return null;
        try
        {
            return JsonSerializer.Deserialize<RagBattlecardDto>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return raw[start..(end + 1)];
    }

    private static string FormatBattlecardMarkdown(RagBattlecardDto card)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Partner summary");
        sb.AppendLine(card.PartnerSummary);
        sb.AppendLine();
        sb.AppendLine("## Competitor summary");
        sb.AppendLine(card.CompetitorSummary);
        sb.AppendLine();
        sb.AppendLine("## Differentiators");
        foreach (var d in card.Differentiators)
            sb.AppendLine($"- {d}");
        sb.AppendLine();
        sb.AppendLine("## Risks / watch-outs");
        foreach (var r in card.Risks)
            sb.AppendLine($"- {r}");
        return sb.ToString().Trim();
    }

    private async Task<RagGenerateResponse?> TryCiteableGenerateAsync(
        string intent,
        string topic,
        IReadOnlyList<string> entities,
        IReadOnlyList<RagAdTemplateDto> templates,
        Guid? partnerRunId,
        Guid? competitorRunId,
        RagRetrievalFamily family,
        string model,
        RagGenerateRequest request,
        List<string> warnings,
        CancellationToken ct)
    {
        var stage = NormalizeGenerationStage(request.GenerationStage);
        var mappedTemplates = templates
            .Select(t => new GeekCrawlerRagTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Channel = t.Channel,
                Framework = t.Framework,
                Body = t.Body,
            })
            .ToList();

        var result = await _rag.GenerateAsync(
            new GeekCrawlerRagGenerateRequest
            {
                WritingIntent = intent,
                Topic = topic,
                PartnerRunId = partnerRunId?.ToString("D"),
                CompetitorRunId = competitorRunId?.ToString("D"),
                TargetEntities = entities.Count > 0 ? entities : null,
                AdTemplates = mappedTemplates.Count > 0 ? mappedTemplates : null,
                GraphEnabled = _graphEnabled && family == RagRetrievalFamily.Slides,
                GenerationStage = NormalizeGenerationStage(request.GenerationStage),
                Outline = request.Outline?
                    .Select(s => new GeekCrawlerRagOutlineSectionDto
                    {
                        Key = s.Key,
                        Heading = s.Heading,
                        Brief = s.Brief,
                        EvidenceIds = s.EvidenceIds,
                    })
                    .ToList(),
                SectionKey = request.SectionKey,
                SectionHeading = request.SectionHeading,
                SectionBrief = request.SectionBrief,
                CompletedSectionSummaries = request.CompletedSectionSummaries,
                DraftContent = request.DraftContent,
                Sources = request.Sources?
                    .Select(s => new GeekCrawlerRagGenerateSourceDto
                    {
                        PageId = s.PageId,
                        Url = s.Url,
                        Title = s.Title,
                        Entity = s.Entity,
                        CrawlType = s.CrawlType,
                        Kind = s.Kind,
                    })
                    .ToList(),
                CanonicalBrief = request.CanonicalBrief,
                ModelPolicyPreset = request.ModelPolicyPreset,
                ModelPolicyVersion = request.ModelPolicyVersion,
                StageModelOverrides = request.StageModelOverrides,
                ExecutionVersion = request.ExecutionVersion,
                AttemptId = request.AttemptId,
                SkillExecution = request.SkillExecution,
            },
            ct).ConfigureAwait(false);

        if (result is null)
            return null;
        var returnedModel = result.Provenance?.ModelUsed ?? result.ModelUsed;
        if (request.RequireCiteable
            && !string.IsNullOrWhiteSpace(request.RequestedModel)
            && string.IsNullOrWhiteSpace(returnedModel))
            throw new InvalidOperationException(
                "RAG response omitted model provenance; the requested model cannot be verified.");
        if (!string.IsNullOrWhiteSpace(request.RequestedModel)
            && !string.IsNullOrWhiteSpace(returnedModel)
            && !string.Equals(request.RequestedModel, returnedModel, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"RAG returned model '{returnedModel}' after '{request.RequestedModel}' was explicitly requested. " +
                "Silent model substitution is not allowed.");
        }
        if (!string.IsNullOrWhiteSpace(request.ModelPolicyVersion)
            && !string.Equals(
                request.ModelPolicyVersion,
                result.Provenance?.ModelPolicyVersion,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"RAG response did not confirm model policy version '{request.ModelPolicyVersion}'.");
        if (stage == "validation"
            && (!string.Equals(
                    request.ModelPolicyPreset,
                    result.Provenance?.ModelPolicyPreset,
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(result.Provenance?.PromptVersion)
                || string.IsNullOrWhiteSpace(result.Provenance?.Retrieval)))
            throw new InvalidOperationException(
                "RAG validation response returned incomplete policy or generation provenance.");
        if (request.RequireCiteable
            && !string.Equals(
                stage,
                result.Provenance?.GenerationStage,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"RAG response did not confirm generation stage '{stage}'.");
        if (request.RequireCiteable
            && (!string.Equals(request.ExecutionVersion, result.Provenance?.ExecutionVersion, StringComparison.Ordinal)
                || !string.Equals(request.AttemptId, result.Provenance?.AttemptId, StringComparison.Ordinal)
                || !string.Equals(request.SkillExecution?.SnapshotHash, result.Provenance?.Skills?.SnapshotHash, StringComparison.Ordinal)
                || !string.Equals(stage, result.Provenance?.Skills?.Stage, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "RAG response did not confirm execution version, attempt ID, skill snapshot, and stage.");
        if (stage == "validation" && result.Validation is null)
            throw new InvalidOperationException(
                "RAG validation response was missing or malformed; validation fails closed.");
        if (stage == "validation")
            ValidateTypedValidation(result.Validation!);

        foreach (var w in result.Warnings)
        {
            if (!string.IsNullOrWhiteSpace(w))
                warnings.Add(w);
        }

        RagBattlecardDto? battlecard = null;
        if (result.Battlecard is not null)
        {
            battlecard = new RagBattlecardDto
            {
                PartnerSummary = result.Battlecard.PartnerSummary,
                CompetitorSummary = result.Battlecard.CompetitorSummary,
                Differentiators = result.Battlecard.Differentiators,
                Risks = result.Battlecard.Risks,
            };
        }

        IReadOnlyList<RagThemeSourceDto>? themeSources = null;
        if (family == RagRetrievalFamily.Slides && result.Themes.Count > 0)
        {
            themeSources = result.Themes
                .Select(t => new RagThemeSourceDto
                {
                    Label = t.Label,
                    Relationship = t.Relationship,
                    Entity = t.Entity,
                    Url = t.Url,
                })
                .ToList();
        }

        return new RagGenerateResponse
        {
            Intent = intent,
            Content = result.Content
                      ?? (battlecard is not null ? FormatBattlecardMarkdown(battlecard) : null)
                      ?? (result.Variations is { Count: > 0 } ? result.Variations[0] : null),
            Variations = result.Variations,
            Battlecard = battlecard,
            Sources = result.Sources
                .Select(s => new RagGenerateSourceDto
                {
                    Url = s.Url,
                    Title = s.Title,
                    Entity = s.Entity,
                    CrawlType = s.CrawlType,
                    Kind = s.Kind,
                    PageId = s.PageId,
                })
                .ToList(),
            Citations = result.Citations
                .Select(c => new RagCitationDto
                {
                    PageId = c.PageId,
                    Url = c.Url,
                    Title = c.Title,
                    SectionTitle = c.SectionTitle,
                    Quote = c.Quote,
                    CrawlType = c.CrawlType,
                })
                .ToList(),
            ThemeSources = themeSources,
            Outline = result.Outline?
                .Select(s => new RagOutlineSectionDto
                {
                    Key = s.Key,
                    Heading = s.Heading,
                    Brief = s.Brief,
                    EvidenceIds = s.EvidenceIds,
                })
                .ToList(),
            AppliedTemplates = family == RagRetrievalFamily.ShortForm && templates.Count > 0
                ? templates.ToList()
                : null,
            Warnings = warnings,
            EvidenceWarnings = result.EvidenceWarnings,
            ModelUsed = returnedModel,
            RetrievalMode = result.Provenance?.Retrieval ?? result.Retrieval,
            PromptVersion = result.Provenance?.PromptVersion ?? "rag-generate/1",
            Provenance = result.Provenance is null
                ? null
                : new RagGenerateProvenanceDto
                {
                    GenerationStage = result.Provenance.GenerationStage,
                    ModelUsed = result.Provenance.ModelUsed,
                    ModelPolicyPreset = result.Provenance.ModelPolicyPreset,
                    ModelPolicyVersion = result.Provenance.ModelPolicyVersion,
                    PromptVersion = result.Provenance.PromptVersion,
                    Retrieval = result.Provenance.Retrieval,
                    EvidenceIds = result.Provenance.EvidenceIds,
                    SpecialistExecutor = result.Provenance.SpecialistExecutor,
                    SpecialistExecutorVersion = result.Provenance.SpecialistExecutorVersion,
                    ExecutionVersion = result.Provenance.ExecutionVersion,
                    AttemptId = result.Provenance.AttemptId,
                    Skills = result.Provenance.Skills is null
                        ? null
                        : new RagSkillProvenanceDto
                        {
                            EnvelopeVersion = result.Provenance.Skills.EnvelopeVersion,
                            CatalogVersion = result.Provenance.Skills.CatalogVersion,
                            SnapshotHash = result.Provenance.Skills.SnapshotHash,
                            Stage = result.Provenance.Skills.Stage,
                            SkillVersions = result.Provenance.Skills.SkillVersions,
                        },
                },
            Validation = result.Validation is null
                ? null
                : new RagValidationDto
                {
                    Approved = result.Validation.Approved,
                    Issues = result.Validation.Issues.Select(i => new RagValidationIssueDto
                    {
                        SectionTitle = i.SectionTitle,
                        Category = MapValidationCategory(i.Category),
                        Detail = i.Detail,
                        RepairInstruction = i.RepairInstruction,
                    }).ToList(),
                    Strengths = result.Validation.Strengths,
                    UnsupportedClaimCount = result.Validation.UnsupportedClaimCount,
                    BriefAlignmentScore = result.Validation.BriefAlignmentScore,
                    EvidenceCoverageScore = result.Validation.EvidenceCoverageScore,
                    UsefulnessScore = result.Validation.UsefulnessScore,
                    OriginalityScore = result.Validation.OriginalityScore,
                    BrandAlignmentScore = result.Validation.BrandAlignmentScore,
                },
        };
    }

    private static void ValidateTypedValidation(GeekCrawlerRagValidation validation)
    {
        var unsupportedIssues = validation.Issues.Count(
            issue => issue.Category == GeekCrawlerRagValidationIssueCategory.UnsupportedClaim);
        if (validation.UnsupportedClaimCount < unsupportedIssues
            || (validation.UnsupportedClaimCount > 0 && validation.Approved)
            || (!validation.Approved && validation.Issues.Count == 0))
            throw new InvalidOperationException(
                "RAG validation response was internally inconsistent; validation fails closed.");
    }

    private static RagValidationIssueCategory MapValidationCategory(
        GeekCrawlerRagValidationIssueCategory category) => category switch
    {
        GeekCrawlerRagValidationIssueCategory.UnsupportedClaim => RagValidationIssueCategory.UnsupportedClaim,
        GeekCrawlerRagValidationIssueCategory.SourceConflict => RagValidationIssueCategory.SourceConflict,
        GeekCrawlerRagValidationIssueCategory.BriefAlignment => RagValidationIssueCategory.BriefAlignment,
        GeekCrawlerRagValidationIssueCategory.BrandVoice => RagValidationIssueCategory.BrandVoice,
        GeekCrawlerRagValidationIssueCategory.OriginalityRepetition => RagValidationIssueCategory.OriginalityRepetition,
        GeekCrawlerRagValidationIssueCategory.Usefulness => RagValidationIssueCategory.Usefulness,
        GeekCrawlerRagValidationIssueCategory.Cta => RagValidationIssueCategory.Cta,
        GeekCrawlerRagValidationIssueCategory.SeoGeo => RagValidationIssueCategory.SeoGeo,
        GeekCrawlerRagValidationIssueCategory.ContentTypeRequirements => RagValidationIssueCategory.ContentTypeRequirements,
        _ => throw new InvalidOperationException(
            $"Unsupported RAG validation issue category '{category}'."),
    };

    private static bool ParseEnabledFlag(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        return raw.Trim() switch
        {
            "0" or "false" or "False" or "FALSE" or "no" or "off" => false,
            _ => true,
        };
    }

    internal static string NormalizeGenerationStage(string? raw)
    {
        var stage = raw?.Trim().ToLowerInvariant();
        return stage switch
        {
            "outline" or "section" or "repair" or "validation" or "complete" => stage,
            "finalsynthesis" or "final-synthesis" => "finalSynthesis",
            _ => "complete",
        };
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
