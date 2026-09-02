using System.Text;
using System.Text.Json;

namespace GeekAPI.HttpClients;

public sealed class HttpGeekCrawlerRepository : IGeekCrawlerResumeRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly ILogger<HttpGeekCrawlerRepository> _logger;

    public HttpGeekCrawlerRepository(HttpClient http, ILogger<HttpGeekCrawlerRepository> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task<GeekCrawlerRunDto?> GetRunAsync(Guid runId, CancellationToken ct = default) =>
        GetAsync<GeekCrawlerRunDto>($"repo/geek-crawler/runs/{runId}", ct);

    public Task<IReadOnlyList<GeekCrawlerRunDto>> ListRunsForUserAsync(
        string ownerUserId,
        string? crawlType = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        var path = new StringBuilder(
            $"repo/geek-crawler/runs/for-user?ownerUserId={Uri.EscapeDataString(ownerUserId)}&limit={limit}");
        if (!string.IsNullOrWhiteSpace(crawlType))
            path.Append($"&crawlType={Uri.EscapeDataString(crawlType)}");
        return GetListAsync<GeekCrawlerRunDto>(path.ToString(), ct);
    }

    public Task<GeekCrawlerRunDto?> GetLatestRunAsync(
        string ownerUserId,
        string crawlType,
        string seedsJson,
        CancellationToken ct = default) =>
        GetAsync<GeekCrawlerRunDto>(
            $"repo/geek-crawler/runs/latest?ownerUserId={Uri.EscapeDataString(ownerUserId)}" +
            $"&crawlType={Uri.EscapeDataString(crawlType)}" +
            $"&seedsJson={Uri.EscapeDataString(seedsJson)}",
            ct);

    public Task<IReadOnlyList<GeekCrawlerRunDto>> GetRunsByStatusAsync(
        string status,
        int limit = 200,
        CancellationToken ct = default) =>
        GetListAsync<GeekCrawlerRunDto>(
            $"repo/geek-crawler/runs/by-status/{Uri.EscapeDataString(status)}?limit={limit}",
            ct);

    public Task<GeekCrawlerRunDto> CreateRunAsync(
        CreateGeekCrawlerRunCommand command,
        CancellationToken ct = default) =>
        PostAsync<GeekCrawlerRunDto>("repo/geek-crawler/runs", command, ct);

    public Task<GeekCrawlerRunDto> PatchRunAsync(
        Guid runId,
        PatchGeekCrawlerRunCommand command,
        CancellationToken ct = default) =>
        PatchAsync<GeekCrawlerRunDto>($"repo/geek-crawler/runs/{runId}", command, ct);

    public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesAsync(
        Guid runId,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default) =>
        GetListAsync<GeekCrawlerPageDto>(
            $"repo/geek-crawler/pages?runId={runId}&limit={limit}&offset={offset}",
            ct);

    public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
        Guid runId,
        IReadOnlyList<string> seedUrls,
        CancellationToken ct = default)
    {
        if (seedUrls.Count == 0)
            return Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);

        var seedsParam = string.Join(",", seedUrls.Select(Uri.EscapeDataString));
        return GetListAsync<GeekCrawlerPageDto>(
            $"repo/geek-crawler/pages/by-seeds?runId={runId}&seeds={seedsParam}",
            ct);
    }

    public Task<GeekCrawlerRunDto?> GetRunForSlotAsync(
        string ownerUserId,
        string crawlType,
        string seedKey,
        CancellationToken ct = default) =>
        GetAsync<GeekCrawlerRunDto>(
            $"repo/geek-crawler/runs/for-slot?ownerUserId={Uri.EscapeDataString(ownerUserId)}" +
            $"&crawlType={Uri.EscapeDataString(crawlType)}" +
            $"&seedKey={Uri.EscapeDataString(seedKey)}",
            ct);

    public Task<IReadOnlyList<GeekCrawlerPageResumeRowDto>> ListPagesForResumeAsync(
        Guid runId,
        int limit = 500,
        int offset = 0,
        CancellationToken ct = default) =>
        GetListAsync<GeekCrawlerPageResumeRowDto>(
            $"repo/geek-crawler/pages/for-resume?runId={runId}&limit={limit}&offset={offset}",
            ct);

    public Task<GeekCrawlerPageActivityDto?> GetPageActivityAsync(
        Guid runId,
        CancellationToken ct = default) =>
        GetAsync<GeekCrawlerPageActivityDto>($"repo/geek-crawler/pages/activity?runId={runId}", ct);

    public Task<GeekCrawlerPageBatchResult> CreatePagesBatchAsync(
        CreateGeekCrawlerPageBatchCommand command,
        CancellationToken ct = default) =>
        PostAsync<GeekCrawlerPageBatchResult>("repo/geek-crawler/pages/batch", command, ct);

    public Task<IReadOnlyList<GeekCrawlerLinkDto>> ListLinksAsync(
        Guid runId,
        bool? sameOrigin = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        var path = new StringBuilder(
            $"repo/geek-crawler/links?runId={runId}&limit={limit}&offset={offset}");
        if (sameOrigin is not null)
            path.Append($"&sameOrigin={sameOrigin.Value.ToString().ToLowerInvariant()}");
        return GetListAsync<GeekCrawlerLinkDto>(path.ToString(), ct);
    }

    public Task<IReadOnlyList<GeekCrawlerLinkResumeRowDto>> ListLinksForResumeAsync(
        Guid runId,
        int limit = 500,
        DateTimeOffset? afterDiscoveredAtUtc = null,
        Guid? afterId = null,
        CancellationToken ct = default)
    {
        var path = new StringBuilder(
            $"repo/geek-crawler/links/for-resume?runId={runId}&limit={limit}");
        if (afterDiscoveredAtUtc is not null && afterId is not null)
        {
            path.Append($"&afterDiscoveredAtUtc={Uri.EscapeDataString(afterDiscoveredAtUtc.Value.ToString("O"))}");
            path.Append($"&afterId={afterId.Value:D}");
        }

        return GetListAsync<GeekCrawlerLinkResumeRowDto>(path.ToString(), ct);
    }

    public Task<GeekCrawlerLinkActivityDto?> GetLinkActivityAsync(
        Guid runId,
        CancellationToken ct = default) =>
        GetAsync<GeekCrawlerLinkActivityDto>($"repo/geek-crawler/links/activity?runId={runId}", ct);

    public Task CreateLinksBatchAsync(
        CreateGeekCrawlerLinkBatchCommand command,
        CancellationToken ct = default) =>
        PostAsync<object>("repo/geek-crawler/links/batch", command, ct);

    public async Task ClearRunCrawlDataAsync(Guid runId, CancellationToken ct = default)
    {
        var res = await _http.DeleteAsync($"repo/geek-crawler/runs/{runId}/crawl-data", ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            return;
        res.EnsureSuccessStatusCode();
    }

    public Task<GeekCrawlerScheduleDto?> GetScheduleAsync(Guid scheduleId, CancellationToken ct = default) =>
        GetAsync<GeekCrawlerScheduleDto>($"repo/geek-crawler/schedules/{scheduleId}", ct);

    public async Task<GeekCrawlerScheduleDto?> ClaimScheduleAsync(
        Guid scheduleId,
        ClaimGeekCrawlerScheduleCommand command,
        CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(command, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var res = await _http.PostAsync($"repo/geek-crawler/schedules/{scheduleId}/claim", content, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        res.EnsureSuccessStatusCode();
        var responseJson = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<GeekCrawlerScheduleDto>(responseJson, JsonOpts);
    }

    public Task<IReadOnlyList<GeekCrawlerScheduleDto>> ListDueSchedulesAsync(
        DateTimeOffset beforeUtc,
        int limit = 50,
        CancellationToken ct = default) =>
        GetListAsync<GeekCrawlerScheduleDto>(
            $"repo/geek-crawler/schedules/due?beforeUtc={Uri.EscapeDataString(beforeUtc.ToString("O"))}&limit={limit}",
            ct);

    public Task<IReadOnlyList<GeekCrawlerScheduleDto>> ListSchedulesForUserAsync(
        string ownerUserId,
        string? crawlType = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        var path = new StringBuilder(
            $"repo/geek-crawler/schedules/for-user?ownerUserId={Uri.EscapeDataString(ownerUserId)}&limit={limit}");
        if (!string.IsNullOrWhiteSpace(crawlType))
            path.Append($"&crawlType={Uri.EscapeDataString(crawlType)}");
        return GetListAsync<GeekCrawlerScheduleDto>(path.ToString(), ct);
    }

    public Task<GeekCrawlerScheduleDto> CreateScheduleAsync(
        CreateGeekCrawlerScheduleCommand command,
        CancellationToken ct = default) =>
        PostAsync<GeekCrawlerScheduleDto>("repo/geek-crawler/schedules", command, ct);

    public Task<GeekCrawlerScheduleDto> PatchScheduleAsync(
        Guid scheduleId,
        PatchGeekCrawlerScheduleCommand command,
        CancellationToken ct = default) =>
        PatchAsync<GeekCrawlerScheduleDto>($"repo/geek-crawler/schedules/{scheduleId}", command, ct);

    public async Task DeleteScheduleAsync(Guid scheduleId, CancellationToken ct = default)
    {
        var res = await _http.DeleteAsync($"repo/geek-crawler/schedules/{scheduleId}", ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            return;
        res.EnsureSuccessStatusCode();
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        try
        {
            var res = await _http.GetAsync(path, ct);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET {Path} failed", path);
            throw;
        }
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken ct)
    {
        var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"GET {path} failed with {(int)res.StatusCode}: {TruncateBody(body)}");
        }

        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? [];
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var res = await _http.PostAsync(path, content, ct);
        res.EnsureSuccessStatusCode();
        var responseJson = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(responseJson, JsonOpts)
               ?? throw new InvalidOperationException($"Empty response from POST {path}");
    }

    private async Task<T> PatchAsync<T>(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var res = await _http.PatchAsync(path, content, ct);
        res.EnsureSuccessStatusCode();
        var responseJson = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(responseJson, JsonOpts)
               ?? throw new InvalidOperationException($"Empty response from PATCH {path}");
    }

    private static string TruncateBody(string body) =>
        body.Length <= 500 ? body : body[..500] + "…";
}
