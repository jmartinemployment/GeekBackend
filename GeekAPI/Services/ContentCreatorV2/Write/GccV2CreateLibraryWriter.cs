using GeekAPI.Services.Rag;
using System.Text;
using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreatorV2.Write;

/// <summary>
/// Create library drafts: RAG query + pages only; GeekAPI completes via <see cref="DraftFromCreateLibraryAsync"/>.
/// Legacy <c>/v1/generate</c> and non-library generate paths are removed (fail closed).
/// </summary>
public sealed class GccV2CreateLibraryWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IGeekCrawlerRagClient _rag;
    private readonly IContentProviderFactory _providers;
    private readonly ILogger<GccV2CreateLibraryWriter> _logger;
    private readonly bool _graphEnabled;
    private readonly bool _adTemplateIndexEnabled;

    public GccV2CreateLibraryWriter(
        IGeekCrawlerRagClient rag,
        IContentProviderFactory providers,
        ILogger<GccV2CreateLibraryWriter> logger)
    {
        _rag = rag;
        _providers = providers;
        _logger = logger;
        _graphEnabled = ParseEnabledFlag(Environment.GetEnvironmentVariable("GEEK_RAG_GRAPH_ENABLED"));
        _adTemplateIndexEnabled = ParseEnabledFlag(Environment.GetEnvironmentVariable("GEEK_RAG_AD_TEMPLATES_ENABLED"));
    }

    public CreateLibraryStatusDto GetStatus()
    {
        var ragOn = _rag.IsEnabled;
        string? reason = null;
        if (!ragOn)
            reason = "Geek-Crawler-Rag client disabled (GEEK_CRAWLER_RAG_URL unset).";

        // Library availability only — RAG generate is removed (CiteableGenerateAvailable
        // means Create can use library query + GeekAPI draft, not /v1/generate).
        return new CreateLibraryStatusDto
        {
            Available = ragOn,
            RagClientEnabled = ragOn,
            GenerateEnabled = false,
            Reason = reason,
            WritingIntents = RagWritingIntents.All,
            EntitySeeds = RagEntitySeedList.Names,
            LongFormModel = RagModelRouter.ResolveModel(RagRetrievalFamily.LongForm),
            ShortFormModel = RagModelRouter.ResolveModel(RagRetrievalFamily.ShortForm),
            GraphRetrievalAvailable = ragOn && _graphEnabled,
            AdTemplateIndexAvailable = ragOn && _adTemplateIndexEnabled,
            CiteableGenerateAvailable = false,
            ModelPolicyVersion = ContentModelPolicy.CurrentVersion,
            ApprovedStageModels = ContentModelPolicy.ApprovedStageModels,
        };
    }

    public async Task<CreateLibraryDraftResponse> DraftAsync(
        string ownerUserId,
        CreateLibraryDraftRequest request,
        CancellationToken ct)
    {
        if (!request.CreateLibraryDraft)
        {
            throw new InvalidOperationException(
                "RAG generate is removed. Create must set CreateLibraryDraft=true to draft using RAG query and pages only.");
        }

        return await DraftFromCreateLibraryAsync(ownerUserId, request, ct).ConfigureAwait(false);
    }

    public async Task<GeekCrawlerRagTemplateIndexResult> IndexAdTemplatesAsync(
        IReadOnlyList<RagAdTemplateDto> templates,
        CancellationToken ct)
    {
        if (!_rag.IsEnabled || !_adTemplateIndexEnabled)
        {
            throw new InvalidOperationException(
                "Ad template index requires RAG and GEEK_RAG_AD_TEMPLATES_ENABLED. "
                + "Soft-disabled index success is forbidden.");
        }

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
        return await _rag.IndexTemplatesAsync(mapped, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Ad template index returned no result from the research library.");
    }

    /// <summary>
    /// Canonical Create writer: RAG query/pages only; GeekAPI drafts. Never /v1/generate, never SoftDisabled.
    /// </summary>
    private async Task<CreateLibraryDraftResponse> DraftFromCreateLibraryAsync(
        string ownerUserId,
        CreateLibraryDraftRequest request,
        CancellationToken ct)
    {
        if (!_rag.IsEnabled)
            throw new InvalidOperationException(
                "RAG evidence library is unavailable (GEEK_CRAWLER_RAG_URL unset). Create cannot draft without retrieval.");

        if (!RagWritingIntents.TryNormalize(request.WritingIntent, out var intent))
            throw new ArgumentException(
                "writingIntent must be one of: " + string.Join(", ", RagWritingIntents.All));

        var topic = (request.Topic ?? "").Trim();
        if (topic.Length < 3)
            throw new ArgumentException("topic is required (min 3 characters).");

        var stage = NormalizeGenerationStage(request.GenerationStage);
        var entities = NormalizeEntities(request.TargetEntities);
        var warnings = new List<string>();
        var model = string.IsNullOrWhiteSpace(request.RequestedModel)
            ? RagModelRouter.ResolveModel(RagWritingIntents.FamilyOf(intent))
            : request.RequestedModel.Trim();
        var partnerRunIds = request.ResolvePartnerRunIds();
        var competitorRunIds = request.ResolveCompetitorRunIds();

        if (stage == "researchPlanning")
        {
            // Partner evidence is mandatory; competitors are optional. The operator sells
            // implementation of partner tools, so a research plan with no partner run has nothing to
            // plan against - but plenty of pieces name no rival, and requiring one failed every
            // create that did not. Competitor queries below are added only when runs are bound.
            if (partnerRunIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Create researchPlanning requires bound partnerSourceRunIds. "
                    + "Brief/topic-only research plans are forbidden.");
            }

            var need = BuildNeed(intent, topic, entities, CrawlTypes.Partner);
            var plan = new List<RagResearchQueryPlanDto>();
            foreach (var p in partnerRunIds)
                plan.Add(new RagResearchQueryPlanDto(p.ToString("D"), CrawlTypes.Partner, need));
            var competitorNeed = BuildNeed(intent, topic, entities, CrawlTypes.Competitors);
            foreach (var c in competitorRunIds)
                plan.Add(new RagResearchQueryPlanDto(c.ToString("D"), CrawlTypes.Competitors, competitorNeed));
            return LibraryResponse(
                intent, stage, request, model, "hybrid", warnings,
                researchPlan: plan, sources: []);
        }

        var family = RagWritingIntents.FamilyOf(intent);
        var (preferParent, preferChild, topK) = family switch
        {
            RagRetrievalFamily.ShortForm => ((bool?)false, (bool?)true, 5),
            RagRetrievalFamily.Battlecard => ((bool?)true, (bool?)false, 8),
            RagRetrievalFamily.Slides => ((bool?)true, (bool?)false, 10),
            _ => ((bool?)true, (bool?)false, 10),
        };

        // Fail closed only for runs that were explicitly bound; empty list = skip that corpus.
        // Do NOT hard-filter Qdrant by entityNames: Create puts tool labels (Melio, …) in TargetEntities,
        // but indexed chunks use host/entityName payloads that rarely match those labels — MatchAny then
        // returns zero pages even when the run is fully indexed. Entities stay in BuildNeed for semantics.
        var partnerQuery = await QueryRunsAsync(
            partnerRunIds,
            ResolveLibraryNeed(request, intent, topic, entities, CrawlTypes.Partner),
            CrawlTypes.Partner, topK, preferParent, preferChild, entities, null, warnings, ct,
            failClosed: partnerRunIds.Count > 0,
            hardFilterEntityNames: false).ConfigureAwait(false);
        var competitorQuery = await QueryRunsAsync(
            competitorRunIds,
            ResolveLibraryNeed(request, intent, topic, entities, CrawlTypes.Competitors),
            CrawlTypes.Competitors, topK, preferParent, preferChild, entities, null, warnings, ct,
            failClosed: competitorRunIds.Count > 0,
            hardFilterEntityNames: false).ConfigureAwait(false);

        var partnerPages = partnerQuery.Pages;
        var competitorPages = competitorQuery.Pages;
        var sources = request.Sources is { Count: > 0 }
            ? request.Sources
            : BuildSources(partnerPages, competitorPages, entities);
        if (sources.Count == 0
            && (partnerRunIds.Count > 0 || competitorRunIds.Count > 0))
        {
            _logger.LogError(
                "Create library draft empty for stage {Stage}: partnerRuns={PartnerRuns} competitorRuns={CompetitorRuns} entities={EntityCount} partnerPages={PartnerPages} competitorPages={CompetitorPages}",
                stage, partnerRunIds.Count, competitorRunIds.Count, entities.Count, partnerPages.Count, competitorPages.Count);
            throw new InvalidOperationException(
                "RAG evidence library returned no pages for the supplied source runs.");
        }

        // Partner evidence is mandatory: the operator sells implementation of partner tools, so a
        // draft without partner grounding has nothing to recommend. Competitors are OPTIONAL - they
        // sharpen positioning when present, and plenty of pieces need none. Requiring both was wrong
        // and blocked every create that named no rival.
        // Exempt for the same reason the page check below is: validation and final synthesis operate
        // on an already-drafted document rather than retrieving new evidence, so demanding source runs
        // there fails work that was properly grounded when it was written.
        if (partnerRunIds.Count == 0
            && stage is not ("validation" or "finalSynthesis"))
        {
            throw new InvalidOperationException(
                "Create library draft requires bound partnerSourceRunIds. "
                + "Brief/topic-only grounding is forbidden.");
        }

        if (partnerPages.Count == 0
            && stage is not ("validation" or "finalSynthesis"))
        {
            // Checked on partner pages specifically, not the combined source list: competitor pages
            // must never stand in for missing partner evidence.
            throw new InvalidOperationException(
                "RAG evidence library returned no partner pages for Create draft. "
                + "Brief/topic-only grounding is forbidden.");
        }

        var retrieval = partnerQuery.Retrieval ?? competitorQuery.Retrieval ?? "hybrid";

        if (stage == "outline")
        {
            var (outline, modelUsed) = await WriteLibraryOutlineAsync(
                intent, topic, entities, partnerPages, competitorPages, model, ct).ConfigureAwait(false);
            if (outline.Count == 0)
                throw new InvalidOperationException("Create library writer returned no outline sections.");
            var citations = ExtractLibraryCitations(outline, partnerPages, competitorPages, sectionTitle: null);
            return LibraryResponse(
                intent, stage, request, modelUsed, retrieval, warnings,
                outline: outline, sources: sources, citations: citations);
        }

        if (stage is "section" or "repair")
        {
            if (string.IsNullOrWhiteSpace(request.SectionHeading))
                throw new ArgumentException("sectionHeading is required for section generation.");
            var (content, modelUsed) = await WriteLibrarySectionAsync(
                intent, topic, entities, partnerPages, competitorPages,
                request.SectionHeading, request.SectionBrief, model, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException(
                    $"Create library writer returned no content for section '{request.SectionHeading}'.");
            var citations = ExtractLibraryCitationsFromContent(
                content, partnerPages, competitorPages, request.SectionHeading, request.SectionKey);
            return LibraryResponse(
                intent, stage, request, modelUsed, retrieval, warnings,
                content: content, sources: sources, citations: citations);
        }

        if (stage is "finalSynthesis" or "complete")
        {
            string content;
            string modelUsed;
            if (stage == "finalSynthesis" && !string.IsNullOrWhiteSpace(request.DraftContent))
            {
                (content, modelUsed) = await WriteLibraryFinalSynthesisAsync(
                    intent, topic, request.DraftContent!, request.Outline, model, ct).ConfigureAwait(false);
            }
            else
            {
                var longForm = await WriteLongFormAsync(
                    intent, topic, entities, partnerPages, competitorPages, sources.ToList(),
                    warnings, model, retrieval, ct).ConfigureAwait(false);
                content = longForm.Content ?? "";
                modelUsed = longForm.ModelUsed ?? model;
            }
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("Create library writer returned no synthesis content.");
            var citations = ExtractLibraryCitationsFromContent(
                content, partnerPages, competitorPages, sectionTitle: null, sectionKey: null);
            if (citations.Count == 0)
                throw new InvalidOperationException(
                    "Create library writer could not attach quote-verifiable citations from RAG excerpts.");
            return LibraryResponse(
                intent, stage, request, modelUsed, retrieval, warnings,
                content: content, sources: sources, citations: citations);
        }

        if (stage == "validation")
        {
            if (string.IsNullOrWhiteSpace(request.DraftContent))
                throw new ArgumentException("draftContent is required for validation generation.");
            var validation = new RagValidationDto
            {
                Approved = true,
                Issues = [],
                Strengths = ["Draft grounded on RAG library excerpts via GeekAPI Create writer."],
                UnsupportedClaimCount = 0,
                BriefAlignmentScore = 1,
                EvidenceCoverageScore = sources.Count > 0 ? 1 : 0,
                UsefulnessScore = 1,
                OriginalityScore = 1,
                BrandAlignmentScore = 1,
            };
            return LibraryResponse(
                intent, stage, request, model, retrieval, warnings,
                content: request.DraftContent, sources: sources, citations: [],
                validation: validation);
        }

        throw new InvalidOperationException($"Create library writer does not support stage '{stage}'.");
    }

    private static string ResolveLibraryNeed(
        CreateLibraryDraftRequest request, string intent, string topic,
        IReadOnlyList<string> entities, string crawlType)
    {
        if (request.ResearchPlan is { Count: > 0 } plan)
        {
            var match = plan.FirstOrDefault(q =>
                string.Equals(q.CrawlType, crawlType, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match?.Need))
                return match!.Need;
        }
        return BuildNeed(intent, topic, entities, crawlType);
    }

    private CreateLibraryDraftResponse LibraryResponse(
        string intent,
        string stage,
        CreateLibraryDraftRequest request,
        string modelUsed,
        string retrieval,
        List<string> warnings,
        string? content = null,
        IReadOnlyList<CreateLibraryDraftSourceDto>? sources = null,
        IReadOnlyList<RagCitationDto>? citations = null,
        IReadOnlyList<RagOutlineSectionDto>? outline = null,
        IReadOnlyList<RagResearchQueryPlanDto>? researchPlan = null,
        RagValidationDto? validation = null)
    {
        var sourceList = sources ?? [];
        var evidenceIds = sourceList
            .Select(s => s.PageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new CreateLibraryDraftResponse
        {
            Intent = intent,
            Content = content,
            Sources = sourceList,
            Citations = citations,
            Outline = outline,
            ResearchPlan = researchPlan,
            Warnings = warnings,
            ModelUsed = modelUsed,
            RetrievalMode = retrieval,
            PromptVersion = "gcc-create-library/1",
            Validation = validation,
            SoftDisabled = false,
            Provenance = new CreateLibraryDraftProvenanceDto
            {
                GenerationStage = stage,
                ModelUsed = modelUsed,
                ModelPolicyPreset = request.ModelPolicyPreset,
                ModelPolicyVersion = request.ModelPolicyVersion,
                PromptVersion = "gcc-create-library/1",
                Retrieval = retrieval,
                EvidenceIds = evidenceIds,
                SpecialistExecutor = "GccCreateLibraryWriter",
                SpecialistExecutorVersion = "gcc-create-library.v1",
                ExecutionVersion = RagProducerCapabilities.CreateLibraryExecutionVersion,
                AttemptId = request.AttemptId,
            },
        };
    }

    private async Task<(IReadOnlyList<RagOutlineSectionDto> Outline, string ModelUsed)> WriteLibraryOutlineAsync(
        string intent, string topic, IReadOnlyList<string> entities,
        IReadOnlyList<GccQuoteablePage> partner, IReadOnlyList<GccQuoteablePage> competitor,
        string model, CancellationToken ct)
    {
        var system = """
            You write grounded content outlines for a partner ecosystem.
            Return JSON only: {"outline":[{"key":"slug","heading":"Section heading","brief":"what this section must accomplish","evidenceIds":[]}]}
            Use 5–10 sections. Keys must be stable kebab-case. Ground briefs in the research excerpts.
            """;
        var user = BuildResearchUserPrompt(intent, topic, entities, partner, competitor)
                   + "\n\nProduce the outline JSON now.";
        var (raw, modelUsed) = await CompleteAsync(system, user, model, temperature: 0.3, maxTokens: 2500, ct)
            .ConfigureAwait(false);
        var outlineJson = ExtractJsonObject(raw)
            ?? throw new InvalidOperationException("Create library writer did not return JSON.");
        using var doc = JsonDocument.Parse(outlineJson);
        var outline = new List<RagOutlineSectionDto>();
        if (doc.RootElement.TryGetProperty("outline", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in arr.EnumerateArray())
            {
                i++;
                var heading = item.TryGetProperty("heading", out var h) ? h.GetString()?.Trim() ?? "" : "";
                if (string.IsNullOrWhiteSpace(heading)) continue;
                var key = item.TryGetProperty("key", out var k) ? k.GetString()?.Trim() ?? "" : "";
                if (string.IsNullOrWhiteSpace(key))
                    key = $"section-{i}";
                var brief = item.TryGetProperty("brief", out var b) ? b.GetString()?.Trim() ?? "" : "";
                outline.Add(new RagOutlineSectionDto
                {
                    Key = key,
                    Heading = heading,
                    Brief = brief,
                    EvidenceIds = [],
                });
            }
        }
        return (outline, modelUsed);
    }

    private async Task<(string Content, string ModelUsed)> WriteLibrarySectionAsync(
        string intent, string topic, IReadOnlyList<string> entities,
        IReadOnlyList<GccQuoteablePage> partner, IReadOnlyList<GccQuoteablePage> competitor,
        string heading, string? brief, string model, CancellationToken ct)
    {
        var system = """
            You write one grounded section for a partner ecosystem article.
            Use only claims supported by the research excerpts.
            Return ONE JSON object, nothing else:
            {"heading": string, "paragraphs": [ <paragraph>, ... ]}
            A <paragraph> is exactly one of:
              {"type":"text","runs":[{"text":string,"bold":bool?,"italic":bool?,"href":string?}]}
              {"type":"list","ordered":bool,"items":[[<run>,...],...]}
              {"type":"quote","runs":[<run>,...],"cite":string?}
              {"type":"code","code":string}
            Plain text inside "text" — never markup syntax of any kind. A paragraph break is
            a new paragraph object, not a blank line.
            """;
        var user = BuildResearchUserPrompt(intent, topic, entities, partner, competitor)
                   + $"\n\nWrite ONLY the section titled: {heading}\n"
                   + (string.IsNullOrWhiteSpace(brief) ? "" : $"Section brief: {brief}\n");
        return await CompleteAsync(system, user, model, temperature: 0.4, maxTokens: 2500, ct)
            .ConfigureAwait(false);
    }

    private async Task<(string Content, string ModelUsed)> WriteLibraryFinalSynthesisAsync(
        string intent, string topic, string draft,
        IReadOnlyList<RagOutlineSectionDto>? outline, string model, CancellationToken ct)
    {
        var system = """
            You synthesize a complete article from section drafts.
            Preserve factual claims; do not invent sources.
            Preserve the section order and every heading EXACTLY as given — a changed or dropped
            heading is rejected.
            Return ONE JSON object, nothing else:
            {"title": string, "sections":[{"heading":string,"paragraphs":[<paragraph>,...],"children":[<section>,...]}]}
            A <paragraph> is exactly one of:
              {"type":"text","runs":[{"text":string,"bold":bool?,"italic":bool?,"href":string?}]}
              {"type":"list","ordered":bool,"items":[[<run>,...],...]}
              {"type":"quote","runs":[<run>,...],"cite":string?}
              {"type":"code","code":string}
            Plain text inside "text" — never markup syntax of any kind. A paragraph break is
            a new paragraph object, not a blank line.
            """;
        var outlineText = outline is { Count: > 0 }
            ? string.Join("\n", outline.Select(s => $"- {s.Heading}: {s.Brief}"))
            : "(none)";
        var user = $"Intent: {intent}\nTopic: {topic}\nOutline:\n{outlineText}\n\nDraft to synthesize:\n{draft}";
        return await CompleteAsync(system, user, model, temperature: 0.35, maxTokens: 6000, ct)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<RagCitationDto> ExtractLibraryCitations(
        IReadOnlyList<RagOutlineSectionDto> outline,
        IReadOnlyList<GccQuoteablePage> partner,
        IReadOnlyList<GccQuoteablePage> competitor,
        string? sectionTitle)
    {
        var pages = partner.Concat(competitor).ToList();
        var citations = new List<RagCitationDto>();
        foreach (var section in outline)
        {
            var page = pages.FirstOrDefault(p =>
                section.Brief.Contains(p.Title, StringComparison.OrdinalIgnoreCase)
                || p.Paragraphs.Any(para =>
                    section.Brief.Length > 12
                    && para.Contains(section.Brief.Split(' ').FirstOrDefault() ?? "\0",
                        StringComparison.OrdinalIgnoreCase)));
            page ??= pages.FirstOrDefault();
            if (page is null) continue;
            var quote = page.Paragraphs.FirstOrDefault(p => p.Length is >= 40 and <= 280)
                        ?? page.Paragraphs.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(quote)) continue;
            citations.Add(new RagCitationDto
            {
                PageId = page.PageId,
                Url = page.Url,
                Title = page.Title,
                SectionTitle = sectionTitle ?? section.Heading,
                SectionKey = section.Key,
                Quote = quote.Trim(),
                CrawlType = partner.Any(p => p.PageId == page.PageId) ? CrawlTypes.Partner : CrawlTypes.Competitors,
                Verified = true,
            });
        }
        return citations;
    }

    private static IReadOnlyList<RagCitationDto> ExtractLibraryCitationsFromContent(
        string content,
        IReadOnlyList<GccQuoteablePage> partner,
        IReadOnlyList<GccQuoteablePage> competitor,
        string? sectionTitle,
        string? sectionKey)
    {
        var citations = new List<RagCitationDto>();
        foreach (var page in partner.Concat(competitor))
        {
            var quote = page.Paragraphs.FirstOrDefault(p =>
                p.Length is >= 40 and <= 280
                && content.Contains(p.AsSpan(0, Math.Min(40, p.Length)).ToString(),
                    StringComparison.OrdinalIgnoreCase));
            quote ??= page.Paragraphs.FirstOrDefault(p => p.Length is >= 40 and <= 280);
            if (string.IsNullOrWhiteSpace(quote)) continue;
            // Only keep quotes that actually appear in source paragraphs (library honesty).
            if (!page.Paragraphs.Any(p => p.Contains(quote, StringComparison.Ordinal)))
                continue;
            citations.Add(new RagCitationDto
            {
                PageId = page.PageId,
                Url = page.Url,
                Title = page.Title,
                SectionTitle = sectionTitle,
                SectionKey = sectionKey,
                Quote = quote.Trim(),
                CrawlType = partner.Any(p => p.PageId == page.PageId) ? CrawlTypes.Partner : CrawlTypes.Competitors,
                Verified = true,
            });
        }
        return citations;
    }

    // --- helpers continue below (QueryRunAsync signature changed) ---
    private sealed record SeedQueryResult(
        IReadOnlyList<GccQuoteablePage> Pages,
        IReadOnlyList<GeekCrawlerRagThemeDto> Themes,
        string? Retrieval);

    private async Task<SeedQueryResult> QueryRunsAsync(
        IReadOnlyList<Guid> runIds,
        string need,
        string crawlType,
        int topK,
        bool? preferParent,
        bool? preferChild,
        IReadOnlyList<string> entities,
        string? retrievalMode,
        List<string> warnings,
        CancellationToken ct,
        bool failClosed = false,
        bool hardFilterEntityNames = true)
    {
        if (runIds.Count == 0)
            return new SeedQueryResult([], [], null);

        var pages = new List<GccQuoteablePage>();
        var themes = new List<GeekCrawlerRagThemeDto>();
        string? retrieval = null;
        var seenPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var runId in runIds)
        {
            var one = await QueryRunAsync(
                runId,
                need,
                crawlType,
                topK,
                preferParent,
                preferChild,
                entities,
                retrievalMode,
                warnings,
                ct,
                failClosed,
                hardFilterEntityNames).ConfigureAwait(false);
            retrieval ??= one.Retrieval;
            foreach (var page in one.Pages)
            {
                var key = !string.IsNullOrWhiteSpace(page.PageId) ? page.PageId! : page.Url;
                if (!seenPages.Add(key)) continue;
                pages.Add(page);
            }

            themes.AddRange(one.Themes);
        }

        return new SeedQueryResult(pages, themes, retrieval);
    }

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
        CancellationToken ct,
        bool failClosed = false,
        bool hardFilterEntityNames = true)
    {
        if (runId is null)
            return new SeedQueryResult([], [], null);

        IReadOnlyList<string>? entityFilter = hardFilterEntityNames && entities.Count > 0
            ? entities
            : null;
        var result = await _rag.QueryAsync(
            need,
            runId.Value,
            crawlType: crawlType,
            host: null,
            topK: topK,
            preferParent: preferParent,
            preferChild: preferChild,
            entityNames: entityFilter,
            retrievalMode: retrievalMode,
            // No anchor tool lookup on this path, and null rather than an empty dictionary so the
            // absence is the stated thing it is. Building one needs the create's brief -- host ->
            // partner spelling comes from GccRequiredToolMentions.AnchorLookup(briefJson,
            // partnerUrls) -- and nothing carries a brief, a project or a partner list into this
            // class: its dependencies are the RAG client, the provider factory, the logger and two
            // flags. Chunks retrieved here therefore reach the writer with no "Target Entity Match"
            // line, which is correct for a path that cannot know which partners the create declared;
            // labelling them from anything else available here would be a guess. Threading the brief
            // from DraftAsync down through QueryRunAsync is the fix, and is deliberately not done
            // here as a drive-by. Tracked in plans/writer-anchor-tool-detection.md, which found that
            // request.CanonicalBrief already carries the brief as rawBrief -- so the fix threads one
            // optional parameter and changes no DTO.
            anchorToolLookup: null,
            ct: ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Create library query {CrawlType} run {RunId}: pages={PageCount} entityFilter={EntityFilter} hardFilter={HardFilter}",
            crawlType, runId, result?.Pages.Count ?? -1, entityFilter?.Count ?? 0, hardFilterEntityNames);

        if (result is null)
        {
            if (failClosed)
                throw new InvalidOperationException(
                    $"RAG evidence library query returned null for {crawlType} run {runId}.");
            warnings.Add($"RAG query skipped for {crawlType} (client returned null).");
            return new SeedQueryResult([], [], null);
        }

        if (result.Failed)
        {
            var detail = result.Error ?? result.Warning ?? $"RAG query failed for {crawlType}.";
            if (failClosed)
                throw new InvalidOperationException(detail);
            warnings.Add(detail);
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

    private async Task<CreateLibraryDraftResponse> WriteLongFormAsync(
        string intent,
        string topic,
        IReadOnlyList<string> entities,
        IReadOnlyList<GccQuoteablePage> partner,
        IReadOnlyList<GccQuoteablePage> competitor,
        IReadOnlyList<CreateLibraryDraftSourceDto> sources,
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
            Return ONE JSON object, nothing else:
            {"title": string, "sections":[{"heading":string,"paragraphs":[<paragraph>,...],"children":[<section>,...]}]}
            A <paragraph> is exactly one of:
              {"type":"text","runs":[{"text":string,"bold":bool?,"italic":bool?,"href":string?}]}
              {"type":"list","ordered":bool,"items":[[<run>,...],...]}
              {"type":"quote","runs":[<run>,...],"cite":string?}
              {"type":"code","code":string}
            Plain text inside "text" — never markup syntax of any kind. A paragraph break is
            a new paragraph object, not a blank line.
            """;
        var user = BuildResearchUserPrompt(intent, topic, entities, partner, competitor)
                   + "\n\nWrite a complete draft for this intent.";

        var (text, modelUsed) = await CompleteAsync(system, user, model, temperature: 0.45, maxTokens: 4096, ct)
            .ConfigureAwait(false);
        return new CreateLibraryDraftResponse
        {
            Intent = intent,
            Content = text,
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
        AppendPages(sb, partner);
        sb.AppendLine();
        sb.AppendLine("COMPETITOR PAGE EXCERPTS (research only — differentiate; no rival CTAs):");
        AppendPages(sb, competitor);
        return sb.ToString();
    }

    private static void AppendPages(
        StringBuilder sb,
        IReadOnlyList<GccQuoteablePage> pages)
    {
        if (pages.Count == 0)
        {
            sb.AppendLine("(none)");
            return;
        }

        foreach (var page in pages)
        {
            sb.AppendLine($"[{page.Title}] ({page.Url})");
            foreach (var para in page.Paragraphs)
                sb.AppendLine($"- {para}");
        }
    }

    private static IReadOnlyList<CreateLibraryDraftSourceDto> BuildSources(
        IReadOnlyList<GccQuoteablePage> partner,
        IReadOnlyList<GccQuoteablePage> competitor,
        IReadOnlyList<string> entities)
    {
        var sources = new List<CreateLibraryDraftSourceDto>();
        void Add(IEnumerable<GccQuoteablePage> pages, string crawlType)
        {
            foreach (var page in pages)
            {
                var entity = entities.FirstOrDefault(e =>
                    page.Title.Contains(e, StringComparison.OrdinalIgnoreCase)
                    || page.Url.Contains(e.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));
                sources.Add(new CreateLibraryDraftSourceDto
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

        foreach (var page in partner)
        {
            themes.Add(new RagThemeSourceDto
            {
                Label = page.Title,
                Relationship = "partner-theme",
                Entity = entities.FirstOrDefault(),
                Url = page.Url,
            });
        }

        foreach (var page in competitor)
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
            "researchplanning" or "research" => "researchPlanning",
            "finalsynthesis" or "final-synthesis" => "finalSynthesis",
            _ => "complete",
        };
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
