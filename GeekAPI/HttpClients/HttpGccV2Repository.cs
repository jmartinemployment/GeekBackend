using System.Net;
using System.Text;
using System.Text.Json;

namespace GeekAPI.HttpClients;

/// <summary>
/// GeekAPI's client to GeekRepository's <c>repo/content-creator-v2/*</c> routes.
/// Mirrors <see cref="HttpGccRepository"/>'s style; entirely separate from v1.
/// </summary>
public class HttpGccV2Repository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly ILogger<HttpGccV2Repository> _logger;

    public HttpGccV2Repository(HttpClient http, ILogger<HttpGccV2Repository> logger)
    {
        _http = http;
        _logger = logger;
    }

    // Creates

    public Task<GccV2CreateDto?> GetCreateAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2CreateDto>($"repo/content-creator-v2/creates/{id}", ct);

    public Task<IReadOnlyList<GccV2CreateDto>> ListCreatesAsync(string? ownerUserId, CancellationToken ct = default)
    {
        var path = "repo/content-creator-v2/creates" +
            (string.IsNullOrWhiteSpace(ownerUserId) ? "" : $"?ownerUserId={Uri.EscapeDataString(ownerUserId)}");
        return GetListAsync<GccV2CreateDto>(path, ct);
    }

    public Task<GccV2CreateDto> CreateCreateAsync(CreateGccV2CreateCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2CreateDto>("repo/content-creator-v2/creates", command, ct);

    // Briefs

    public Task<GccV2BriefDto?> GetBriefAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2BriefDto>($"repo/content-creator-v2/briefs/{id}", ct);

    public Task<GccV2BriefDto> CreateBriefAsync(CreateGccV2BriefCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2BriefDto>("repo/content-creator-v2/briefs", command, ct);

    // Jobs

    public Task<GccV2JobDto?> GetJobAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2JobDto>($"repo/content-creator-v2/jobs/{id}", ct);

    /// <summary>Latest job for a create — lets Canvas routes (keyed by create id) resolve "the job"
    /// without the caller already knowing its id.</summary>
    public Task<GccV2JobDto?> GetLatestJobByCreateAsync(Guid createId, CancellationToken ct = default) =>
        GetAsync<GccV2JobDto>($"repo/content-creator-v2/jobs/by-create/{createId}", ct);

    public Task<IReadOnlyList<GccV2JobDto>> ListJobsByCreateAsync(Guid createId, CancellationToken ct = default) =>
        GetListAsync<GccV2JobDto>($"repo/content-creator-v2/jobs/list-by-create/{createId}", ct);

    public Task<IReadOnlyList<GccV2JobDto>> GetJobsByStatusAsync(
        string status,
        DateTimeOffset? leaseBefore = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        var q = new List<string> { $"limit={limit}" };
        if (leaseBefore is not null) q.Add($"leaseBefore={Uri.EscapeDataString(leaseBefore.Value.ToString("O"))}");
        return GetListAsync<GccV2JobDto>($"repo/content-creator-v2/jobs/by-status/{status}?{string.Join("&", q)}", ct);
    }

    public Task<GccV2JobDto> CreateJobAsync(CreateGccV2JobCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2JobDto>("repo/content-creator-v2/jobs", command, ct);

    public Task<GccV2JobDto> PatchJobAsync(Guid id, PatchGccV2JobCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2JobDto>($"repo/content-creator-v2/jobs/{id}", command, ct);

    /// <summary>Atomically patch the job and/or append one event in a single DB transaction.</summary>
    public Task<GccV2JobTransitionResultDto> ApplyJobTransitionAsync(
        Guid id,
        ApplyGccV2JobTransitionCommand command,
        CancellationToken ct = default) =>
        PostAsync<GccV2JobTransitionResultDto>($"repo/content-creator-v2/jobs/{id}/transition", command, ct);

    /// <summary>Claims the job if pending or its lease expired. Null when not claimable (409).</summary>
    public async Task<GccV2JobDto?> ClaimJobAsync(Guid id, string instanceId, int leaseSeconds = 120, CancellationToken ct = default)
    {
        var path = $"repo/content-creator-v2/jobs/{id}/claim?instanceId={Uri.EscapeDataString(instanceId)}&leaseSeconds={leaseSeconds}";
        var res = await _http.PostAsync(path, content: null, ct);
        if (res.StatusCode == HttpStatusCode.Conflict) return null;
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<GccV2JobDto>(json, JsonOpts);
    }

    public Task<IReadOnlyList<GccV2JobEventDto>> GetJobEventsAsync(Guid id, int afterSeq = 0, CancellationToken ct = default) =>
        GetListAsync<GccV2JobEventDto>($"repo/content-creator-v2/jobs/{id}/events?afterSeq={afterSeq}", ct);

    public Task<GccV2JobEventDto> AppendJobEventAsync(Guid id, AppendGccV2JobEventCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2JobEventDto>($"repo/content-creator-v2/jobs/{id}/events", command, ct);

    public Task<IReadOnlyList<GccV2StageResultDto>> GetStageResultsAsync(Guid id, CancellationToken ct = default) =>
        GetListAsync<GccV2StageResultDto>($"repo/content-creator-v2/jobs/{id}/stage-results", ct);

    public Task<GccV2StageResultDto> AddStageResultAsync(Guid id, CreateGccV2StageResultCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2StageResultDto>($"repo/content-creator-v2/jobs/{id}/stage-results", command, ct);

    // Brand kits

    public Task<GccV2BrandKitDto?> GetBrandKitAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2BrandKitDto>($"repo/content-creator-v2/brand-kits/{id}", ct);

    /// <summary>Latest-first; callers typically want <c>.FirstOrDefault()</c> for "the current kit".</summary>
    public Task<IReadOnlyList<GccV2BrandKitDto>> ListBrandKitsByProfileAsync(Guid derivedFromProfileId, CancellationToken ct = default) =>
        GetListAsync<GccV2BrandKitDto>($"repo/content-creator-v2/brand-kits?derivedFromProfileId={derivedFromProfileId}", ct);

    public Task<GccV2BrandKitDto> CreateBrandKitAsync(CreateGccV2BrandKitCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2BrandKitDto>("repo/content-creator-v2/brand-kits", command, ct);

    public Task<GccV2BrandKitDto> PatchBrandKitAsync(Guid id, PatchGccV2BrandKitCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2BrandKitDto>($"repo/content-creator-v2/brand-kits/{id}", command, ct);

    // Outlines

    public Task<GccV2OutlineDto?> GetOutlineAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2OutlineDto>($"repo/content-creator-v2/outlines/{id}", ct);

    public Task<IReadOnlyList<GccV2OutlineDto>> ListOutlinesByBriefAsync(Guid briefId, CancellationToken ct = default) =>
        GetListAsync<GccV2OutlineDto>($"repo/content-creator-v2/outlines?briefId={briefId}", ct);

    public Task<GccV2OutlineDto> CreateOutlineAsync(CreateGccV2OutlineCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2OutlineDto>("repo/content-creator-v2/outlines", command, ct);

    public Task<GccV2OutlineDto> PatchOutlineAsync(Guid id, PatchGccV2OutlineCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2OutlineDto>($"repo/content-creator-v2/outlines/{id}", command, ct);

    // Guardrail rules

    public Task<IReadOnlyList<GccV2GuardrailRuleDto>> ListGuardrailRulesAsync(bool? enabled = true, CancellationToken ct = default)
    {
        var path = "repo/content-creator-v2/guardrail-rules" + (enabled is null ? "" : $"?enabled={enabled.Value.ToString().ToLowerInvariant()}");
        return GetListAsync<GccV2GuardrailRuleDto>(path, ct);
    }

    public Task<int> SeedDefaultGuardrailRulesAsync(CancellationToken ct = default)
    {
        return PostSeedCountAsync("repo/content-creator-v2/guardrail-rules/seed-defaults", ct);
    }

    // Publish records

    public Task<GccV2PublishRecordDto?> GetPublishRecordAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2PublishRecordDto>($"repo/content-creator-v2/publish-records/{id}", ct);

    /// <summary>Latest-first; the frontend renders these as a publish history for the create.</summary>
    public Task<IReadOnlyList<GccV2PublishRecordDto>> ListPublishRecordsByCreateAsync(Guid createId, CancellationToken ct = default) =>
        GetListAsync<GccV2PublishRecordDto>($"repo/content-creator-v2/publish-records?createId={createId}", ct);

    public Task<GccV2PublishRecordDto> CreatePublishRecordAsync(CreateGccV2PublishRecordCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2PublishRecordDto>("repo/content-creator-v2/publish-records", command, ct);

    public Task<GccV2PublishRecordDto> PatchPublishRecordAsync(Guid id, PatchGccV2PublishRecordCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2PublishRecordDto>($"repo/content-creator-v2/publish-records/{id}", command, ct);

    // AI-visibility snapshots

    public Task<GccV2AiVisibilitySnapshotDto?> GetLatestAiVisibilitySnapshotAsync(Guid createId, CancellationToken ct = default) =>
        GetAsync<GccV2AiVisibilitySnapshotDto>($"repo/content-creator-v2/ai-visibility-snapshots/latest?createId={createId}", ct);

    /// <summary>Latest-first; the frontend renders these as an AI-visibility history for the create.</summary>
    public Task<IReadOnlyList<GccV2AiVisibilitySnapshotDto>> ListAiVisibilitySnapshotsByCreateAsync(Guid createId, CancellationToken ct = default) =>
        GetListAsync<GccV2AiVisibilitySnapshotDto>($"repo/content-creator-v2/ai-visibility-snapshots?createId={createId}", ct);

    public Task<GccV2AiVisibilitySnapshotDto> CreateAiVisibilitySnapshotAsync(CreateGccV2AiVisibilitySnapshotCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2AiVisibilitySnapshotDto>("repo/content-creator-v2/ai-visibility-snapshots", command, ct);

    public Task<GccV2PartnerResearchRecordDto?> GetFreshPartnerResearchAsync(
        string targetUrl,
        int withinHours = 24,
        CancellationToken ct = default)
    {
        var path =
            $"repo/content-creator-v2/partner-research-records/fresh?targetUrl={Uri.EscapeDataString(targetUrl)}&withinHours={withinHours}";
        return GetAsync<GccV2PartnerResearchRecordDto>(path, ct);
    }

    public Task<GccV2PartnerResearchRecordDto> CreatePartnerResearchRecordAsync(
        CreateGccV2PartnerResearchRecordCommand command,
        CancellationToken ct = default) =>
        PostAsync<GccV2PartnerResearchRecordDto>("repo/content-creator-v2/partner-research-records", command, ct);

    private async Task<int> PostSeedCountAsync(string path, CancellationToken ct)
    {
        var res = await _http.PostAsync(path, content: null, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("seeded", out var seeded) ? seeded.GetInt32() : 0;
    }

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
                throw new HttpRequestException($"GET {path} failed with {(int)res.StatusCode}: {TruncateBody(body)}");
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
