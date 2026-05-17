using System.Net;
using System.Net.Http.Json;
using GeekApplication.Entities;
using GeekApplication.Interfaces;

namespace GeekAPI.HttpClients;

public sealed class HttpCaseStudyRepository : ICaseStudyRepository
{
    private readonly HttpClient _http;

    public HttpCaseStudyRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<CaseStudy?> GetByIdAsync(int id)
    {
        var response = await _http.GetAsync($"repo/content/case-studies/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseStudy>();
    }

    public async Task<CaseStudy?> GetBySlugAsync(string slug)
    {
        var response = await _http.GetAsync($"repo/content/case-studies/{Uri.EscapeDataString(slug)}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseStudy>();
    }

    public async Task<IReadOnlyList<CaseStudy>> GetAllAsync()
    {
        var response = await _http.GetAsync("repo/content/case-studies/all");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CaseStudy>>() ?? [];
    }

    public async Task<IReadOnlyList<CaseStudy>> GetPublishedAsync()
    {
        var response = await _http.GetAsync("repo/content/case-studies");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CaseStudy>>() ?? [];
    }

    public async Task AddAsync(CaseStudy caseStudy)
    {
        var response = await _http.PostAsJsonAsync("repo/content/case-studies", caseStudy);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CaseStudy>();
        if (created != null) caseStudy.Id = created.Id;
    }

    public async Task UpdateAsync(CaseStudy caseStudy)
    {
        var response = await _http.PutAsJsonAsync($"repo/content/case-studies/{caseStudy.Id}", caseStudy);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(int id)
    {
        var response = await _http.DeleteAsync($"repo/content/case-studies/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<CaseStudyActor?> GetActorAsync(int caseStudyId, int actorId)
    {
        var response = await _http.GetAsync($"repo/content/case-studies/{caseStudyId}/actors/{actorId}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseStudyActor>();
    }

    public async Task<CaseStudyActor> AddActorAsync(int caseStudyId, CaseStudyActor actor)
    {
        var response = await _http.PostAsJsonAsync($"repo/content/case-studies/{caseStudyId}/actors", actor);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseStudyActor>() ?? actor;
    }

    public async Task<CaseStudyActor?> UpdateActorAsync(int caseStudyId, int actorId, string actorName, string actorRole, int sortOrder)
    {
        var response = await _http.PutAsJsonAsync($"repo/content/case-studies/{caseStudyId}/actors/{actorId}", new { actorName, actorRole, sortOrder });
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseStudyActor>();
    }

    public async Task DeleteActorAsync(int caseStudyId, int actorId)
    {
        var response = await _http.DeleteAsync($"repo/content/case-studies/{caseStudyId}/actors/{actorId}");
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        response.EnsureSuccessStatusCode();
    }

    public async Task<CaseStudyMetric?> GetMetricAsync(int caseStudyId, int metricId)
    {
        var response = await _http.GetAsync($"repo/content/case-studies/{caseStudyId}/metrics/{metricId}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseStudyMetric>();
    }

    public async Task<CaseStudyMetric> AddMetricAsync(int caseStudyId, CaseStudyMetric metric)
    {
        var response = await _http.PostAsJsonAsync($"repo/content/case-studies/{caseStudyId}/metrics", metric);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseStudyMetric>() ?? metric;
    }

    public async Task<CaseStudyMetric?> UpdateMetricAsync(int caseStudyId, int metricId, string metricLabel, string metricValue, string? metricUnit, int sortOrder)
    {
        var response = await _http.PutAsJsonAsync($"repo/content/case-studies/{caseStudyId}/metrics/{metricId}", new { metricLabel, metricValue, metricUnit, sortOrder });
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseStudyMetric>();
    }

    public async Task DeleteMetricAsync(int caseStudyId, int metricId)
    {
        var response = await _http.DeleteAsync($"repo/content/case-studies/{caseStudyId}/metrics/{metricId}");
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        response.EnsureSuccessStatusCode();
    }

    public async Task<CaseStudyEventFlowStep?> GetEventFlowStepAsync(int caseStudyId, int stepId)
    {
        var response = await _http.GetAsync($"repo/content/case-studies/{caseStudyId}/event-flow-steps/{stepId}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseStudyEventFlowStep>();
    }

    public async Task<CaseStudyEventFlowStep> AddEventFlowStepAsync(int caseStudyId, CaseStudyEventFlowStep step)
    {
        var response = await _http.PostAsJsonAsync($"repo/content/case-studies/{caseStudyId}/event-flow-steps", step);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseStudyEventFlowStep>() ?? step;
    }

    public async Task<CaseStudyEventFlowStep?> UpdateEventFlowStepAsync(int caseStudyId, int stepId, int stepNumber, string stepDescription, int? stepActorId)
    {
        var response = await _http.PutAsJsonAsync($"repo/content/case-studies/{caseStudyId}/event-flow-steps/{stepId}", new { stepNumber, stepDescription, stepActorId });
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseStudyEventFlowStep>();
    }

    public async Task DeleteEventFlowStepAsync(int caseStudyId, int stepId)
    {
        var response = await _http.DeleteAsync($"repo/content/case-studies/{caseStudyId}/event-flow-steps/{stepId}");
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        response.EnsureSuccessStatusCode();
    }
}
