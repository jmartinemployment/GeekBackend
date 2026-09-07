using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeekApplication.Models.ContentCreator;

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
}

public sealed class GeekCrawlerRagQueryResult
{
    public required Guid RunId { get; init; }
    public required IReadOnlyList<GccQuoteablePage> Pages { get; init; }
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
                Paragraphs: paragraphs));

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
    }
}
