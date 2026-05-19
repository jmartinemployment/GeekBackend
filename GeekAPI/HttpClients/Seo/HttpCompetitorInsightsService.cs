using GeekApplication.Interfaces.Seo;
using GeekApplication.Results;

namespace GeekAPI.HttpClients.Seo;

public sealed class HttpCompetitorInsightsService(IHttpClientFactory factory) : ICompetitorInsightsService
{
    private readonly HttpClient _http = factory.CreateClient("GeekRepository");

    public async Task<Result<CompetitorInsightsResult>> GetForDocumentAsync(
        Guid userId, Guid documentId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(
            $"repo/seo/content/{documentId}/competitors?userId={userId}", ct);
        if (!response.IsSuccessStatusCode)
            return Result<CompetitorInsightsResult>.Failure(await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<CompetitorInsightsResult>(ct);
        return value is null
            ? Result<CompetitorInsightsResult>.Failure("Empty response")
            : Result<CompetitorInsightsResult>.Success(value);
    }

    public async Task<Result<CompetitorInsightsResult>> RefreshCrawlForDocumentAsync(
        Guid userId, Guid documentId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync(
            $"repo/seo/content/{documentId}/competitors/crawl?userId={userId}", null, ct);
        if (!response.IsSuccessStatusCode)
            return Result<CompetitorInsightsResult>.Failure(await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<CompetitorInsightsResult>(ct);
        return value is null
            ? Result<CompetitorInsightsResult>.Failure("Empty response")
            : Result<CompetitorInsightsResult>.Success(value);
    }
}
