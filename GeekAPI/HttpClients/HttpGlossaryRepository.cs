using System.Net;
using System.Net.Http.Json;
using GeekApplication.Interfaces;
using GeekApplication.Models.Glossary;

namespace GeekAPI.HttpClients;

public sealed class HttpGlossaryRepository : IGlossaryRepository
{
    private readonly HttpClient _http;

    public HttpGlossaryRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<IReadOnlyList<GlossaryTermSummaryDto>> GetAllPublishedAsync(
        CancellationToken ct = default)
    {
        var response = await _http.GetAsync("repo/content/glossary", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<GlossaryTermSummaryDto>>(ct) ?? [];
    }

    public async Task<GlossaryTermDto?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(
            $"repo/content/glossary/{Uri.EscapeDataString(slug)}",
            ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GlossaryTermDto>(ct);
    }

    public async Task<GlossaryTermDto> CreateAsync(
        GlossaryTermWriteRequest request,
        CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("repo/content/glossary", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GlossaryTermDto>(ct))!;
    }

    public async Task<GlossaryTermDto?> UpdateAsync(
        string slug,
        GlossaryTermWriteRequest request,
        CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"repo/content/glossary/{Uri.EscapeDataString(slug)}",
            request,
            ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GlossaryTermDto>(ct);
    }

    public async Task<bool> DeleteAsync(string slug, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync(
            $"repo/content/glossary/{Uri.EscapeDataString(slug)}",
            ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }
}
