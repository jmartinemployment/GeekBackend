using System.Net.Http.Json;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekAPI.HttpClients.Seo;

public sealed class HttpContentScoringService(IHttpClientFactory factory) : IContentScoringService
{
    private readonly HttpClient _http = factory.CreateClient("GeekRepository");

    public async Task<Result<ContentScoreHubResult>> ProcessContentChangedAsync(
        Guid userId, Guid documentId, string contentHtml, string targetKeyword,
        CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"repo/seo/scoring/process?userId={userId}",
            new { documentId, contentHtml, targetKeyword },
            ct);

        if (!response.IsSuccessStatusCode)
            return Result<ContentScoreHubResult>.Failure(await response.Content.ReadAsStringAsync(ct));

        var value = await response.Content.ReadFromJsonAsync<ContentScoreHubResult>(ct);
        return value is null
            ? Result<ContentScoreHubResult>.Failure("Empty scoring response")
            : Result<ContentScoreHubResult>.Success(value);
    }

    public async Task<Result<ContentScoreHubResult>> ProcessKeywordChangedAsync(
        Guid userId, Guid documentId, string contentHtml, string targetKeyword, string targetLocation,
        CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"repo/seo/scoring/keyword-changed?userId={userId}",
            new { documentId, contentHtml, targetKeyword, targetLocation },
            ct);

        if (!response.IsSuccessStatusCode)
            return Result<ContentScoreHubResult>.Failure(await response.Content.ReadAsStringAsync(ct));

        var value = await response.Content.ReadFromJsonAsync<ContentScoreHubResult>(ct);
        return value is null
            ? Result<ContentScoreHubResult>.Failure("Empty scoring response")
            : Result<ContentScoreHubResult>.Success(value);
    }

    public async Task<Result<AutoOptimizeResult>> AutoOptimizeAsync(
        Guid userId, Guid documentId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync(
            $"repo/seo/content/{documentId}/auto-optimize?userId={userId}", null, ct);
        if (!response.IsSuccessStatusCode)
            return Result<AutoOptimizeResult>.Failure(await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<AutoOptimizeResult>(ct);
        return value is null
            ? Result<AutoOptimizeResult>.Failure("Empty response")
            : Result<AutoOptimizeResult>.Success(value);
    }
}
