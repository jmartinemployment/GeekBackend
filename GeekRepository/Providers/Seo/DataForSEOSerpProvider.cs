using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekRepository.Providers.Seo;

public sealed class DataForSEOSerpProvider(IHttpClientFactory httpClientFactory) : ISerpProvider
{
    public string ProviderName => "dataforseo";

    public async Task<Result<SerpResult>> GetSerpResultsAsync(SerpRequest request, CancellationToken ct = default)
    {
        var login = Environment.GetEnvironmentVariable("DATAFORSEO_LOGIN");
        var password = Environment.GetEnvironmentVariable("DATAFORSEO_PASSWORD");
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            return Result<SerpResult>.Failure(
                "DATAFORSEO_LOGIN and DATAFORSEO_PASSWORD must be set. Sign up at https://dataforseo.com/");
        }

        var client = httpClientFactory.CreateClient("DataForSEO");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var body = new[]
        {
            new
            {
                keyword = request.Keyword,
                location_name = request.Location,
                language_code = request.LanguageCode,
                device = request.Device,
                depth = Math.Clamp(request.ResultCount, 1, 50),
                os = "windows",
            },
        };

        using var response = await client.PostAsJsonAsync(
            "/v3/serp/google/organic/live/advanced",
            body,
            ct);

        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return Result<SerpResult>.Failure($"DataForSEO HTTP {(int)response.StatusCode}: {raw}");

        return ParseResponse(request, raw);
    }

    private static Result<SerpResult> ParseResponse(SerpRequest request, string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("status_code", out var statusCode) && statusCode.GetInt32() != 20000)
            {
                var msg = root.TryGetProperty("status_message", out var sm) ? sm.GetString() : "DataForSEO error";
                return Result<SerpResult>.Failure(msg ?? "DataForSEO error");
            }

            var items = root.GetProperty("tasks")[0].GetProperty("result")[0].GetProperty("items");
            var organic = new List<SerpOrganicResult>();
            var paa = new List<PeopleAlsoAskResult>();
            var related = new List<string>();
            string? featuredSnippet = null;
            var features = new SerpFeatures();

            foreach (var item in items.EnumerateArray())
            {
                var type = item.GetProperty("type").GetString() ?? string.Empty;
                switch (type)
                {
                    case "organic":
                        organic.Add(new SerpOrganicResult
                        {
                            Position = item.TryGetProperty("rank_group", out var rg) ? rg.GetInt32() : organic.Count + 1,
                            Url = item.GetProperty("url").GetString() ?? string.Empty,
                            Title = item.GetProperty("title").GetString() ?? string.Empty,
                            Snippet = item.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty,
                            Domain = item.TryGetProperty("domain", out var dom) ? dom.GetString() : null,
                        });
                        break;
                    case "people_also_ask":
                        features = features with { HasPeopleAlsoAsk = true };
                        if (item.TryGetProperty("items", out var paaItems))
                        {
                            foreach (var paaItem in paaItems.EnumerateArray())
                            {
                                paa.Add(new PeopleAlsoAskResult
                                {
                                    Question = paaItem.GetProperty("title").GetString() ?? string.Empty,
                                    Answer = paaItem.TryGetProperty("description", out var ans) ? ans.GetString() : null,
                                });
                            }
                        }
                        break;
                    case "featured_snippet":
                        features = features with { HasFeaturedSnippet = true };
                        featuredSnippet = item.TryGetProperty("description", out var fs) ? fs.GetString() : null;
                        break;
                    case "local_pack":
                        features = features with { HasLocalPack = true };
                        break;
                    case "images":
                        features = features with { HasImagePack = true };
                        break;
                    case "video":
                        features = features with { HasVideoCarousel = true };
                        break;
                    case "knowledge_graph":
                        features = features with { HasKnowledgePanel = true };
                        break;
                    case "related_searches":
                        if (item.TryGetProperty("items", out var relItems))
                        {
                            foreach (var rel in relItems.EnumerateArray())
                            {
                                var title = rel.GetProperty("title").GetString();
                                if (!string.IsNullOrWhiteSpace(title))
                                    related.Add(title);
                            }
                        }
                        break;
                }
            }

            return Result<SerpResult>.Success(new SerpResult
            {
                Keyword = request.Keyword,
                Location = request.Location,
                OrganicResults = organic,
                PeopleAlsoAsk = paa,
                RelatedSearches = related,
                FeaturedSnippetText = featuredSnippet,
                Features = features,
                FetchedAt = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            return Result<SerpResult>.Failure($"Failed to parse DataForSEO response: {ex.Message}");
        }
    }
}
