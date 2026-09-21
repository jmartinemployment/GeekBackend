using System.Text;
using System.Text.Json;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.HttpClients;

public class HttpGccRepository : IGccProjectReader
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

    /// <summary>
    /// Delete a create and everything beneath it. False when the repository had no such create,
    /// which is not an error to the caller: the goal state is "gone".
    /// </summary>
    public Task<bool> DeleteCreateAsync(Guid id, CancellationToken ct = default) =>
        DeleteAsync($"repo/content-creator/creates/{id}", ct);

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

    public Task<GccSiteAnalysisDto?> GetLatestSiteAnalysisByDomainAsync(string domain, CancellationToken ct = default) =>
        GetAsync<GccSiteAnalysisDto>($"repo/content-creator/site-analyses/by-domain/{Uri.EscapeDataString(domain)}", ct);

    public async Task<bool> HasReadySiteAnalysisAsync(CancellationToken ct = default)
    {
        var body = await GetAsync<SiteAnalysisReadyDto>("repo/content-creator/site-analyses/ready", ct);
        return body?.Ready == true;
    }

    private sealed record SiteAnalysisReadyDto(bool Ready);

    public Task<GccSiteAnalysisDto> CreateSiteAnalysisAsync(CreateGccSiteAnalysisCommand command, CancellationToken ct = default) =>
        PostAsync<GccSiteAnalysisDto>("repo/content-creator/site-analyses", command, ct);

    public Task<GccSiteAnalysisDto> UpdateSiteAnalysisAsync(
        Guid id,
        UpdateGccSiteAnalysisCommand command,
        CancellationToken ct = default) =>
        PatchAsync<GccSiteAnalysisDto>($"repo/content-creator/site-analyses/{id}", command, ct);

    public Task<IReadOnlyList<GccSiteFindingDto>> ListSiteFindingsAsync(Guid analysisId, CancellationToken ct = default) =>
        GetListAsync<GccSiteFindingDto>($"repo/content-creator/site-analyses/{analysisId}/findings", ct);

    public Task<IReadOnlyList<GccSiteFindingDto>> ReplaceSiteFindingsAsync(
        Guid analysisId,
        CreateGccSiteFindingsCommand command,
        CancellationToken ct = default) =>
        PutAsync<IReadOnlyList<GccSiteFindingDto>>(
            $"repo/content-creator/site-analyses/{analysisId}/findings",
            command,
            ct);

    public Task<GccClientDto?> GetClientByIdAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccClientDto>($"repo/content-creator/clients/{id}", ct);

    public Task<GccClientDto?> GetClientByNameAsync(string name, CancellationToken ct = default) =>
        GetAsync<GccClientDto>($"repo/content-creator/clients?name={Uri.EscapeDataString(name)}", ct);

    public Task<GccClientDto> CreateClientAsync(CreateGccClientCommand command, CancellationToken ct = default) =>
        PostAsync<GccClientDto>("repo/content-creator/clients", command, ct);

    public Task<IReadOnlyList<GccClientDto>> ListClientsAsync(CancellationToken ct = default) =>
        GetListAsync<GccClientDto>("repo/content-creator/clients/all", ct);

    public Task<GccClientDto> UpdateClientAsync(UpdateGccClientCommand command, CancellationToken ct = default) =>
        PutAsync<GccClientDto>($"repo/content-creator/clients/{command.Id}", command, ct);

    public Task<bool> DeleteClientAsync(Guid id, CancellationToken ct = default) =>
        DeleteAsync($"repo/content-creator/clients/{id}", ct);

    public Task<GccProjectDto?> GetProjectAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccProjectDto>($"repo/content-creator/projects/{id}", ct);

    public Task<IReadOnlyList<GccProjectDto>> ListProjectsByClientAsync(Guid clientId, CancellationToken ct = default) =>
        GetListAsync<GccProjectDto>($"repo/content-creator/projects?clientId={clientId}", ct);

    public Task<IReadOnlyList<GccProjectLogEntryDto>> GetProjectLogAsync(Guid projectId, CancellationToken ct = default) =>
        GetListAsync<GccProjectLogEntryDto>($"repo/content-creator/projects/{projectId}/log", ct);

    /// <summary>
    /// Create a project, carrying a 409 back as a result rather than an exception.
    /// </summary>
    /// <remarks>
    /// The shared <c>PostAsync</c> calls <c>EnsureSuccessStatusCode</c>, which turns the one
    /// status this call has a considered answer for into a thrown exception. A conflicting
    /// idempotency key is a thing the caller must be told about precisely, so it is read from the
    /// response here instead.
    /// </remarks>
    public async Task<GccProjectCreateResult> CreateProjectAsync(
        CreateGccProjectCommand command,
        CancellationToken ct = default)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(command, JsonOpts),
            Encoding.UTF8,
            "application/json");

        var res = await _http.PostAsync("repo/content-creator/projects", content, ct);

        if (res.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogWarning(
                "GeekRepository refused project create for client {ClientId}: idempotency key in use by another client.",
                command.ClientId);
            return GccProjectCreateResult.ConflictingClient();
        }

        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        var project = JsonSerializer.Deserialize<GccProjectDto>(json, JsonOpts)
            ?? throw new InvalidOperationException("Empty response from repo/content-creator/projects");

        return GccProjectCreateResult.Created(project);
    }

    public Task<GccProjectDto> UpdateProjectAsync(UpdateGccProjectCommand command, CancellationToken ct = default) =>
        PutAsync<GccProjectDto>($"repo/content-creator/projects/{command.Id}", command, ct);

    public Task<IReadOnlyList<GccTaskDto>> ListTasksAsync(Guid projectId, CancellationToken ct = default) =>
        GetListAsync<GccTaskDto>($"repo/content-creator/projects/{projectId}/tasks", ct);

    public Task<GccTaskDto> CreateTaskAsync(CreateGccTaskCommand command, CancellationToken ct = default) =>
        PostAsync<GccTaskDto>($"repo/content-creator/projects/{command.ProjectId}/tasks", command, ct);

    public Task<GccTaskDto> UpdateTaskAsync(Guid projectId, UpdateGccTaskCommand command, CancellationToken ct = default) =>
        PutAsync<GccTaskDto>($"repo/content-creator/projects/{projectId}/tasks/{command.Id}", command, ct);

    public Task<IReadOnlyList<GccTimeEntryDto>> ListTimeAsync(Guid projectId, CancellationToken ct = default) =>
        GetListAsync<GccTimeEntryDto>($"repo/content-creator/projects/{projectId}/time", ct);

    public async Task<GccProjectTimeTotals> TimeTotalsAsync(Guid projectId, CancellationToken ct = default) =>
        await GetAsync<GccProjectTimeTotals>($"repo/content-creator/projects/{projectId}/time/totals", ct)
        ?? new GccProjectTimeTotals(0, 0, []);

    /// <summary>
    /// Log time, carrying a refusal back as a reason rather than an exception.
    /// </summary>
    /// <remarks>
    /// "That client has no rate" is the whole point of leaving rate nullable, and the operator has
    /// to read it. EnsureSuccessStatusCode would turn it into a 500 with the reason buried.
    /// </remarks>
    public async Task<GccTimeEntryResult> LogTimeAsync(
        CreateGccTimeEntryCommand command,
        CancellationToken ct = default)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(command, JsonOpts),
            Encoding.UTF8,
            "application/json");

        var res = await _http.PostAsync(
            $"repo/content-creator/projects/{command.ProjectId}/time", content, ct);

        if (res.StatusCode == System.Net.HttpStatusCode.Conflict)
            return GccTimeEntryResult.Refused(await res.Content.ReadAsStringAsync(ct));

        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        var entry = JsonSerializer.Deserialize<GccTimeEntryDto>(json, JsonOpts)
            ?? throw new InvalidOperationException("Empty response logging time.");
        return GccTimeEntryResult.Logged(entry);
    }

    public Task<IReadOnlyList<GccDeliverableDto>> ListDeliverablesAsync(Guid projectId, CancellationToken ct = default) =>
        GetListAsync<GccDeliverableDto>($"repo/content-creator/projects/{projectId}/deliverables", ct);

    /// <summary>
    /// Record a deliverable, carrying a refusal back as a reason rather than an exception.
    /// </summary>
    /// <remarks>
    /// "That create belongs to a different client" is a sentence the operator must read, and
    /// EnsureSuccessStatusCode would turn it into a 500 with the reason buried in a body nobody
    /// looks at.
    /// </remarks>
    public async Task<GccDeliverableResult> CreateDeliverableAsync(
        CreateGccDeliverableCommand command,
        CancellationToken ct = default)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(command, JsonOpts),
            Encoding.UTF8,
            "application/json");

        var res = await _http.PostAsync(
            $"repo/content-creator/projects/{command.ProjectId}/deliverables", content, ct);

        if (res.StatusCode == System.Net.HttpStatusCode.Conflict)
            return GccDeliverableResult.Refused(await res.Content.ReadAsStringAsync(ct));

        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        var deliverable = JsonSerializer.Deserialize<GccDeliverableDto>(json, JsonOpts)
            ?? throw new InvalidOperationException("Empty response recording a deliverable.");
        return GccDeliverableResult.Created(deliverable);
    }

    public Task<GccDeliverableDto> ChangeDeliverableStatusAsync(
        Guid projectId,
        ChangeGccDeliverableStatusCommand command,
        CancellationToken ct = default) =>
        PutAsync<GccDeliverableDto>(
            $"repo/content-creator/projects/{projectId}/deliverables/{command.Id}/status",
            command,
            ct);

    public Task<GccProjectDto> ChangeProjectStatusAsync(
        ChangeGccProjectStatusCommand command,
        CancellationToken ct = default) =>
        PutAsync<GccProjectDto>($"repo/content-creator/projects/{command.Id}/status", command, ct);

    /// <summary>
    /// Soft-delete a project. False when it does not exist or was already deleted — not an error to
    /// the caller, since either way the goal state ("gone from every view") already holds.
    /// </summary>
    public Task<bool> DeleteProjectAsync(Guid id, string actorUserId, CancellationToken ct = default) =>
        DeleteAsync($"repo/content-creator/projects/{id}?actorUserId={Uri.EscapeDataString(actorUserId)}", ct);

    /// <summary>
    /// Delete one log entry, for real — not the project's soft delete above. False when it does not
    /// exist, which includes "already deleted".
    /// </summary>
    public Task<bool> DeleteProjectLogEntryAsync(
        Guid projectId,
        long logEntryId,
        string actorUserId,
        CancellationToken ct = default) =>
        DeleteAsync(
            $"repo/content-creator/projects/{projectId}/log/{logEntryId}?actorUserId={Uri.EscapeDataString(actorUserId)}",
            ct);

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

    private async Task<bool> DeleteAsync(string path, CancellationToken ct)
    {
        var res = await _http.DeleteAsync(path, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        res.EnsureSuccessStatusCode();
        return true;
    }

    private async Task<T> PutAsync<T>(string path, object body, CancellationToken ct)
    {
        var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
        var res = await _http.PutAsync(path, content, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Empty response from {path}");
    }
}
