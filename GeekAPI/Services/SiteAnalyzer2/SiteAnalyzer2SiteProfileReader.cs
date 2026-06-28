using System.Text.Json;
using Dapper;
using GeekAPI.Models;
using Npgsql;

namespace GeekAPI.Services.SiteAnalyzer2;

public sealed class SiteAnalyzer2SiteProfileReader(ILogger<SiteAnalyzer2SiteProfileReader> logger)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<SiteAnalyzer2SiteProfileExport?> GetByIdAsync(Guid siteProfileId, CancellationToken ct = default)
    {
        var connectionString = SiteAnalyzer2Connection.TryResolve();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("SITE_ANALYZER2_DATABASE_URL is not configured; cannot read site profile {SiteProfileId}", siteProfileId);
            return null;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT
                "Id",
                "SiteUrl",
                "DisplayName",
                "GeekSeoProjectId",
                "PrimaryNiche",
                "NicheDescription",
                "NicheTags",
                "BusinessSummary",
                "GeoAnchorNodes",
                "ServiceAreaDescription",
                "CompetitorDomains",
                "AuthorityPageUrls",
                "WritingRecommendations",
                "UpdatedAt"
            FROM sa2.site_profiles
            WHERE "Id" = @SiteProfileId
            """;

        var row = await conn.QuerySingleOrDefaultAsync<SiteProfileRow>(
            new CommandDefinition(sql, new { SiteProfileId = siteProfileId }, cancellationToken: ct));

        return row is null ? null : Map(row);
    }

    private static SiteAnalyzer2SiteProfileExport Map(SiteProfileRow row) =>
        new()
        {
            Id = row.Id,
            SiteUrl = row.SiteUrl,
            DisplayName = row.DisplayName,
            GeekSeoProjectId = row.GeekSeoProjectId,
            PrimaryNiche = row.PrimaryNiche,
            NicheDescription = row.NicheDescription,
            NicheTags = ParseStringList(row.NicheTags),
            BusinessSummary = row.BusinessSummary,
            GeoAnchorNodes = ParseStringList(row.GeoAnchorNodes),
            ServiceAreaDescription = row.ServiceAreaDescription,
            CompetitorDomains = ParseStringList(row.CompetitorDomains),
            AuthorityPageUrls = ParseStringList(row.AuthorityPageUrls),
            WritingRecommendations = ParseStringList(row.WritingRecommendations),
            UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc)),
        };

    private static IReadOnlyList<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed class SiteProfileRow
    {
        public Guid Id { get; init; }
        public string SiteUrl { get; init; } = string.Empty;
        public string? DisplayName { get; init; }
        public Guid? GeekSeoProjectId { get; init; }
        public string? PrimaryNiche { get; init; }
        public string? NicheDescription { get; init; }
        public string? NicheTags { get; init; }
        public string? BusinessSummary { get; init; }
        public string? GeoAnchorNodes { get; init; }
        public string? ServiceAreaDescription { get; init; }
        public string? CompetitorDomains { get; init; }
        public string? AuthorityPageUrls { get; init; }
        public string? WritingRecommendations { get; init; }
        public DateTime UpdatedAt { get; init; }
    }
}
