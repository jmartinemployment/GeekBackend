using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.Rag;
using GeekAPI.Services.ContentCreatorV2.Write;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>
/// Thin HTTP client for Geek-Crawler-Rag (index, query, pages, templates). Soft-disabled when
/// <c>GEEK_CRAWLER_RAG_URL</c> is unset. No <c>/v1/generate</c> — library retrieval only.
/// </summary>
public interface IGeekCrawlerRagClient
{
    bool IsEnabled { get; }

    /// <summary>Fire-and-forget friendly enqueue. Returns null when disabled or request fails.</summary>
    Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default);

    /// <summary>One-shot status snapshot for UI reconnect (no polling). Null when disabled or 404.</summary>
    Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Delete every crawler-owned vector for a run. Returns false when the purge could not be
    /// proven — the caller must then abort the delete rather than orphan vectors whose Markdown
    /// source is about to disappear.
    /// </summary>
    /// Default is <c>false</c> (purge unproven) so an implementation that does not override it
    /// can never authorize a cascade delete by omission.
    Task<bool> DeleteRunIndexAsync(Guid runId, CancellationToken ct = default) =>
        Task.FromResult(false);

    /// <summary>
    /// Retrieve English chunks for a need. Returns null when the client is disabled.
    /// HTTP/transport failures set <see cref="GeekCrawlerRagQueryResult.Failed"/> — empty Pages must not be treated as success.
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

    /// <summary>Fetch Mongo Markdown by pageId. Null when disabled or 404. Optional runId scopes the library page.</summary>
    Task<GeekCrawlerRagPageMarkdown?> GetPageMarkdownAsync(
        string pageId,
        CancellationToken ct = default,
        string? runId = null);

    Task<JsonElement?> RunDiagnosticAsync(
        string endpoint,
        JsonElement input,
        CancellationToken ct = default) =>
        Task.FromResult<JsonElement?>(null);

    Task<GeekCrawlerRagCapabilities> GetCapabilitiesAsync(CancellationToken ct = default);
}

public sealed class GeekCrawlerRagCapabilities
{
    public IReadOnlyList<string> ExecutionVersions { get; init; } = [];
    public IReadOnlyList<string> SkillEnvelopeVersions { get; init; } = [];
    public IReadOnlyList<string> GenerationStages { get; init; } = [];
    /// <summary>Stages advertised by upstream capabilities (informational for Create library).</summary>
    public IReadOnlyList<string> AgentGenerationStages { get; init; } = [];
    public IReadOnlyList<string> SpecialistExecutors { get; init; } = [];
    public string SpecialistExecutorVersion { get; init; } = "";
    public bool ToolsAllowed { get; init; }
    public IReadOnlyList<string> AgentTraceVersions { get; init; } = [];
    public IReadOnlyList<string> AgentToolVersions { get; init; } = [];
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
    /// <summary>True when the RAG query HTTP call failed or threw — empty Pages must not be treated as success.</summary>
    public bool Failed { get; init; }
    public string? Error { get; init; }
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
    private static readonly JsonSerializerOptions JsonOpts = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new GccV2PythonDateTimeOffsetConverter());
        options.Converters.Add(new GccV2PythonDoubleConverter());
        return options;
    }

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

    public async Task<JsonElement?> RunDiagnosticAsync(
        string endpoint,
        JsonElement input,
        CancellationToken ct = default)
    {
        if (!_enabled) return null;
        var path = endpoint switch
        {
            "readiness-score" or "fact-density" or "entity-map" or "schema-markup"
                => $"v1/diagnostics/{endpoint}",
            "query-plan" or "competitor-page" or "content-gap"
                or "readiness-comparison" or "competitor-audit" or "competitor-positioning"
                => $"v1/intelligence/{endpoint}",
            "faq-set" or "citable-claims" or "comparison-brief"
                or "competitive-response" or "pillar-outline" or "pillar-article"
                => $"v1/content/{endpoint}",
            _ => throw new ArgumentException("Unsupported analysis endpoint.", nameof(endpoint)),
        };
        using var response = await _http.PostAsJsonAsync(
            path, input, JsonOpts, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Analysis '{endpoint}' failed with HTTP {(int)response.StatusCode}: {Truncate(body)}");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, ct)
            .ConfigureAwait(false);
    }

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

    public async Task<bool> DeleteRunIndexAsync(Guid runId, CancellationToken ct = default)
    {
        if (!_enabled)
            return false;

        try
        {
            using var response = await _http
                .DeleteAsync($"v1/index/runs/{runId:D}", ct)
                .ConfigureAwait(false);

            // 404 means the run holds no points — already in the desired state.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return true;

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogError(
                    "Geek-Crawler-Rag vector purge failed for {RunId}: {Status} {Body}",
                    runId,
                    (int)response.StatusCode,
                    Truncate(body));
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Geek-Crawler-Rag vector purge threw for {RunId}", runId);
            return false;
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
                    Failed = true,
                    Error = $"RAG query failed ({(int)response.StatusCode}).",
                    Warning = $"RAG query failed ({(int)response.StatusCode}).",
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
                    ? (dto?.Warning ?? "No RAG chunks returned for this query.")
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
                Failed = true,
                Error = "RAG query unavailable.",
                Warning = "RAG query unavailable.",
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
        CancellationToken ct = default,
        string? runId = null)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(pageId))
            return null;
        try
        {
            var path = $"v1/pages/{Uri.EscapeDataString(pageId)}";
            if (!string.IsNullOrWhiteSpace(runId))
                path += $"?runId={Uri.EscapeDataString(runId.Trim())}";

            using var response = await _http.GetAsync(path, ct)
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
                RunId = dto.RunId ?? runId ?? "",
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

    public async Task<GeekCrawlerRagCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        if (!_enabled)
        {
            throw new CapabilitiesUnavailableException(
                "Geek-Crawler-Rag is disabled (GEEK_CRAWLER_RAG_URL unset). Configure RAG and retry.");
        }

        try
        {
            using var response = await _http.GetAsync("v1/capabilities", ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status >= 500 || status == 408 || status == 429)
            {
                _logger.LogWarning(
                    "Geek-Crawler-Rag capabilities transport failure HTTP {Status}", status);
                throw new CapabilitiesTransportError(
                    $"RAG capabilities unavailable (HTTP {status}). Retry the job.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Geek-Crawler-Rag capabilities unavailable HTTP {Status}", status);
                throw new CapabilitiesUnavailableException(
                    $"RAG capabilities endpoint returned HTTP {status}. Fix RAG config / re-deploy.");
            }

            var dto = await response.Content.ReadFromJsonAsync<CapabilitiesDto>(JsonOpts, ct)
                .ConfigureAwait(false);
            if (dto is null)
            {
                throw new CapabilitiesUnavailableException(
                    "RAG capabilities response body was empty or malformed.");
            }

            return new GeekCrawlerRagCapabilities
            {
                ExecutionVersions = dto.ExecutionVersions ?? [],
                SkillEnvelopeVersions = dto.SkillEnvelopeVersions ?? [],
                GenerationStages = dto.GenerationStages ?? [],
                AgentGenerationStages = dto.AgentGenerationStages
                    ?? (dto.GenerationStages ?? [])
                        .Where(s => !string.Equals(s, "complete", StringComparison.Ordinal))
                        .ToList(),
                SpecialistExecutors = dto.SpecialistExecutors ?? [],
                SpecialistExecutorVersion = dto.SpecialistExecutorVersion ?? "",
                ToolsAllowed = dto.ToolsAllowed,
                AgentTraceVersions = dto.AgentTraceVersions ?? [],
                AgentToolVersions = dto.AgentToolVersions ?? [],
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not CapabilitiesTransportError
                                   and not CapabilitiesUnavailableException)
        {
            _logger.LogWarning(ex, "Geek-Crawler-Rag capabilities request threw");
            throw new CapabilitiesTransportError(
                "RAG capabilities request failed (network/timeout). Retry the job.", ex);
        }
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
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk));
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

    private sealed class CapabilitiesDto
    {
        public List<string>? ExecutionVersions { get; set; }
        public List<string>? SkillEnvelopeVersions { get; set; }
        public List<string>? GenerationStages { get; set; }
        public List<string>? AgentGenerationStages { get; set; }
        public List<string>? SpecialistExecutors { get; set; }
        public string? SpecialistExecutorVersion { get; set; }
        public bool ToolsAllowed { get; set; }
        public List<string>? AgentTraceVersions { get; set; }
        public List<string>? AgentToolVersions { get; set; }
    }
}
