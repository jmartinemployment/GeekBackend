using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.Gsc;

/// <summary>
/// Content Creator–owned Google Search Console fetch.
/// Patterns copied from Geek-SEO GoogleDataService; does not call Geek SEO at runtime.
/// </summary>
public sealed class GccV2GscSearchAnalyticsClient(IHttpClientFactory httpClientFactory)
{
    public async Task<GccV2GscTokenResponse> ExchangeAuthorizationCodeAsync(
        string code,
        string clientId,
        string clientSecret,
        string redirectUri,
        CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        };
        return await TokenRequestAsync(form, ct);
    }

    public async Task<string> ExchangeRefreshTokenAsync(
        string refreshToken,
        string clientId,
        string clientSecret,
        CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        };
        var token = await TokenRequestAsync(form, ct);
        return token.AccessToken;
    }

    public async Task<IReadOnlyList<string>> ListAccessibleSiteUrlsAsync(
        string accessToken,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("GccV2GoogleApis");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/webmasters/v3/sites");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return [];

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("siteEntry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return [];

        var sites = new List<string>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("siteUrl", out var siteUrl))
                continue;
            var value = siteUrl.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                sites.Add(value);
        }

        return sites;
    }

    private async Task<GccV2GscTokenResponse> TokenRequestAsync(
        IReadOnlyDictionary<string, string> formFields,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("GccV2GoogleApis");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(formFields),
        };
        using var response = await client.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google token endpoint failed ({(int)response.StatusCode}): {raw}");

        using var doc = JsonDocument.Parse(raw);
        var accessToken = doc.RootElement.TryGetProperty("access_token", out var tokenEl)
            ? tokenEl.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Google token response missing access_token.");
        var refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var refreshEl)
            ? refreshEl.GetString()
            : null;
        return new GccV2GscTokenResponse(accessToken, refreshToken);
    }

    public async Task<IReadOnlyList<GccV2GscQueryRow>> QueryAsync(
        string accessToken,
        string siteUrl,
        DateOnly startDate,
        DateOnly endDate,
        int rowLimit,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("GccV2GoogleApis");
        var encodedSite = Uri.EscapeDataString(siteUrl);
        var endpoint = $"https://www.googleapis.com/webmasters/v3/sites/{encodedSite}/searchAnalytics/query";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                startDate = startDate.ToString("yyyy-MM-dd"),
                endDate = endDate.ToString("yyyy-MM-dd"),
                dimensions = new[] { "query" },
                rowLimit,
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GSC query failed ({(int)response.StatusCode}) for {siteUrl}: {raw}");

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<GccV2GscQueryRow>();
        foreach (var row in rows.EnumerateArray())
        {
            var keys = row.TryGetProperty("keys", out var rowKeys) && rowKeys.ValueKind == JsonValueKind.Array
                ? rowKeys.EnumerateArray().Select(k => k.GetString() ?? string.Empty).ToArray()
                : [];
            var query = keys.ElementAtOrDefault(0) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query)) continue;
            results.Add(new GccV2GscQueryRow(
                query,
                row.TryGetProperty("impressions", out var imp) ? (long)imp.GetDouble() : 0,
                row.TryGetProperty("clicks", out var clk) ? (long)clk.GetDouble() : 0));
        }

        return results;
    }
}

public sealed record GccV2GscQueryRow(string Query, long Impressions, long Clicks);

public sealed record GccV2GscTokenResponse(string AccessToken, string? RefreshToken);
