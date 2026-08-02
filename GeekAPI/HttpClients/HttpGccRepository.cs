using System.Text;
using System.Text.Json;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.HttpClients;

public class HttpGccRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly ILogger<HttpGccRepository> _logger;

    public HttpGccRepository(HttpClient http, ILogger<HttpGccRepository> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task<GccCreateDto?> GetCreateAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccCreateDto>($"repo/content-creator/creates/{id}", ct);

    public Task<IReadOnlyList<GccCreateDto>> ListCreatesAsync(Guid? clientId, string? ownerUserId, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (clientId is Guid cid && cid != Guid.Empty) q.Add($"clientId={cid}");
        if (!string.IsNullOrWhiteSpace(ownerUserId)) q.Add($"ownerUserId={Uri.EscapeDataString(ownerUserId)}");
        var path = "repo/content-creator/creates" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return GetListAsync<GccCreateDto>(path, ct);
    }

    public Task<GccCreateDto> CreateCreateAsync(CreateGccCreateCommand command, CancellationToken ct = default) =>
        PostAsync<GccCreateDto>("repo/content-creator/creates", command, ct);

    public Task<GccCreateDto> UpdateBriefResearchAsync(
        Guid id,
        UpdateGccCreateBriefResearchCommand command,
        CancellationToken ct = default) =>
        PatchAsync<GccCreateDto>($"repo/content-creator/creates/{id}/brief-research", command, ct);

    public Task<GccArtifactDto?> GetArtifactAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccArtifactDto>($"repo/content-creator/artifacts/{id}", ct);

    public Task<IReadOnlyList<GccArtifactDto>> ListArtifactsAsync(Guid createId, CancellationToken ct = default) =>
        GetListAsync<GccArtifactDto>($"repo/content-creator/artifacts?createId={createId}", ct);

    public Task<GccArtifactDto> CreateArtifactAsync(CreateGccArtifactCommand command, CancellationToken ct = default) =>
        PostAsync<GccArtifactDto>("repo/content-creator/artifacts", command, ct);

    public Task<GccArtifactDto> UpdateArtifactStatusAsync(Guid id, string status, CancellationToken ct = default) =>
        PatchAsync<GccArtifactDto>($"repo/content-creator/artifacts/{id}/status", new { status }, ct);

    public Task<GccArtifactVersionDto?> GetVersionAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccArtifactVersionDto>($"repo/content-creator/versions/{id}", ct);

    public Task<IReadOnlyList<GccArtifactVersionDto>> ListVersionsAsync(Guid artifactId, CancellationToken ct = default) =>
        GetListAsync<GccArtifactVersionDto>($"repo/content-creator/versions?artifactId={artifactId}", ct);

    public Task<GccArtifactVersionDto> CreateVersionAsync(CreateGccArtifactVersionCommand command, CancellationToken ct = default) =>
        PostAsync<GccArtifactVersionDto>("repo/content-creator/versions", command, ct);

    public Task<GccApprovalEventDto> CreateApprovalEventAsync(CreateGccApprovalEventCommand command, CancellationToken ct = default) =>
        PostAsync<GccApprovalEventDto>("repo/content-creator/approval-events", command, ct);

    public Task<GccSiteAnalysisDto?> GetSiteAnalysisAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccSiteAnalysisDto>($"repo/content-creator/site-analyses/{id}", ct);

    public Task<GccSiteAnalysisDto> CreateSiteAnalysisAsync(CreateGccSiteAnalysisCommand command, CancellationToken ct = default) =>
        PostAsync<GccSiteAnalysisDto>("repo/content-creator/site-analyses", command, ct);

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        try
        {
            var res = await _http.GetAsync(path, ct);
            if (!res.IsSuccessStatusCode) return null;
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
        try
        {
            var res = await _http.GetAsync(path, ct);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"GET {path} failed with {(int)res.StatusCode}: {TruncateBody(body)}");
            }

            var json = await res.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? new List<T>();
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET list {Path} failed", path);
            throw;
        }
    }

    private static string TruncateBody(string body) =>
        string.IsNullOrWhiteSpace(body) ? "(empty)" : (body.Length <= 240 ? body : body[..240]);

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
        var res = await _http.PostAsync(path, content, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Empty response from {path}");
    }

    private async Task<T> PatchAsync<T>(string path, object body, CancellationToken ct)
    {
        var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
        var req = new HttpRequestMessage(HttpMethod.Patch, path) { Content = content };
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Empty response from {path}");
    }
}
