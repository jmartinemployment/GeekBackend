using Dapper;
using GeekSeo.Application.Models.Seo;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GeekSa2Read;

public sealed class Sa2ContentWriterBundleReader(ILogger<Sa2ContentWriterBundleReader> logger)
{
    public async Task<ContentWriterSiteBundle?> GetByGeekSeoProjectIdAsync(Guid geekSeoProjectId, CancellationToken ct = default)
    {
        var connectionString = SiteAnalyzer2Connection.TryResolve();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("SITE_ANALYZER2_DATABASE_URL is not configured; cannot read site bundle for project {GeekSeoProjectId}", geekSeoProjectId);
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
                "BusinessType",
                "BusinessDescription",
                "GeneratedSchemaJson",
                "CreatedAt",
                "UpdatedAt",
                "BusinessProfileAt",
                "LastRunAt"
            FROM sa2.site_profiles
            WHERE "GeekSeoProjectId" = @GeekSeoProjectId
            LIMIT 1
            """;

        var row = await conn.QuerySingleOrDefaultAsync<SiteProfileRow>(
            new CommandDefinition(sql, new { GeekSeoProjectId = geekSeoProjectId }, cancellationToken: ct));

        return row is null ? null : Map(row);
    }

    public async Task<ContentWriterSiteBundle?> GetByProfileIdAsync(Guid siteProfileId, CancellationToken ct = default)
    {
        var connectionString = SiteAnalyzer2Connection.TryResolve();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("SITE_ANALYZER2_DATABASE_URL is not configured; cannot read site bundle {SiteProfileId}", siteProfileId);
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
                "BusinessType",
                "BusinessDescription",
                "GeneratedSchemaJson",
                "CreatedAt",
                "UpdatedAt",
                "BusinessProfileAt",
                "LastRunAt"
            FROM sa2.site_profiles
            WHERE "Id" = @SiteProfileId
            """;

        var row = await conn.QuerySingleOrDefaultAsync<SiteProfileRow>(
            new CommandDefinition(sql, new { SiteProfileId = siteProfileId }, cancellationToken: ct));

        return row is null ? null : Map(row);
    }

    private static ContentWriterSiteBundle Map(SiteProfileRow row)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        return new ContentWriterSiteBundle
        {
            BundleVersion = ContentWriterSiteBundle.CurrentBundleVersion,
            CapturedAt = capturedAt,
            SiteProfileId = row.Id,
            GeekSeoProjectId = row.GeekSeoProjectId,
            SiteUrl = row.SiteUrl,
            DisplayName = row.DisplayName,
            CreatedAt = ToOffset(row.CreatedAt),
            UpdatedAt = ToOffset(row.UpdatedAt),
            BusinessProfileAt = ToOffset(row.BusinessProfileAt),
            LastRunAt = ToOffset(row.LastRunAt),
            BusinessType = row.BusinessType,
            BusinessDescription = row.BusinessDescription,
            BusinessSummary = row.BusinessSummary,
            GeneratedSchemaJson = string.IsNullOrWhiteSpace(row.GeneratedSchemaJson)
                ? null
                : row.GeneratedSchemaJson.Trim(),
            PrimaryNiche = row.PrimaryNiche,
            NicheDescription = row.NicheDescription,
            NicheTags = Sa2Json.ParseStringList(row.NicheTags),
            GeoAnchorNodes = Sa2Json.ParseStringList(row.GeoAnchorNodes),
            ServiceAreaDescription = row.ServiceAreaDescription,
            CompetitorDomains = Sa2Json.ParseStringList(row.CompetitorDomains),
            AuthorityPageUrls = Sa2Json.ParseStringList(row.AuthorityPageUrls),
            WritingRecommendations = Sa2Json.ParseStringList(row.WritingRecommendations),
            RecommendedHomepageJsonLd = [],
        };
    }

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is null ? null : ToOffset(value.Value);

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
        public string? BusinessType { get; init; }
        public string? BusinessDescription { get; init; }
        public string? GeneratedSchemaJson { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public DateTime? BusinessProfileAt { get; init; }
        public DateTime? LastRunAt { get; init; }
    }
}
