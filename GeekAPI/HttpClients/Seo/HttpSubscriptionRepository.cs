using System.Net;
using System.Net.Http.Json;
using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Results;

namespace GeekAPI.HttpClients.Seo;

public sealed class HttpSubscriptionRepository(IHttpClientFactory factory) : ISubscriptionRepository
{
    private readonly HttpClient _http = factory.CreateClient("GeekRepository");

    public async Task<Result<SeoSubscription?>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"repo/seo/subscriptions?userId={userId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Result<SeoSubscription?>.Success(null);
        if (!response.IsSuccessStatusCode)
            return Result<SeoSubscription?>.Failure(await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<SeoSubscription>(ct);
        return Result<SeoSubscription?>.Success(value);
    }
}
