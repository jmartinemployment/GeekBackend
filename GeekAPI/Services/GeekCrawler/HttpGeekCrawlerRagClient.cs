using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.ContentCreatorV2.Generation;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>
/// Thin HTTP client for Geek-Crawler-Rag (index + query). Soft-disabled when
/// <c>GEEK_CRAWLER_RAG_URL</c> is unset. Never embeds or talks to Qdrant directly.
/// </summary>
public interface IGeekCrawlerRagClient
{
    bool IsEnabled { get; }

    /// <summary>Fire-and-forget friendly enqueue. Returns null when disabled or request fails.</summary>
    Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default);

    /// <summary>One-shot status snapshot for UI reconnect (no polling). Null when disabled or 404.</summary>
    Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Retrieve English chunks for a need. Empty list + warning on miss (notify-and-skip).
    /// Returns null when the client is disabled.
    /// Optional preferParent/preferChild and entityNames are forward-compatible with
    /// Geek-Crawler-Rag Phase B (ignored by older indexers).
    /// </summary>
    Task<GeekCrawlerRagQueryResult?> QueryAsync(
        string need,
        Guid runId,
        string? crawlType = null,
        string? host = null,
        int topK = 8,
        bool? preferParent = null,
        bool? preferChild = null,
        IReadOnlyList<string>? entityNames = null,
        string? retrievalMode = null,
        CancellationToken ct = default);

    /// <summary>Phase D2 — upsert ad templates into Geek-Crawler-Rag. Null when disabled.</summary>
    Task<GeekCrawlerRagTemplateIndexResult?> IndexTemplatesAsync(
        IReadOnlyList<GeekCrawlerRagTemplateDto> templates,
        CancellationToken ct = default);

    /// <summary>Phase D2 — retrieve few-shot ad template exemplars. Null when disabled.</summary>
    Task<GeekCrawlerRagTemplateQueryResult?> QueryTemplatesAsync(
        string need,
        int topK = 5,
        string? channel = null,
        IReadOnlyList<string>? entityTags = null,
        CancellationToken ct = default);

    /// <summary>Fetch Mongo Markdown by pageId. Null when disabled or 404.</summary>
    Task<GeekCrawlerRagPageMarkdown?> GetPageMarkdownAsync(
        string pageId,
        CancellationToken ct = default);

    /// <summary>
    /// Citeable multi-step generate on Rag (retrieve → Markdown → draft → verify).
    /// Null when disabled or request fails (caller may fall back to one-shot).
    /// </summary>
    Task<GeekCrawlerRagGenerateResult?> GenerateAsync(
        GeekCrawlerRagGenerateRequest request,
        CancellationToken ct = default);

    Task<GeekCrawlerRagCapabilities?> GetCapabilitiesAsync(CancellationToken ct = default) =>
        Task.FromResult<GeekCrawlerRagCapabilities?>(null);
}

public sealed class GeekCrawlerRagCapabilities
{
    public IReadOnlyList<string> ExecutionVersions { get; init; } = [];
    public IReadOnlyList<string> SkillEnvelopeVersions { get; init; } = [];
    public IReadOnlyList<string> GenerationStages { get; init; } = [];
    public IReadOnlyList<string> SpecialistExecutors { get; init; } = [];
    public string SpecialistExecutorVersion { get; init; } = "";
    public bool ToolsAllowed { get; init; }
}

public sealed class GeekCrawlerRagPageMarkdown
{
    public required string PageId { get; init; }
    public required string RunId { get; init; }
    public required string Url { get; init; }
    public string? FinalUrl { get; init; }
    public string? Title { get; init; }
    public required string Markdown { get; init; }
}

public sealed class GeekCrawlerRagGenerateRequest
{
    public required string WritingIntent { get; init; }
    public required string Topic { get; init; }
    public string? PartnerRunId { get; init; }
    public string? CompetitorRunId { get; init; }
    public IReadOnlyList<string>? TargetEntities { get; init; }
    public IReadOnlyList<GeekCrawlerRagTemplateDto>? AdTemplates { get; init; }
    public bool GraphEnabled { get; init; } = true;
    public string GenerationStage { get; init; } = "complete";
    public IReadOnlyList<GeekCrawlerRagOutlineSectionDto>? Outline { get; init; }
    public string? SectionKey { get; init; }
    public string? SectionHeading { get; init; }
    public string? SectionBrief { get; init; }
    public IReadOnlyList<string>? CompletedSectionSummaries { get; init; }
    public string? DraftContent { get; init; }
    public IReadOnlyList<GeekCrawlerRagGenerateSourceDto>? Sources { get; init; }
    public JsonElement? CanonicalBrief { get; init; }
    public string? ModelPolicyPreset { get; init; }
    public string? ModelPolicyVersion { get; init; }
    public IReadOnlyDictionary<string, string>? StageModelOverrides { get; init; }
    public string ExecutionVersion { get; init; } = "rag-generate.v1";
    public string AttemptId { get; init; } = Guid.NewGuid().ToString("D");
    public GccV2SkillExecutionSnapshot? SkillExecution { get; init; }
}

public sealed class GeekCrawlerRagOutlineSectionDto
{
    public string Key { get; init; } = "";
    public string Heading { get; init; } = "";
    public string Brief { get; init; } = "";
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
}

public sealed class GeekCrawlerRagSkillProvenance
{
    public string EnvelopeVersion { get; init; } = "";
    public string CatalogVersion { get; init; } = "";
    public string SnapshotHash { get; init; } = "";
    public string Stage { get; init; } = "";
    public IReadOnlyList<string> SkillVersions { get; init; } = [];
}

public sealed class GeekCrawlerRagGenerateProvenance
{
    public string? GenerationStage { get; init; }
    public string? ModelUsed { get; init; }
    public string? ModelPolicyPreset { get; init; }
    public string? ModelPolicyVersion { get; init; }
    public string? PromptVersion { get; init; }
    public string? Retrieval { get; init; }
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
    public string? SpecialistExecutor { get; init; }
    public string? SpecialistExecutorVersion { get; init; }
    public string? ExecutionVersion { get; init; }
    public string? AttemptId { get; init; }
    public GeekCrawlerRagSkillProvenance? Skills { get; init; }
}

public enum GeekCrawlerRagValidationIssueCategory
{
    UnsupportedClaim,
    SourceConflict,
    BriefAlignment,
    BrandVoice,
    OriginalityRepetition,
    Usefulness,
    Cta,
    SeoGeo,
    ContentTypeRequirements,
}

public sealed class GeekCrawlerRagValidationIssue
{
    public string? SectionTitle { get; init; }
    public required GeekCrawlerRagValidationIssueCategory Category { get; init; }
    public required string Detail { get; init; }
    public required string RepairInstruction { get; init; }
}

public sealed class GeekCrawlerRagValidation
{
    public bool Approved { get; init; }
    public required IReadOnlyList<GeekCrawlerRagValidationIssue> Issues { get; init; }
    public required IReadOnlyList<string> Strengths { get; init; }
    public int UnsupportedClaimCount { get; init; }
    public double BriefAlignmentScore { get; init; }
    public double EvidenceCoverageScore { get; init; }
    public double UsefulnessScore { get; init; }
    public double OriginalityScore { get; init; }
    public double BrandAlignmentScore { get; init; }
}

public sealed class GeekCrawlerRagCitationDto
{
    public string? PageId { get; init; }
    public string Url { get; init; } = "";
    public string? Title { get; init; }
    public string? SectionTitle { get; init; }
    public string Quote { get; init; } = "";
    public string? CrawlType { get; init; }
}

public sealed class GeekCrawlerRagGenerateSourceDto
{
    public string Url { get; init; } = "";
    public string? Title { get; init; }
    public string? Entity { get; init; }
    public string? CrawlType { get; init; }
    public string? Kind { get; init; }
    public string? PageId { get; init; }
}

public sealed class GeekCrawlerRagBattlecardDto
{
    public string PartnerSummary { get; init; } = "";
    public string CompetitorSummary { get; init; } = "";
    public List<string> Differentiators { get; init; } = [];
    public List<string> Risks { get; init; } = [];
}

public sealed class GeekCrawlerRagGenerateResult
{
    public required string Intent { get; init; }
    public string? Content { get; init; }
    public IReadOnlyList<string>? Variations { get; init; }
    public GeekCrawlerRagBattlecardDto? Battlecard { get; init; }
    public IReadOnlyList<GeekCrawlerRagCitationDto> Citations { get; init; } = [];
    public IReadOnlyList<GeekCrawlerRagGenerateSourceDto> Sources { get; init; } = [];
    public IReadOnlyList<GeekCrawlerRagThemeDto> Themes { get; init; } = [];
    public IReadOnlyList<GeekCrawlerRagOutlineSectionDto>? Outline { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> EvidenceWarnings { get; init; } = [];
    public string? ModelUsed { get; init; }
    public string? Retrieval { get; init; }
    public GeekCrawlerRagGenerateProvenance? Provenance { get; init; }
    public GeekCrawlerRagValidation? Validation { get; init; }
}

public sealed class GeekCrawlerRagThemeDto
{
    public string Label { get; init; } = "";
    public string? Relationship { get; init; }
    public string? Entity { get; init; }
    public string? RelatedEntity { get; init; }
    public string? Url { get; init; }
    public string? Category { get; init; }
    public string? CrawlType { get; init; }
    public double? Score { get; init; }
}

public sealed class GeekCrawlerRagQueryResult
{
    public required Guid RunId { get; init; }
    public required IReadOnlyList<GccQuoteablePage> Pages { get; init; }
    public string? Warning { get; init; }
    public IReadOnlyList<GeekCrawlerRagThemeDto> Themes { get; init; } = [];
    public string? Retrieval { get; init; }
}

public sealed class GeekCrawlerRagTemplateDto
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Channel { get; init; }
    public string? Framework { get; init; }
    public string? Tone { get; init; }
    public string Body { get; init; } = "";
    public IReadOnlyList<string>? EntityTags { get; init; }
}

public sealed class GeekCrawlerRagTemplateIndexResult
{
    public int Upserted { get; init; }
    public string? Warning { get; init; }
}

public sealed class GeekCrawlerRagTemplateQueryResult
{
    public IReadOnlyList<GeekCrawlerRagTemplateDto> Templates { get; init; } = [];
    public string? Warning { get; init; }
}

public sealed class GeekCrawlerRagIndexStatus
{
    public required Guid RunId { get; init; }
    public required string State { get; init; }
    public string? CrawlType { get; init; }
    public int? MongoPageCount { get; init; }
    public int PagesSeen { get; init; }
    public int PagesEnglish { get; init; }
    public int PagesSkippedLang { get; init; }
    public int PagesSkippedEmpty { get; init; }
    public int ChunksUpserted { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; init; }
}

public sealed class HttpGeekCrawlerRagClient : IGeekCrawlerRagClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly ILogger<HttpGeekCrawlerRagClient> _logger;
    private readonly bool _enabled;

    public HttpGeekCrawlerRagClient(HttpClient http, ILogger<HttpGeekCrawlerRagClient> logger)
    {
        _http = http;
        _logger = logger;
        _enabled = _http.BaseAddress is not null;
    }

    public bool IsEnabled => _enabled;

    public async Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default)
    {
        if (!_enabled)
            return null;

        try
        {
            using var response = await _http.PostAsJsonAsync(
                "v1/index",
                new { runId = runId.ToString("D") },
                JsonOpts,
                ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "Geek-Crawler-Rag index enqueue failed for {RunId}: {Status} {Body}",
                    runId,
                    (int)response.StatusCode,
                    Truncate(body));
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<IndexStatusDto>(JsonOpts, ct)
                .ConfigureAwait(false);
            if (dto is null || string.IsNullOrWhiteSpace(dto.RunId))
                return null;

            return new GeekCrawlerRagIndexStatus
            {
                RunId = Guid.TryParse(dto.RunId, out var id) ? id : runId,
                State = dto.State ?? "unknown",
                CrawlType = dto.CrawlType,
                MongoPageCount = dto.MongoPageCount,
                PagesSeen = dto.PagesSeen,
                PagesEnglish = dto.PagesEnglish,
                PagesSkippedLang = dto.PagesSkippedLang,
                PagesSkippedEmpty = dto.PagesSkippedEmpty,
                ChunksUpserted = dto.ChunksUpserted,
                Error = dto.Error,
                StartedAtUtc = dto.StartedAtUtc,
                FinishedAtUtc = dto.FinishedAtUtc,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Geek-Crawler-Rag index enqueue threw for {RunId}", runId);
            return null;
        }
    }

    public async Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default)
    {
        if (!_enabled)
            return null;

        try
        {
            using var response = await _http.GetAsync($"v1/index/{runId:D}", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "Geek-Crawler-Rag index status failed for {RunId}: {Status} {Body}",
                    runId,
                    (int)response.StatusCode,
                    Truncate(body));
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<IndexStatusDto>(JsonOpts, ct)
                .ConfigureAwait(false);
            if (dto is null || string.IsNullOrWhiteSpace(dto.RunId))
                return null;

            return new GeekCrawlerRagIndexStatus
            {
                RunId = Guid.TryParse(dto.RunId, out var id) ? id : runId,
                State = dto.State ?? "unknown",
                CrawlType = dto.CrawlType,
                MongoPageCount = dto.MongoPageCount,
                PagesSeen = dto.PagesSeen,
                PagesEnglish = dto.PagesEnglish,
                PagesSkippedLang = dto.PagesSkippedLang,
                PagesSkippedEmpty = dto.PagesSkippedEmpty,
                ChunksUpserted = dto.ChunksUpserted,
                Error = dto.Error,
                StartedAtUtc = dto.StartedAtUtc,
                FinishedAtUtc = dto.FinishedAtUtc,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Geek-Crawler-Rag index status threw for {RunId}", runId);
            return null;
        }
    }

    public async Task<GeekCrawlerRagQueryResult?> QueryAsync(
        string need,
        Guid runId,
        string? crawlType = null,
        string? host = null,
        int topK = 8,
        bool? preferParent = null,
        bool? preferChild = null,
        IReadOnlyList<string>? entityNames = null,
        string? retrievalMode = null,
        CancellationToken ct = default)
    {
        if (!_enabled)
            return null;

        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["need"] = need,
                ["runId"] = runId.ToString("D"),
                ["topK"] = topK,
            };
            if (!string.IsNullOrWhiteSpace(crawlType))
                payload["crawlType"] = crawlType;
            if (!string.IsNullOrWhiteSpace(host))
                payload["host"] = host;
            if (preferParent is not null)
                payload["preferParent"] = preferParent.Value;
            if (preferChild is not null)
                payload["preferChild"] = preferChild.Value;
            if (entityNames is { Count: > 0 })
                payload["entityNames"] = entityNames.Where(e => !string.IsNullOrWhiteSpace(e)).Take(12).ToArray();
            if (!string.IsNullOrWhiteSpace(retrievalMode))
                payload["retrievalMode"] = retrievalMode;

            using var response = await _http.PostAsJsonAsync("v1/query", payload, JsonOpts, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "Geek-Crawler-Rag query failed for {RunId}: {Status} {Body}",
                    runId,
                    (int)response.StatusCode,
                    Truncate(body));
                return new GeekCrawlerRagQueryResult
                {
                    RunId = runId,
                    Pages = [],
                    Warning = $"RAG query failed ({(int)response.StatusCode}). Continuing without it.",
                };
            }

            var dto = await response.Content.ReadFromJsonAsync<QueryResponseDto>(JsonOpts, ct)
                .ConfigureAwait(false);
            var pages = MapChunksToQuoteable(dto?.Chunks);
            return new GeekCrawlerRagQueryResult
            {
                RunId = runId,
                Pages = pages,
                Themes = MapThemes(dto?.Themes),
                Retrieval = dto?.Retrieval,
                Warning = pages.Count == 0
                    ? (dto?.Warning ?? "No RAG chunks returned. Continuing without it.")
                    : dto?.Warning,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Geek-Crawler-Rag query threw for {RunId}", runId);
            return new GeekCrawlerRagQueryResult
            {
                RunId = runId,
                Pages = [],
                Warning = "RAG query unavailable. Continuing without it.",
            };
        }
    }

    public async Task<GeekCrawlerRagTemplateIndexResult?> IndexTemplatesAsync(
        IReadOnlyList<GeekCrawlerRagTemplateDto> templates,
        CancellationToken ct = default)
    {
        if (!_enabled)
            return null;
        try
        {
            using var response = await _http.PostAsJsonAsync(
                "v1/templates/index",
                new { templates },
                JsonOpts,
                ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "Geek-Crawler-Rag template index failed: {Status} {Body}",
                    (int)response.StatusCode,
                    Truncate(body));
                return new GeekCrawlerRagTemplateIndexResult
                {
                    Upserted = 0,
                    Warning = $"Template index failed ({(int)response.StatusCode}).",
                };
            }

            var dto = await response.Content.ReadFromJsonAsync<TemplateIndexDto>(JsonOpts, ct)
                .ConfigureAwait(false);
            return new GeekCrawlerRagTemplateIndexResult
            {
                Upserted = dto?.Upserted ?? 0,
                Warning = dto?.Warning,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Geek-Crawler-Rag template index threw");
            return new GeekCrawlerRagTemplateIndexResult
            {
                Upserted = 0,
                Warning = "Template index unavailable.",
            };
        }
    }

    public async Task<GeekCrawlerRagTemplateQueryResult?> QueryTemplatesAsync(
        string need,
        int topK = 5,
        string? channel = null,
        IReadOnlyList<string>? entityTags = null,
        CancellationToken ct = default)
    {
        if (!_enabled)
            return null;
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["need"] = need,
                ["topK"] = topK,
            };
            if (!string.IsNullOrWhiteSpace(channel))
                payload["channel"] = channel;
            if (entityTags is { Count: > 0 })
                payload["entityTags"] = entityTags.Take(12).ToArray();

            using var response = await _http.PostAsJsonAsync("v1/templates/query", payload, JsonOpts, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "Geek-Crawler-Rag template query failed: {Status} {Body}",
                    (int)response.StatusCode,
                    Truncate(body));
                return new GeekCrawlerRagTemplateQueryResult
                {
                    Templates = [],
                    Warning = $"Template query failed ({(int)response.StatusCode}).",
                };
            }

            var dto = await response.Content.ReadFromJsonAsync<TemplateQueryDto>(JsonOpts, ct)
                .ConfigureAwait(false);
            var list = (dto?.Templates ?? [])
                .Where(t => !string.IsNullOrWhiteSpace(t.Body))
                .Select(t => new GeekCrawlerRagTemplateDto
                {
                    Id = t.Id ?? "",
                    Name = t.Name ?? "template",
                    Channel = t.Channel,
                    Framework = t.Framework,
                    Tone = t.Tone,
                    Body = t.Body ?? "",
                    EntityTags = t.EntityTags,
                })
                .ToList();
            return new GeekCrawlerRagTemplateQueryResult
            {
                Templates = list,
                Warning = dto?.Warning,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Geek-Crawler-Rag template query threw");
            return new GeekCrawlerRagTemplateQueryResult
            {
                Templates = [],
                Warning = "Template query unavailable.",
            };
        }
    }

    public async Task<GeekCrawlerRagPageMarkdown?> GetPageMarkdownAsync(
        string pageId,
        CancellationToken ct = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(pageId))
            return null;
        try
        {
            using var response = await _http.GetAsync($"v1/pages/{Uri.EscapeDataString(pageId)}", ct)
                .ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Geek-Crawler-Rag page markdown failed for {PageId}: {Status}",
                    pageId,
                    (int)response.StatusCode);
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<PageMarkdownDto>(JsonOpts, ct)
                .ConfigureAwait(false);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Markdown))
                return null;
            return new GeekCrawlerRagPageMarkdown
            {
                PageId = dto.PageId ?? pageId,
                RunId = dto.RunId ?? "",
                Url = dto.Url ?? "",
                FinalUrl = dto.FinalUrl,
                Title = dto.Title,
                Markdown = dto.Markdown,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Geek-Crawler-Rag page markdown threw for {PageId}", pageId);
            return null;
        }
    }

    public async Task<GeekCrawlerRagGenerateResult?> GenerateAsync(
        GeekCrawlerRagGenerateRequest request,
        CancellationToken ct = default)
    {
        if (!_enabled)
            return null;
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["writingIntent"] = request.WritingIntent,
                ["topic"] = request.Topic,
                ["graphEnabled"] = request.GraphEnabled,
                ["generationStage"] = request.GenerationStage,
                ["executionVersion"] = request.ExecutionVersion,
                ["attemptId"] = request.AttemptId,
            };
            if (request.SkillExecution is not null)
                payload["skillExecution"] = request.SkillExecution;
            if (!string.IsNullOrWhiteSpace(request.PartnerRunId))
                payload["partnerRunId"] = request.PartnerRunId;
            if (!string.IsNullOrWhiteSpace(request.CompetitorRunId))
                payload["competitorRunId"] = request.CompetitorRunId;
            if (request.TargetEntities is { Count: > 0 })
                payload["targetEntities"] = request.TargetEntities.Take(12).ToArray();
            if (request.AdTemplates is { Count: > 0 })
            {
                payload["adTemplates"] = request.AdTemplates
                    .Select(t => new
                    {
                        id = t.Id,
                        name = t.Name,
                        channel = t.Channel,
                        framework = t.Framework,
                        body = t.Body,
                    })
                    .ToArray();
            }
            if (request.Outline is { Count: > 0 })
                payload["outline"] = request.Outline;
            if (!string.IsNullOrWhiteSpace(request.SectionKey))
                payload["sectionKey"] = request.SectionKey;
            if (!string.IsNullOrWhiteSpace(request.SectionHeading))
                payload["sectionHeading"] = request.SectionHeading;
            if (!string.IsNullOrWhiteSpace(request.SectionBrief))
                payload["sectionBrief"] = request.SectionBrief;
            if (request.CompletedSectionSummaries is { Count: > 0 })
                payload["completedSectionSummaries"] =
                    request.CompletedSectionSummaries.Take(12).ToArray();
            if (!string.IsNullOrWhiteSpace(request.DraftContent))
                payload["draftContent"] = request.DraftContent;
            if (request.Sources is { Count: > 0 })
                payload["sources"] = request.Sources;
            if (request.CanonicalBrief is { } canonicalBrief)
                payload["canonicalBrief"] = canonicalBrief;
            if (!string.IsNullOrWhiteSpace(request.ModelPolicyPreset))
                payload["modelPolicyPreset"] = request.ModelPolicyPreset;
            if (!string.IsNullOrWhiteSpace(request.ModelPolicyVersion))
                payload["modelPolicyVersion"] = request.ModelPolicyVersion;
            if (request.StageModelOverrides is { Count: > 0 })
                payload["stageModelOverrides"] = request.StageModelOverrides;

            using var response = await _http.PostAsJsonAsync("v1/generate", payload, JsonOpts, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "Geek-Crawler-Rag generate failed: {Status} {Body}",
                    (int)response.StatusCode,
                    Truncate(body));
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<GenerateResponseDto>(JsonOpts, ct)
                .ConfigureAwait(false);
            if (dto is null)
                return null;

            return new GeekCrawlerRagGenerateResult
            {
                Intent = dto.Intent ?? request.WritingIntent,
                Content = dto.Content,
                Variations = dto.Variations,
                Battlecard = dto.Battlecard is null
                    ? null
                    : new GeekCrawlerRagBattlecardDto
                    {
                        PartnerSummary = dto.Battlecard.PartnerSummary ?? "",
                        CompetitorSummary = dto.Battlecard.CompetitorSummary ?? "",
                        Differentiators = dto.Battlecard.Differentiators ?? [],
                        Risks = dto.Battlecard.Risks ?? [],
                    },
                Citations = (dto.Citations ?? [])
                    .Where(c => !string.IsNullOrWhiteSpace(c.Quote) && !string.IsNullOrWhiteSpace(c.Url))
                    .Select(c => new GeekCrawlerRagCitationDto
                    {
                        PageId = c.PageId,
                        Url = c.Url ?? "",
                        Title = c.Title,
                        SectionTitle = c.SectionTitle,
                        Quote = c.Quote ?? "",
                        CrawlType = c.CrawlType,
                    })
                    .ToList(),
                Sources = (dto.Sources ?? [])
                    .Select(s => new GeekCrawlerRagGenerateSourceDto
                    {
                        Url = s.Url ?? "",
                        Title = s.Title,
                        Entity = s.Entity,
                        CrawlType = s.CrawlType,
                        Kind = s.Kind,
                        PageId = s.PageId,
                    })
                    .ToList(),
                Themes = MapThemes(dto.Themes),
                Outline = dto.Outline?
                    .Where(s => !string.IsNullOrWhiteSpace(s.Heading))
                    .Select(s => new GeekCrawlerRagOutlineSectionDto
                    {
                        Key = s.Key ?? "",
                        Heading = s.Heading ?? "",
                        Brief = s.Brief ?? "",
                        EvidenceIds = s.EvidenceIds ?? [],
                    })
                    .ToList(),
                Warnings = dto.Warnings ?? [],
                EvidenceWarnings = dto.EvidenceWarnings ?? [],
                ModelUsed = dto.ModelUsed,
                Retrieval = dto.Retrieval,
                Provenance = dto.Provenance is null
                    ? null
                    : new GeekCrawlerRagGenerateProvenance
                    {
                        GenerationStage = dto.Provenance.GenerationStage,
                        ModelUsed = dto.Provenance.ModelUsed,
                        ModelPolicyPreset = dto.Provenance.ModelPolicyPreset,
                        ModelPolicyVersion = dto.Provenance.ModelPolicyVersion,
                        PromptVersion = dto.Provenance.PromptVersion,
                        Retrieval = dto.Provenance.Retrieval,
                        EvidenceIds = dto.Provenance.EvidenceIds ?? [],
                        SpecialistExecutor = dto.Provenance.SpecialistExecutor,
                        SpecialistExecutorVersion = dto.Provenance.SpecialistExecutorVersion,
                        ExecutionVersion = dto.Provenance.ExecutionVersion,
                        AttemptId = dto.Provenance.AttemptId,
                        Skills = dto.Provenance.Skills is null
                            ? null
                            : new GeekCrawlerRagSkillProvenance
                            {
                                EnvelopeVersion = dto.Provenance.Skills.EnvelopeVersion ?? "",
                                CatalogVersion = dto.Provenance.Skills.CatalogVersion ?? "",
                                SnapshotHash = dto.Provenance.Skills.SnapshotHash ?? "",
                                Stage = dto.Provenance.Skills.Stage ?? "",
                                SkillVersions = dto.Provenance.Skills.SkillVersions ?? [],
                            },
                    },
                Validation = MapValidation(dto.Validation),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Geek-Crawler-Rag generate threw");
            return null;
        }
    }

    public async Task<GeekCrawlerRagCapabilities?> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        if (!_enabled) return null;
        try
        {
            using var response = await _http.GetAsync("v1/capabilities", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var dto = await response.Content.ReadFromJsonAsync<CapabilitiesDto>(JsonOpts, ct)
                .ConfigureAwait(false);
            return dto is null
                ? null
                : new GeekCrawlerRagCapabilities
                {
                    ExecutionVersions = dto.ExecutionVersions ?? [],
                    SkillEnvelopeVersions = dto.SkillEnvelopeVersions ?? [],
                    GenerationStages = dto.GenerationStages ?? [],
                    SpecialistExecutors = dto.SpecialistExecutors ?? [],
                    SpecialistExecutorVersion = dto.SpecialistExecutorVersion ?? "",
                    ToolsAllowed = dto.ToolsAllowed,
                };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Geek-Crawler-Rag capabilities request threw");
            return null;
        }
    }

    private static GeekCrawlerRagValidation? MapValidation(ValidationDto? validation)
    {
        if (validation?.Approved is null
            || validation.Issues is null
            || validation.Strengths is null
            || validation.UnsupportedClaimCount is null)
            return null;
        var issues = new List<GeekCrawlerRagValidationIssue>();
        foreach (var issue in validation.Issues)
        {
            if (!TryParseValidationCategory(issue.Category, out var category)
                || string.IsNullOrWhiteSpace(issue.Detail)
                || string.IsNullOrWhiteSpace(issue.RepairInstruction))
                return null;
            issues.Add(new GeekCrawlerRagValidationIssue
            {
                SectionTitle = issue.SectionTitle,
                Category = category,
                Detail = issue.Detail,
                RepairInstruction = issue.RepairInstruction,
            });
        }
        if (validation.UnsupportedClaimCount < 0
            || !ValidScore(validation.BriefAlignmentScore)
            || !ValidScore(validation.EvidenceCoverageScore)
            || !ValidScore(validation.UsefulnessScore)
            || !ValidScore(validation.OriginalityScore)
            || !ValidScore(validation.BrandAlignmentScore))
            return null;
        return new GeekCrawlerRagValidation
        {
            Approved = validation.Approved.GetValueOrDefault(),
            Issues = issues,
            Strengths = validation.Strengths,
            UnsupportedClaimCount = validation.UnsupportedClaimCount.GetValueOrDefault(),
            BriefAlignmentScore = validation.BriefAlignmentScore.GetValueOrDefault(),
            EvidenceCoverageScore = validation.EvidenceCoverageScore.GetValueOrDefault(),
            UsefulnessScore = validation.UsefulnessScore.GetValueOrDefault(),
            OriginalityScore = validation.OriginalityScore.GetValueOrDefault(),
            BrandAlignmentScore = validation.BrandAlignmentScore.GetValueOrDefault(),
        };
    }

    private static bool ValidScore(double? score) => score is >= 0 and <= 100;

    private static bool TryParseValidationCategory(
        string? raw,
        out GeekCrawlerRagValidationIssueCategory category)
    {
        category = raw switch
        {
            "unsupportedClaim" => GeekCrawlerRagValidationIssueCategory.UnsupportedClaim,
            "sourceConflict" => GeekCrawlerRagValidationIssueCategory.SourceConflict,
            "briefAlignment" => GeekCrawlerRagValidationIssueCategory.BriefAlignment,
            "brandVoice" => GeekCrawlerRagValidationIssueCategory.BrandVoice,
            "originalityRepetition" => GeekCrawlerRagValidationIssueCategory.OriginalityRepetition,
            "usefulness" => GeekCrawlerRagValidationIssueCategory.Usefulness,
            "cta" => GeekCrawlerRagValidationIssueCategory.Cta,
            "seoGeo" => GeekCrawlerRagValidationIssueCategory.SeoGeo,
            "contentTypeRequirements" => GeekCrawlerRagValidationIssueCategory.ContentTypeRequirements,
            _ => default,
        };
        return raw is "unsupportedClaim" or "sourceConflict" or "briefAlignment" or "brandVoice"
            or "originalityRepetition" or "usefulness" or "cta" or "seoGeo" or "contentTypeRequirements";
    }

    internal static IReadOnlyList<GeekCrawlerRagThemeDto> MapThemes(IReadOnlyList<ThemeDto>? themes)
    {
        if (themes is null || themes.Count == 0)
            return [];
        return themes
            .Where(t => !string.IsNullOrWhiteSpace(t.Label))
            .Select(t => new GeekCrawlerRagThemeDto
            {
                Label = t.Label!,
                Relationship = t.Relationship,
                Entity = t.Entity,
                RelatedEntity = t.RelatedEntity,
                Url = t.Url,
                Category = t.Category,
                CrawlType = t.CrawlType,
                Score = t.Score,
            })
            .Take(20)
            .ToList();
    }

    internal static IReadOnlyList<GccQuoteablePage> MapChunksToQuoteable(IReadOnlyList<ChunkDto>? chunks)
    {
        if (chunks is null || chunks.Count == 0)
            return [];

        var byUrl = new Dictionary<string, List<ChunkDto>>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in chunks)
        {
            var url = string.IsNullOrWhiteSpace(chunk.FinalUrl) ? chunk.Url : chunk.FinalUrl;
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(chunk.Text))
                continue;
            if (!byUrl.TryGetValue(url, out var list))
            {
                list = [];
                byUrl[url] = list;
            }

            list.Add(chunk);
        }

        var pages = new List<GccQuoteablePage>();
        foreach (var (url, group) in byUrl)
        {
            group.Sort((a, b) => a.ChunkIndex.CompareTo(b.ChunkIndex));
            var title = group.Select(c => c.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
                        ?? url;
            var paragraphs = group
                .Select(c => Truncate(c.Text!, GccPartnerResearchCaps.MaxParagraphChars))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Take(GccPartnerResearchCaps.MaxParagraphsPerPage)
                .ToList();
            if (paragraphs.Count == 0)
                continue;

            pages.Add(new GccQuoteablePage(
                Url: url,
                Title: Truncate(title!, GccPartnerResearchCaps.MaxTitleChars),
                Headings: [],
                Paragraphs: paragraphs,
                PageId: group.Select(c => c.PageId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)),
                SectionTitle: group.Select(c => c.SectionTitle)
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))));

            if (pages.Count >= GccPartnerResearchCaps.MaxUrls)
                break;
        }

        return pages;
    }

    private static string Truncate(string value, int max = 400)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value[..max];
    }

    private sealed class IndexStatusDto
    {
        public string? RunId { get; set; }
        public string? State { get; set; }
        public string? CrawlType { get; set; }
        public int? MongoPageCount { get; set; }
        public int PagesSeen { get; set; }
        public int PagesEnglish { get; set; }
        public int PagesSkippedLang { get; set; }
        public int PagesSkippedEmpty { get; set; }
        public int ChunksUpserted { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset? StartedAtUtc { get; set; }
        public DateTimeOffset? FinishedAtUtc { get; set; }
    }

    private sealed class QueryResponseDto
    {
        public string? RunId { get; set; }
        public List<ChunkDto>? Chunks { get; set; }
        public string? Warning { get; set; }
        public string? Retrieval { get; set; }
        public List<ThemeDto>? Themes { get; set; }
    }

    internal sealed class ThemeDto
    {
        public string? Label { get; set; }
        public string? Relationship { get; set; }
        public string? Entity { get; set; }
        public string? RelatedEntity { get; set; }
        public string? Url { get; set; }
        public string? Category { get; set; }
        public string? CrawlType { get; set; }
        public double? Score { get; set; }
    }

    private sealed class TemplateIndexDto
    {
        public int Upserted { get; set; }
        public string? Warning { get; set; }
    }

    private sealed class TemplateQueryDto
    {
        public List<TemplateHitDto>? Templates { get; set; }
        public string? Warning { get; set; }
    }

    private sealed class TemplateHitDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Channel { get; set; }
        public string? Framework { get; set; }
        public string? Tone { get; set; }
        public string? Body { get; set; }
        public List<string>? EntityTags { get; set; }
    }

    internal sealed class ChunkDto
    {
        public string? RunId { get; set; }
        public string? CrawlType { get; set; }
        public string? Host { get; set; }
        public string? Url { get; set; }
        public string? FinalUrl { get; set; }
        public string? Title { get; set; }
        public int ChunkIndex { get; set; }
        public string? Language { get; set; }
        public string? Text { get; set; }
        public double Score { get; set; }
        public string? PageId { get; set; }
        public string? SectionTitle { get; set; }
    }

    private sealed class PageMarkdownDto
    {
        public string? PageId { get; set; }
        public string? RunId { get; set; }
        public string? Url { get; set; }
        public string? FinalUrl { get; set; }
        public string? Title { get; set; }
        public string? Markdown { get; set; }
    }

    private sealed class GenerateResponseDto
    {
        public string? Intent { get; set; }
        public string? Content { get; set; }
        public List<string>? Variations { get; set; }
        public BattlecardDto? Battlecard { get; set; }
        public List<CitationDto>? Citations { get; set; }
        public List<GenerateSourceDto>? Sources { get; set; }
        public List<ThemeDto>? Themes { get; set; }
        public List<OutlineSectionDto>? Outline { get; set; }
        public List<string>? Warnings { get; set; }
        public List<string>? EvidenceWarnings { get; set; }
        public string? ModelUsed { get; set; }
        public string? Retrieval { get; set; }
        public GenerateProvenanceDto? Provenance { get; set; }
        public ValidationDto? Validation { get; set; }
    }

    private sealed class ValidationDto
    {
        public bool? Approved { get; set; }
        public List<ValidationIssueDto>? Issues { get; set; }
        public List<string>? Strengths { get; set; }
        public int? UnsupportedClaimCount { get; set; }
        public double? BriefAlignmentScore { get; set; }
        public double? EvidenceCoverageScore { get; set; }
        public double? UsefulnessScore { get; set; }
        public double? OriginalityScore { get; set; }
        public double? BrandAlignmentScore { get; set; }
    }

    private sealed class ValidationIssueDto
    {
        public string? SectionTitle { get; set; }
        public string? Category { get; set; }
        public string? Detail { get; set; }
        public string? RepairInstruction { get; set; }
    }

    private sealed class OutlineSectionDto
    {
        public string? Key { get; set; }
        public string? Heading { get; set; }
        public string? Brief { get; set; }
        public List<string>? EvidenceIds { get; set; }
    }

    private sealed class GenerateProvenanceDto
    {
        public string? GenerationStage { get; set; }
        public string? ModelUsed { get; set; }
        public string? ModelPolicyPreset { get; set; }
        public string? ModelPolicyVersion { get; set; }
        public string? PromptVersion { get; set; }
        public string? Retrieval { get; set; }
        public List<string>? EvidenceIds { get; set; }
        public string? SpecialistExecutor { get; set; }
        public string? SpecialistExecutorVersion { get; set; }
        public string? ExecutionVersion { get; set; }
        public string? AttemptId { get; set; }
        public SkillProvenanceDto? Skills { get; set; }
    }

    private sealed class SkillProvenanceDto
    {
        public string? EnvelopeVersion { get; set; }
        public string? CatalogVersion { get; set; }
        public string? SnapshotHash { get; set; }
        public string? Stage { get; set; }
        public List<string>? SkillVersions { get; set; }
    }

    private sealed class CapabilitiesDto
    {
        public List<string>? ExecutionVersions { get; set; }
        public List<string>? SkillEnvelopeVersions { get; set; }
        public List<string>? GenerationStages { get; set; }
        public List<string>? SpecialistExecutors { get; set; }
        public string? SpecialistExecutorVersion { get; set; }
        public bool ToolsAllowed { get; set; }
    }

    private sealed class BattlecardDto
    {
        public string? PartnerSummary { get; set; }
        public string? CompetitorSummary { get; set; }
        public List<string>? Differentiators { get; set; }
        public List<string>? Risks { get; set; }
    }

    private sealed class CitationDto
    {
        public string? PageId { get; set; }
        public string? Url { get; set; }
        public string? Title { get; set; }
        public string? SectionTitle { get; set; }
        public string? Quote { get; set; }
        public string? CrawlType { get; set; }
    }

    private sealed class GenerateSourceDto
    {
        public string? Url { get; set; }
        public string? Title { get; set; }
        public string? Entity { get; set; }
        public string? CrawlType { get; set; }
        public string? Kind { get; set; }
        public string? PageId { get; set; }
    }
}
