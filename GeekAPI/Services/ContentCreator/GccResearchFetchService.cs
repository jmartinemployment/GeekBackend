using System.Net;
using System.Text.Json;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Follow organic URLs and build ResearchJson. Fail closed: any URL failure or empty extract
/// fails the entire operation (no partial success).
/// </summary>
public class GccResearchFetchService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GccResearchFetchService> _logger;

    public GccResearchFetchService(
        IHttpClientFactory httpClientFactory,
        ILogger<GccResearchFetchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<GccResearchDocument> FetchQuoteablesAsync(
        IReadOnlyList<string> urls,
        GccSerpIndex? serpIndex,
        CancellationToken ct)
    {
        if (urls.Count == 0)
            throw new InvalidOperationException("At least one organic URL is required.");
        if (urls.Count > GccResearchCaps.MaxQuoteables)
            throw new InvalidOperationException(
                $"At most {GccResearchCaps.MaxQuoteables} organic URLs may be followed.");

        var quoteables = new List<GccQuoteablePage>();
        var client = _httpClientFactory.CreateClient("GccResearchFetch");

        foreach (var raw in urls)
        {
            var url = (raw ?? "").Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException($"Invalid URL (must be absolute http/https): {url}");
            }

            string html;
            try
            {
                using var response = await client.GetAsync(uri, ct);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Fetch failed for {url}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                    && !mediaType.Contains("text/plain", StringComparison.OrdinalIgnoreCase)
                    && mediaType.Length > 0)
                {
                    throw new InvalidOperationException(
                        $"Fetch failed for {url}: non-HTML content-type '{mediaType}'");
                }

                html = await response.Content.ReadAsStringAsync(ct);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                throw new InvalidOperationException($"Fetch failed for {url}: timeout");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Fetch failed for {Url}", url);
                throw new InvalidOperationException($"Fetch failed for {url}: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(html))
                throw new InvalidOperationException($"Fetch failed for {url}: empty body");

            var page = GccArticleHtmlExtractor.Extract(url, html);
            if (GccArticleHtmlExtractor.IsEmpty(page))
            {
                throw new InvalidOperationException(
                    $"Extract failed for {url}: no headings and no paragraphs (empty extract)");
            }

            quoteables.Add(page);
        }

        return new GccResearchDocument(serpIndex, quoteables);
    }

    public static string Serialize(GccResearchDocument doc) =>
        JsonSerializer.Serialize(doc, JsonOpts);

    public static GccResearchDocument? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<GccResearchDocument>(json, JsonOpts);
    }
}
