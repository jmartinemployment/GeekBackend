using System.Net;
using System.Net.Http.Json;
using GeekApplication.Entities;
using GeekApplication.Interfaces;

namespace GeekAPI.HttpClients;

public sealed class HttpDepartmentRepository : IDepartmentRepository
{
    private readonly HttpClient _http;

    public HttpDepartmentRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<Department?> GetByIdAsync(int id)
    {
        var response = await _http.GetAsync($"repo/content/departments/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Department>();
    }

    public async Task<IReadOnlyList<Department>> GetAllAsync()
    {
        var response = await _http.GetAsync("repo/content/departments");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Department>>() ?? [];
    }

    public async Task<IReadOnlyList<Department>> GetWithUseCasesAndCaseStudiesAsync()
    {
        var response = await _http.GetAsync("repo/content/departments/with-use-cases");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Department>>() ?? [];
    }

    public async Task AddAsync(Department department)
    {
        var response = await _http.PostAsJsonAsync("repo/content/departments", department);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<Department>();
        if (created != null) department.Id = created.Id;
    }

    public async Task UpdateAsync(Department department)
    {
        var response = await _http.PutAsJsonAsync($"repo/content/departments/{department.Id}", department);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(int id)
    {
        var response = await _http.DeleteAsync($"repo/content/departments/{id}");
        response.EnsureSuccessStatusCode();
    }
}
