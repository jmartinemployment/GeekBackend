using System.Net.Http.Json;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekAPI.HttpClients.Seo;

public sealed class HttpWordPressPublishService(IHttpClientFactory factory) : IWordPressPublishService
{
    private readonly HttpClient _http = factory.CreateClient("GeekRepository");

    public async Task<Result> ConnectAsync(
        Guid userId, Guid projectId, WordPressConnectRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"repo/seo/wordpress/connect?userId={userId}&projectId={projectId}", request, ct);
        return response.IsSuccessStatusCode
            ? Result.Success()
            : Result.Failure(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<Result<WordPressPublishResult>> PublishDocumentAsync(
        Guid userId, Guid documentId, WordPressPublishOptions options, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"repo/seo/wordpress/publish?userId={userId}&documentId={documentId}", options, ct);
        if (!response.IsSuccessStatusCode)
            return Result<WordPressPublishResult>.Failure(await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<WordPressPublishResult>(ct);
        return value is null
            ? Result<WordPressPublishResult>.Failure("Empty response")
            : Result<WordPressPublishResult>.Success(value);
    }

    public async Task<Result> DisconnectAsync(Guid userId, Guid projectId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"repo/seo/wordpress/{projectId}?userId={userId}", ct);
        return response.IsSuccessStatusCode
            ? Result.Success()
            : Result.Failure(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<Result<WordPressConnectionStatus>> GetStatusAsync(
        Guid userId, Guid projectId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"repo/seo/wordpress/{projectId}/status?userId={userId}", ct);
        if (!response.IsSuccessStatusCode)
            return Result<WordPressConnectionStatus>.Failure(await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<WordPressConnectionStatus>(ct);
        return value is null
            ? Result<WordPressConnectionStatus>.Failure("Empty response")
            : Result<WordPressConnectionStatus>.Success(value);
    }
}
