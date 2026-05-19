using System.Net.Http.Json;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Results;

namespace GeekAPI.HttpClients.Seo;

public sealed class HttpUsageMeteringRepository(IHttpClientFactory factory) : IUsageMeteringRepository
{
    private readonly HttpClient _http = factory.CreateClient("GeekRepository");

    public async Task<Result<int>> GetCountAsync(
        Guid userId, DateOnly periodStart, string feature, CancellationToken ct = default)
    {
        var url =
            $"repo/seo/usage?userId={userId}&feature={Uri.EscapeDataString(feature)}&periodStart={periodStart:yyyy-MM-dd}";
        var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return Result<int>.Failure(await response.Content.ReadAsStringAsync(ct));
        var body = await response.Content.ReadFromJsonAsync<CountResponse>(ct);
        return Result<int>.Success(body?.Count ?? 0);
    }

    public async Task<Result<int>> IncrementAsync(
        Guid userId, DateOnly periodStart, string feature, int amount = 1, CancellationToken ct = default)
    {
        var url =
            $"repo/seo/usage/increment?userId={userId}&feature={Uri.EscapeDataString(feature)}&periodStart={periodStart:yyyy-MM-dd}&amount={amount}";
        var response = await _http.PostAsync(url, null, ct);
        if (!response.IsSuccessStatusCode)
            return Result<int>.Failure(await response.Content.ReadAsStringAsync(ct));
        var body = await response.Content.ReadFromJsonAsync<CountResponse>(ct);
        return Result<int>.Success(body?.Count ?? 0);
    }

    private sealed record CountResponse(int Count);
}
