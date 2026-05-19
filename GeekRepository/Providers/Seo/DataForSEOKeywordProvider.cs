using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekRepository.Providers.Seo;

public sealed class DataForSEOKeywordProvider(IHttpClientFactory httpClientFactory) : IKeywordProvider
{
    public string ProviderName => "dataforseo";

    public async Task<Result<IReadOnlyList<KeywordResult>>> GetKeywordSuggestionsAsync(
        string seedKeyword, string location, int count, CancellationToken ct = default)
    {
        var login = Environment.GetEnvironmentVariable("DATAFORSEO_LOGIN");
        var password = Environment.GetEnvironmentVariable("DATAFORSEO_PASSWORD");
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            return Result<IReadOnlyList<KeywordResult>>.Failure(
                "DATAFORSEO_LOGIN and DATAFORSEO_PASSWORD must be set for keyword research.");
        }

        var client = httpClientFactory.CreateClient("DataForSEO");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var body = new[]
        {
            new
            {
                keywords = new[] { seedKeyword },
                location_name = location,
                language_code = "en",
                sort_by = "search_volume",
            },
        };

        using var response = await client.PostAsJsonAsync(
            "/v3/keywords_data/google_ads/keywords_for_keywords/live",
            body,
            ct);

        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return Result<IReadOnlyList<KeywordResult>>.Failure($"DataForSEO HTTP {(int)response.StatusCode}: {raw}");

        return ParseSuggestions(raw, count);
    }

    private static Result<IReadOnlyList<KeywordResult>> ParseSuggestions(string raw, int count)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("status_code", out var statusCode) && statusCode.GetInt32() != 20000)
            {
                var msg = root.TryGetProperty("status_message", out var sm) ? sm.GetString() : "DataForSEO error";
                return Result<IReadOnlyList<KeywordResult>>.Failure(msg ?? "DataForSEO error");
            }

            var results = root.GetProperty("tasks")[0].GetProperty("result");
            var list = new List<KeywordResult>();
            foreach (var item in results.EnumerateArray())
            {
                if (list.Count >= count)
                    break;

                var keyword = item.GetProperty("keyword").GetString();
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                var monthly = new List<MonthlySearchVolume>();
                if (item.TryGetProperty("monthly_searches", out var monthlyArr))
                {
                    foreach (var m in monthlyArr.EnumerateArray())
                    {
                        monthly.Add(new MonthlySearchVolume
                        {
                            Year = m.GetProperty("year").GetInt32(),
                            Month = m.GetProperty("month").GetInt32(),
                            Volume = m.GetProperty("search_volume").GetInt32(),
                        });
                    }
                }

                list.Add(new KeywordResult
                {
                    Keyword = keyword,
                    SearchVolume = item.TryGetProperty("search_volume", out var sv) && sv.ValueKind != JsonValueKind.Null
                        ? sv.GetInt32()
                        : 0,
                    KeywordDifficulty = item.TryGetProperty("competition_index", out var ci) && ci.ValueKind != JsonValueKind.Null
                        ? ci.GetInt32()
                        : 0,
                    CpcUsd = item.TryGetProperty("cpc", out var cpc) && cpc.ValueKind == JsonValueKind.Number
                        ? cpc.GetDouble()
                        : 0,
                    Competition = item.TryGetProperty("competition", out var comp) && comp.ValueKind == JsonValueKind.String
                        ? comp.GetString() ?? "UNKNOWN"
                        : "UNKNOWN",
                    MonthlyTrend = monthly,
                });
            }

            return Result<IReadOnlyList<KeywordResult>>.Success(list);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<KeywordResult>>.Failure($"Failed to parse DataForSEO keywords: {ex.Message}");
        }
    }
}
