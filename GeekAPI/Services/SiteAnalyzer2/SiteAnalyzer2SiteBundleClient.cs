using System.Net.Http.Json;
using System.Text.Json;
using GeekAPI.Models;

namespace GeekAPI.Services.SiteAnalyzer2;

public sealed class SiteAnalyzer2SiteBundleClient(ILogger<SiteAnalyzer2SiteBundleClient> logger)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<ContentWriterSiteBundleExport?> GetByProfileIdAsync(Guid siteProfileId, CancellationToken ct = default)
    {
        var baseUrl = Environment.GetEnvironmentVariable("SITE_ANALYZER2_API_URL")?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogWarning("SITE_ANALYZER2_API_URL is not configured; cannot read site bundle {SiteProfileId}", siteProfileId);
            return null;
        }

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl + "/"), Timeout = TimeSpan.FromSeconds(60) };
        var response = await http.GetAsync($"site-profiles/{siteProfileId}/content-writer-bundle", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Site Analyzer site bundle request failed for {SiteProfileId}: {Status}",
                siteProfileId,
                (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ContentWriterSiteBundleExport>(Json, ct);
    }
}

public sealed record ContentWriterSiteBundleExport
{
    public int BundleVersion { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public Guid SiteProfileId { get; init; }
    public Guid? GeekSeoProjectId { get; init; }
    public string SiteUrl { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? BusinessProfileAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public string? BusinessType { get; init; }
    public string? BusinessDescription { get; init; }
    public string? BusinessSummary { get; init; }
    public string? GeneratedSchemaJson { get; init; }
    public string? PrimaryNiche { get; init; }
    public string? NicheDescription { get; init; }
    public IReadOnlyList<string> NicheTags { get; init; } = [];
    public IReadOnlyList<string> GeoAnchorNodes { get; init; } = [];
    public string? ServiceAreaDescription { get; init; }
    public IReadOnlyList<string> CompetitorDomains { get; init; } = [];
    public IReadOnlyList<string> AuthorityPageUrls { get; init; } = [];
    public IReadOnlyList<string> WritingRecommendations { get; init; } = [];
    public IReadOnlyList<RecommendedJsonLdSnippetExport> RecommendedHomepageJsonLd { get; init; } = [];
}

public sealed record RecommendedJsonLdSnippetExport(
    string Id,
    string Title,
    string Description,
    string Json,
    string ScriptTag);
