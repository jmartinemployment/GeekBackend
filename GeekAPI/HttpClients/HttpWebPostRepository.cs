using System.Net;
using System.Net.Http.Json;
using GeekApplication.Interfaces;
using GeekApplication.Models.WebPost;

namespace GeekAPI.HttpClients;

public sealed class HttpWebPostRepository : IWebPostRepository
{
    private readonly HttpClient _http;

    public HttpWebPostRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public async Task<WebPostFlatDto?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"repo/content/webposts/{Uri.EscapeDataString(slug)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WebPostFlatDto>(ct);
    }

    public async Task<WebPostFlatDto> UpsertAsync(UpsertWebPostCommand command, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("repo/content/webposts", command, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WebPostFlatDto>(ct))!;
    }

    public async Task<bool> DeleteAsync(string slug, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"repo/content/webposts/{Uri.EscapeDataString(slug)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }
}
