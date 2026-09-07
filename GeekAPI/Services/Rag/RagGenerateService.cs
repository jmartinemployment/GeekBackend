using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
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
        var model = RagModelRouter.ResolveModel(family);

        var partnerRun = await PickLatestRunAsync(ownerUserId, CrawlTypes.Partner, ct).ConfigureAwait(false);
        var competitorRun = await PickLatestRunAsync(ownerUserId, CrawlTypes.Competitors, ct).ConfigureAwait(false);

        if (partnerRun is null && competitorRun is null)
        {
            warnings.Add(
                "No partner or competitors crawl runs found for this account. Generate continues with empty research.");
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
            partnerRun,
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
            competitorRun,
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
        GeekCrawlerRunDto? run,
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
        if (run is null)
            return new SeedQueryResult([], [], null);

        var result = await _rag.QueryAsync(
            need,
            run.Id,
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
            _logger.LogWarning(ex, "OpenAI provider unavailable; using default LLM provider.");
            provider = _providers.GetDefault();
        }

        try
        {
            var result = await provider.CompleteAsync(request, ct).ConfigureAwait(false);
            return (result.Content?.Trim() ?? "", result.ModelUsed ?? model);
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "OpenAI generate failed for model {Model}; retrying with default provider/model.", model);
            var fallback = _providers.GetDefault();
            var fallbackRequest = request with { Model = null };
            var result = await fallback.CompleteAsync(fallbackRequest, ct).ConfigureAwait(false);
            return (result.Content?.Trim() ?? "", result.ModelUsed ?? "default");
        }
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

    private static bool ParseEnabledFlag(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        return raw.Trim() switch
        {
            "0" or "false" or "False" or "FALSE" or "no" or "off" => false,
            _ => true,
        };
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
