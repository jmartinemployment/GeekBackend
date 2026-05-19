using System.Net;
using System.Net.Http.Json;
using GeekAPI.Auth;
using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekAPI.HttpClients.Seo;

public sealed class HttpContentDocumentRepository(IHttpClientFactory factory, ICurrentUserContext user) : IContentDocumentRepository
{
    private readonly HttpClient _http = factory.CreateClient("GeekRepository");

    public async Task<Result<SeoContentDocument>> GetByIdAsync(Guid documentId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"repo/seo/content/{documentId}?userId={user.UserId}", ct);
        return await ReadOneAsync(response, ct);
    }

    public async Task<Result<IReadOnlyList<SeoContentDocument>>> GetByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"repo/seo/content?userId={user.UserId}&projectId={projectId}", ct);
        if (!response.IsSuccessStatusCode)
            return Result<IReadOnlyList<SeoContentDocument>>.Failure(await response.Content.ReadAsStringAsync(ct));
        var list = await response.Content.ReadFromJsonAsync<List<SeoContentDocument>>(ct);
        return Result<IReadOnlyList<SeoContentDocument>>.Success(list ?? []);
    }

    public async Task<Result<SeoContentDocument>> CreateAsync(
        Guid userId, CreateContentDocumentRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"repo/seo/content?userId={userId}", request, ct);
        return await ReadOneAsync(response, ct);
    }

    public async Task<Result<SeoContentDocument>> UpdateContentAsync(
        Guid documentId, UpdateContentRequest request, int wordCount, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"repo/seo/content/{documentId}/content?userId={user.UserId}", request, ct);
        return await ReadOneAsync(response, ct);
    }

    public async Task<Result<SeoContentDocument>> UpdateStatusAsync(
        Guid documentId, string status, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync(
            $"repo/seo/content/{documentId}/status?userId={user.UserId}", new { status }, ct);
        return await ReadOneAsync(response, ct);
    }

    public async Task<Result> UpdateScoreAsync(
        Guid documentId, int score, string scoreComponentsJson, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"repo/seo/content/{documentId}/score",
            new { score, scoreComponentsJson },
            ct);
        return response.IsSuccessStatusCode
            ? Result.Success()
            : Result.Failure(await response.Content.ReadAsStringAsync(ct));
    }

    public Task<Result> UpdateAiDetectionScoreAsync(Guid documentId, decimal score, CancellationToken ct = default) =>
        Task.FromResult(Result.Success());

    public async Task<Result> DeleteAsync(Guid documentId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"repo/seo/content/{documentId}?userId={user.UserId}", ct);
        return response.IsSuccessStatusCode
            ? Result.Success()
            : Result.Failure(await response.Content.ReadAsStringAsync(ct));
    }

    private static async Task<Result<SeoContentDocument>> ReadOneAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Result<SeoContentDocument>.NotFound("Document not found");
        if (!response.IsSuccessStatusCode)
            return Result<SeoContentDocument>.Failure(await response.Content.ReadAsStringAsync(ct));
        var doc = await response.Content.ReadFromJsonAsync<SeoContentDocument>(ct);
        return doc is null
            ? Result<SeoContentDocument>.Failure("Empty response")
            : Result<SeoContentDocument>.Success(doc);
    }
}
