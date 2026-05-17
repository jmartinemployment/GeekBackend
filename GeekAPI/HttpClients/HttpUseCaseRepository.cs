using System.Net;
using System.Net.Http.Json;
using GeekApplication.Entities;
using GeekApplication.Interfaces;

namespace GeekAPI.HttpClients;

public sealed class HttpUseCaseRepository : IUseCaseRepository
{
    private readonly HttpClient _http;

    public HttpUseCaseRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<UseCase?> GetByIdAsync(int id)
    {
        var response = await _http.GetAsync($"repo/content/use-cases/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UseCase>();
    }

    public async Task<UseCase?> GetBySlugAsync(string slug)
    {
        var response = await _http.GetAsync($"repo/content/use-cases/by-slug/{Uri.EscapeDataString(slug)}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UseCase>();
    }

    public async Task<IReadOnlyList<UseCase>> GetByDepartmentIdAsync(int departmentId)
    {
        var response = await _http.GetAsync($"repo/content/use-cases/by-department/{departmentId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<UseCase>>() ?? [];
    }

    public async Task<IReadOnlyList<UseCase>> GetByCaseStudyIdAsync(int caseStudyId)
    {
        var response = await _http.GetAsync($"repo/content/use-cases/by-case-study/{caseStudyId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<UseCase>>() ?? [];
    }

    public async Task<IReadOnlyList<UseCase>> GetAllAsync()
    {
        var response = await _http.GetAsync("repo/content/use-cases");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<UseCase>>() ?? [];
    }

    public async Task AddAsync(UseCase useCase)
    {
        var response = await _http.PostAsJsonAsync("repo/content/use-cases", useCase);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<UseCase>();
        if (created != null) useCase.Id = created.Id;
    }

    public async Task UpdateAsync(UseCase useCase)
    {
        var response = await _http.PutAsJsonAsync($"repo/content/use-cases/{useCase.Id}", useCase);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(int id)
    {
        var response = await _http.DeleteAsync($"repo/content/use-cases/{id}");
        response.EnsureSuccessStatusCode();
    }
}
