using System.Net.Http.Json;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekAPI.HttpClients.Seo;

public sealed class HttpContentBriefService(IHttpClientFactory factory) : IContentBriefService
{
    private readonly HttpClient _http = factory.CreateClient("GeekRepository");

    public async Task<Result<ContentBrief>> GenerateBriefAsync(
        Guid userId, GenerateBriefRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"repo/seo/briefs/generate?userId={userId}", request, ct);
        if (!response.IsSuccessStatusCode)
            return Result<ContentBrief>.Failure(await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<ContentBrief>(ct);
        return value is null ? Result<ContentBrief>.Failure("Empty response") : Result<ContentBrief>.Success(value);
    }
}
