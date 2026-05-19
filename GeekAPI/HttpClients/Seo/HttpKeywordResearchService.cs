using System.Net.Http.Json;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekAPI.HttpClients.Seo;

public sealed class HttpKeywordResearchService(IHttpClientFactory factory) : IKeywordResearchService
{
    private readonly HttpClient _http = factory.CreateClient("GeekRepository");

    public async Task<Result<IReadOnlyList<KeywordResult>>> ResearchAsync(
        Guid userId, KeywordResearchRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"repo/seo/keywords/research?userId={userId}", request, ct);
        if (!response.IsSuccessStatusCode)
            return Result<IReadOnlyList<KeywordResult>>.Failure(await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<List<KeywordResult>>(ct);
        return Result<IReadOnlyList<KeywordResult>>.Success(value ?? []);
    }

    public async Task<Result<IReadOnlyList<KeywordCluster>>> ClusterAsync(
        Guid userId, ClusterKeywordsRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"repo/seo/keywords/cluster?userId={userId}", request, ct);
        if (!response.IsSuccessStatusCode)
            return Result<IReadOnlyList<KeywordCluster>>.Failure(await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<List<KeywordCluster>>(ct);
        return Result<IReadOnlyList<KeywordCluster>>.Success(value ?? []);
    }

    public async Task<Result<IReadOnlyList<SeoKeywordDto>>> GetProjectKeywordsAsync(
        Guid userId, Guid projectId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"repo/seo/keywords/project/{projectId}?userId={userId}", ct);
        if (!response.IsSuccessStatusCode)
            return Result<IReadOnlyList<SeoKeywordDto>>.Failure(await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<List<SeoKeywordDto>>(ct);
        return Result<IReadOnlyList<SeoKeywordDto>>.Success(value ?? []);
    }
}
