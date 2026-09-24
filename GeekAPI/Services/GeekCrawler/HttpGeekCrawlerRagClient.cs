using System.Net.Http.Json;
using System.Text;
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
    /// proven — the caller must then abort the delete rather than orphan vectors whose source text
    /// source is about to disappear.
    /// </summary>
    /// Default is <c>false</c> (purge unproven) so an implementation that does not override it
    /// can never authorize a cascade delete by omission.
    Task<bool> DeleteRunIndexAsync(Guid runId, CancellationToken ct = default) =>
        Task.FromResult(false);

    /// <summary>
    /// Whether an index exists for each URL's host — the only question that decides whether a create
    /// can use an entered URL.
    ///
    /// Asked of the index, not of crawl_runs. A crawl can complete with pages in Mongo and nothing
    /// indexed, and pages that were never indexed cannot be cited. A URL that will not parse has no
    /// host, was never crawled, and is therefore reported as having no index — which is why no
    /// separate syntax check is needed.
    ///
    /// Default is empty so an implementation that does not override it can never report a URL as
    /// usable by omission.
    /// </summary>
    Task<IReadOnlyList<GeekCrawlerRagHostIndex>> HostsIndexedAsync(
        IReadOnlyList<string> urls,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GeekCrawlerRagHostIndex>>([]);

    /// <summary>
    /// Retrieve English chunks for a need. Returns null when the client is disabled.
    /// HTTP/transport failures set <see cref="GeekCrawlerRagQueryResult.Failed"/> — empty Pages must not be treated as success.
    /// Optional preferParent/preferChild and entityNames are forward-compatible with
    /// Geek-Crawler-Rag Phase B (ignored by older indexers).
    ///
    /// <para>
    /// anchorToolLookup is host -&gt; partner spelling, from
    /// <c>GccRequiredToolMentions.AnchorLookup</c>. It is never sent to Geek-Crawler-Rag: it labels
    /// retrieved chunks locally, so a caller with no brief passes null and gets unlabelled chunks
    /// rather than wrong ones.
    /// </para>
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
        IReadOnlyDictionary<string, string>? anchorToolLookup = null,
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

    /// <summary>Fetch a page's block-text projection by pageId. Null when disabled or 404. Optional runId scopes the library page.</summary>
    Task<GeekCrawlerRagPageText?> GetPageTextAsync(
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

public sealed class GeekCrawlerRagPageText
{
    public required string PageId { get; init; }
    public required string RunId { get; init; }
    public required string Url { get; init; }
    public string? FinalUrl { get; init; }
    public string? Title { get; init; }

    /// <summary>
    /// The page's plaintext projection, derived from the crawler's typed blocks by
    /// Geek-Crawler-Rag's single shared projection. This is the exact string the chunker
    /// embedded, so a quote taken from a retrieved chunk matches here.
    /// </summary>
    public required string Text { get; init; }
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

/// <summary>Whether an index exists for a URL's host, and which run indexed it. Whether, not how
/// much — a count would invite a threshold, which is a different question.</summary>
public sealed record GeekCrawlerRagHostIndex(string Url, string? Host, bool Indexed, string? RunId);

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
    /// <summary>
    /// Internal so a test can deserialize a real Geek-Crawler-Rag payload through the same options
    /// the client uses, rather than through a copy of them that can drift away from these.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOpts = CreateJsonOptions();

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

    /// <summary>
    /// Ceiling on the estimated tokens one page contributes to a research prompt.
    ///
    /// <para>
    /// <see cref="GccPartnerResearchCaps.MaxParagraphsPerPage"/> caps how many chunks reach the
    /// prompt, not how large they are. Eighty chunks each carrying a 2,000-char body plus a
    /// 2,000-char expanded parent block is roughly 80k tokens from a single URL, and a burst query
    /// run stacks several URLs into one context window. <c>MaxCharsPerPage</c> does not apply on
    /// this path -- it guards the HTML extractors, not RAG chunks -- so this is the only size
    /// bound between an expanded parent/child payload and the model.
    /// </para>
    /// </summary>
    private const int PromptResearchTokenCeiling = 16000;

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

    public async Task<IReadOnlyList<GeekCrawlerRagHostIndex>> HostsIndexedAsync(
        IReadOnlyList<string> urls,
        CancellationToken ct = default)
    {
        if (urls.Count == 0) return [];

        // Disabled means unknown, not "no index". Reporting every URL unindexed would block creates
        // on an answer we never obtained; reporting them indexed would be worse. The caller
        // distinguishes an empty result from a populated one.
        if (!_enabled) return [];

        try
        {
            using var response = await _http
                .PostAsJsonAsync("v1/index/hosts", new { urls }, JsonOpts, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "Geek-Crawler-Rag host index check failed: {Status} {Body}",
                    (int)response.StatusCode,
                    Truncate(body));
                return [];
            }

            var dto = await response.Content
                .ReadFromJsonAsync<HostIndexResponseDto>(JsonOpts, ct)
                .ConfigureAwait(false);

            return dto?.Results?
                .Select(r => new GeekCrawlerRagHostIndex(r.Url ?? "", r.Host, r.Indexed, r.RunId))
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Geek-Crawler-Rag host index check threw");
            return [];
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
        IReadOnlyDictionary<string, string>? anchorToolLookup = null,
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
                // A page-level quality floor, applied at retrieval rather than after it.
                // Geek-Crawler-Rag has accepted minQuality all along and turns it into a Qdrant
                // range filter on the qualityScore payload key (build_metadata_filters in
                // llama_engine.py, build_filter in qdrant_store.py); this side never sent one, so
                // nothing was filtered.
                //
                // What it drops is whole thin pages -- nav stubs, cookie policies, near-empty
                // landing pages -- before they take up a topK slot. What it cannot do is strip
                // boilerplate chunks out of a page that is otherwise good: quality_score()
                // (metadata.py, called once per page at llama_nodes.py:51) reads only page-level
                // signals -- total length, title presence, whether blocks parsed -- and the result
                // is stamped identically on every chunk of that page. A cookie banner sitting on a
                // substantial docs page therefore carries that page's score and passes the floor.
                // Chunk-level boilerplate containment is a different mechanism, and this is not it.
                ["minQuality"] = GccPartnerResearchCaps.MinChunkQuality,
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
            var pages = MapChunksToQuoteable(dto?.Chunks, anchorToolLookup, _logger);
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

    public async Task<GeekCrawlerRagPageText?> GetPageTextAsync(
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
                    "Geek-Crawler-Rag page text failed for {PageId}: {Status}",
                    pageId,
                    (int)response.StatusCode);
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<PageTextDto>(JsonOpts, ct)
                .ConfigureAwait(false);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Text))
                return null;
            return new GeekCrawlerRagPageText
            {
                PageId = dto.PageId ?? pageId,
                RunId = dto.RunId ?? runId ?? "",
                Url = dto.Url ?? "",
                FinalUrl = dto.FinalUrl,
                Title = dto.Title,
                Text = dto.Text,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Geek-Crawler-Rag page text threw for {PageId}", pageId);
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

    internal static IReadOnlyList<GccQuoteablePage> MapChunksToQuoteable(
        IReadOnlyList<ChunkDto>? chunks,
        IReadOnlyDictionary<string, string>? anchorToolLookup = null,
        ILogger? logger = null)
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
            var title = group.Select(c => c.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
                        ?? url;

            // Keep the best chunks, then restore reading order among the ones kept.
            //
            // This sorted by ChunkIndex and took the first N, so the cap was a guarantee that
            // top-of-page content survived: navigation, language selectors, hero taglines, cookie
            // notices. The dense, substantive blocks further down the page were evicted by position
            // -- the "top-k returns footers and boilerplate" failure, arriving here rather than at
            // retrieval.
            //
            // Retrieval Score is what actually orders this. QualityScore stands first as a
            // cross-page safety boundary, but within one URL's chunks it is a constant: the indexer
            // scores a page once and stamps every chunk of it with that one value, so the sort is
            // stable on a constant key and Score alone decides which chunks survive the cap. That
            // is the intended outcome -- vector similarity beating position is the whole fix -- but
            // it is not index-time quality scoring doing the work, and reading it that way points
            // the next tuning attempt at a knob that cannot turn.
            var kept = group
                .Where(c => !string.IsNullOrWhiteSpace(c.Text))
                .OrderByDescending(c => c.QualityScore ?? double.MinValue)
                .ThenByDescending(c => c.Score)
                .Take(GccPartnerResearchCaps.MaxParagraphsPerPage)
                .OrderBy(c => c.ChunkIndex)
                .ToList();

            // Anchor-based tool detection. A chunk that links out to a partner's own domain is a
            // chunk about that partner, and the writer is told so in one line. Over `kept`, not
            // `group`, so the work tracks exactly what reaches the prompt.
            foreach (var chunk in kept)
            {
                if (string.IsNullOrWhiteSpace(chunk.EntityName))
                {
                    chunk.EntityName = DetectEntityFromAnchors(chunk.Anchors, anchorToolLookup);
                }
            }

            // Token ceiling, applied in reading order so a truncated page keeps its opening
            // chunks rather than an arbitrary slice. Estimated at the standard English
            // approximation of 4 characters per token, plus a flat 50 for the markup RenderChunk
            // wraps each chunk in: the section header, the entity line, the anchor line.
            var paragraphs = new List<string>();
            int accumulatedTokens = 0;
            foreach (var chunk in kept)
            {
                // Weighed against the capped lengths, not the raw ones. RenderChunk truncates the
                // body and the expanded parent to MaxParagraphChars before either reaches the
                // prompt, so a 40k-char chunk costs the model exactly what a 2k one costs.
                // Measuring the raw string taxes the budget for characters nothing ever sends and
                // evicts chunks that fit.
                var isChild = string.Equals(chunk.ChunkRole, "child", StringComparison.OrdinalIgnoreCase);
                int chunkWeight =
                    Math.Min(chunk.Text?.Length ?? 0, GccPartnerResearchCaps.MaxParagraphChars) / 4;
                int parentWeight = isChild && !string.IsNullOrWhiteSpace(chunk.ParentText)
                    ? Math.Min(chunk.ParentText.Length, GccPartnerResearchCaps.MaxParagraphChars) / 4
                    : 0;

                int trueChunkWeight = chunkWeight + parentWeight + 50;
                if (accumulatedTokens + trueChunkWeight > PromptResearchTokenCeiling)
                {
                    logger?.LogWarning(
                        "Research token threshold reached for {Url}: {Accumulated} estimated tokens "
                        + "across {Rendered} chunks, next chunk needs {Next}, ceiling is {Ceiling}. "
                        + "Dropping the remaining {Dropped} chunks for this page.",
                        url,
                        accumulatedTokens,
                        paragraphs.Count,
                        trueChunkWeight,
                        PromptResearchTokenCeiling,
                        kept.Count - paragraphs.Count);
                    break;
                }

                accumulatedTokens += trueChunkWeight;
                var rendered = RenderChunk(chunk);
                if (!string.IsNullOrWhiteSpace(rendered))
                {
                    paragraphs.Add(rendered);
                }
            }

            if (paragraphs.Count == 0)
                continue;

            pages.Add(new GccQuoteablePage(
                Url: url,
                Title: Truncate(title!, GccPartnerResearchCaps.MaxTitleChars),
                Headings: [],
                Paragraphs: paragraphs,
                PageId: group.Select(c => c.PageId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)),
                SectionTitle: kept.Select(c => c.SectionTitle)
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk));
        }

        return pages;
    }

    /// <summary>
    /// The partner tool a chunk's links point at, or null when they point at nothing the create
    /// declared.
    ///
    /// <para>
    /// This resolves a host and looks it up. It deliberately does not compose a name: the value it
    /// returns was produced by <c>GccRequiredToolMentions.AnchorLookup</c>, which owns the
    /// precedence rule that a brief row's spelling beats a host-derived one. Capitalising a host
    /// here would put "Zoneandco" in front of the writer while the required-mentions block asked
    /// for "Zone &amp; Co", and one partner named two ways in one prompt is how a draft comes back
    /// naming a product that does not exist.
    /// </para>
    ///
    /// <para>
    /// First match in anchor order wins, which is the page's own order and therefore stable across
    /// runs. A chunk linking to two partners is labelled with the one it links to first.
    /// </para>
    /// </summary>
    internal static string? DetectEntityFromAnchors(
        List<GeekCrawlerRagAnchorDto>? anchors,
        IReadOnlyDictionary<string, string>? anchorToolLookup)
    {
        if (anchors is null || anchors.Count == 0)
            return null;
        if (anchorToolLookup is null || anchorToolLookup.Count == 0)
            return null;

        foreach (var anchor in anchors)
        {
            var host = HostFromHref(anchor?.Href);
            if (host.Length == 0)
                continue;

            // Exact host first, then each parent domain: a partner's links are as likely to point at
            // app.dext.com or help.dext.com as at dext.com, and the lookup is keyed on the
            // registrable host the operator entered.
            for (var candidate = host; candidate.Contains('.', StringComparison.Ordinal);)
            {
                if (anchorToolLookup.TryGetValue(candidate, out var name)
                    && !string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim();
                }

                var cut = candidate.IndexOf('.', StringComparison.Ordinal);
                var parent = candidate[(cut + 1)..];
                if (!parent.Contains('.', StringComparison.Ordinal))
                    break;
                candidate = parent;
            }
        }

        return null;
    }

    /// <summary>
    /// The host of an anchor href, lowercased and without a leading "www.". Empty when the href
    /// names no host -- a relative link, a fragment, a mailto:, or anything that will not parse.
    /// Protocol-relative hrefs are real in crawled markup, so "//host/path" is read as https.
    /// </summary>
    private static string HostFromHref(string? href)
    {
        var value = href?.Trim();
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.StartsWith("//", StringComparison.Ordinal))
            value = "https:" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return string.Empty;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return string.Empty;

        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>
    /// One chunk as the writer sees it: the section it belongs to, the surrounding parent block
    /// when this is a child, and the anchors beneath it.
    ///
    /// <para>
    /// This used to be the chunk's text and nothing else, so everything the indexer computed about
    /// where a passage sits was discarded at the last hop -- a child arrived as a sentence with no
    /// surroundings, and the writer had no way to tell a heading's subject matter from a stray
    /// paragraph. The metadata was in the payload the whole time.
    /// </para>
    /// </summary>
    private static string RenderChunk(ChunkDto chunk)
    {
        var body = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(chunk.SectionTitle))
        {
            body.AppendLine($"Section: {chunk.SectionTitle!.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(chunk.EntityName))
        {
            body.AppendLine($"Target Entity Match: {chunk.EntityName!.Trim()}");
        }

        var text = Truncate(chunk.Text!, GccPartnerResearchCaps.MaxParagraphChars);
        var isChild = string.Equals(chunk.ChunkRole, "child", StringComparison.OrdinalIgnoreCase);
        if (isChild && !string.IsNullOrWhiteSpace(chunk.ParentText))
        {
            body.AppendLine($"Context: {Truncate(chunk.ParentText!.Trim(), GccPartnerResearchCaps.MaxParagraphChars)}");
            body.AppendLine($"Specific detail: {text}");
        }
        else
        {
            body.AppendLine(text);
        }

        var anchors = (chunk.Anchors ?? [])
            .Select(a => a?.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(GccPartnerResearchCaps.MaxAnchorsPerChunk)
            .ToList();
        if (anchors.Count > 0)
        {
            body.AppendLine($"Linked from this section: {string.Join(", ", anchors)}");
        }

        return body.ToString().TrimEnd();
    }

    private static string Truncate(string value, int max = 400)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value[..max];
    }

    private sealed class HostIndexResponseDto
    {
        public List<HostIndexResultDto>? Results { get; set; }
    }

    private sealed class HostIndexResultDto
    {
        public string? Url { get; set; }
        public string? Host { get; set; }
        public bool Indexed { get; set; }
        public string? RunId { get; set; }
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

        /// <summary>
        /// Geek-Crawler-Rag scores every chunk at index time (metadata.quality_score) and returns it
        /// on every hit. This side did not read it, so it could not be used to decide which chunks
        /// survive the per-page cap -- and the cap kept whichever came first on the page.
        /// </summary>
        public double? QualityScore { get; set; }

        /// <summary>"parent" or "child" -- the chunk's place in the parent/child pair.</summary>
        public string? ChunkRole { get; set; }

        /// <summary>
        /// The parent block this chunk sits inside, carried in the payload precisely so a matched
        /// child can be expanded without a second fetch. Returned by Geek-Crawler-Rag from 890a8f3.
        /// </summary>
        public string? ParentText { get; set; }

        public string? ChildText { get; set; }

        /// <summary>
        /// The partner tool this chunk is about, resolved here from its anchors.
        ///
        /// <para>
        /// Deliberately not bound to the wire's <c>entityName</c>, hence
        /// <see cref="JsonIgnoreAttribute"/>. The indexer sets that field on every chunk it writes --
        /// <c>entity_from_crawl</c> falls back to the page's normalised host and then to the crawl
        /// type, so it is never null (metadata.py:192-195) -- and a bare host is not a tool
        /// detection. Bound to the wire, this would be non-empty on every chunk, the backfill in
        /// MapChunksToQuoteable would be unreachable, and every chunk would carry a
        /// "Target Entity Match: dext.com" line into the prompt. Unbound, it means one thing: a
        /// partner the create declared was identified from this chunk's own links.
        /// </para>
        /// </summary>
        [JsonIgnore]
        public string? EntityName { get; set; }

        /// <summary>
        /// Link text under the chunk's heading -- what anchor-based tool detection reads.
        ///
        /// <para>
        /// Objects, not strings. Geek-Crawler-Rag returns one <c>{label, href}</c> per anchor
        /// (<c>ChunkAnchor</c>, models.py) because that is how the crawler stores them. Typing this
        /// <c>List&lt;string&gt;</c> made <see cref="JsonSerializer"/> throw on the first chunk that
        /// carried one, and the throw landed in QueryAsync's catch -- so a page with links came back
        /// as <see cref="GeekCrawlerRagQueryResult.Failed"/> rather than as content. The same
        /// mistake on the Python side returned 500 for the same reason.
        /// </para>
        /// </summary>
        public List<GeekCrawlerRagAnchorDto>? Anchors { get; set; }
    }

    /// <summary>
    /// One link under a chunk's heading, exactly as Geek-Crawler-Rag's <c>ChunkAnchor</c> sends it.
    /// </summary>
    internal sealed class GeekCrawlerRagAnchorDto
    {
        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("href")]
        public string? Href { get; init; }
    }

    /// <summary>Mirrors Geek-Crawler-Rag's <c>PageTextResponse</c> (models.py).</summary>
    private sealed class PageTextDto
    {
        public string? PageId { get; set; }
        public string? RunId { get; set; }
        public string? Url { get; set; }
        public string? FinalUrl { get; set; }
        public string? Title { get; set; }
        public string? Text { get; set; }
        public string? Excerpt { get; set; }
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
