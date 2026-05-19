using GeekApplication.Entities.Seo;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

public partial class SeoDbContext : DbContext
{
    public SeoDbContext(DbContextOptions<SeoDbContext> options)
        : base(options)
    {
    }

    public DbSet<SeoProject> Projects => Set<SeoProject>();
    public DbSet<SeoContentDocument> ContentDocuments => Set<SeoContentDocument>();
    public DbSet<SeoKeywordCluster> KeywordClusters => Set<SeoKeywordCluster>();
    public DbSet<SeoKeyword> Keywords => Set<SeoKeyword>();
    public DbSet<SeoSerpResult> SerpResults => Set<SeoSerpResult>();
    public DbSet<SeoCompetitorPage> CompetitorPages => Set<SeoCompetitorPage>();
    public DbSet<SeoPageAudit> PageAudits => Set<SeoPageAudit>();
    public DbSet<SeoSiteAudit> SiteAudits => Set<SeoSiteAudit>();
    public DbSet<SeoSiteAuditPage> SiteAuditPages => Set<SeoSiteAuditPage>();
    public DbSet<SeoRankTracking> RankTracking => Set<SeoRankTracking>();
    public DbSet<SeoGscConnection> GscConnections => Set<SeoGscConnection>();
    public DbSet<SeoSubscription> Subscriptions => Set<SeoSubscription>();
    public DbSet<SeoReport> Reports => Set<SeoReport>();
    public DbSet<SeoAlert> Alerts => Set<SeoAlert>();
    public DbSet<SeoUsageCounter> UsageCounters => Set<SeoUsageCounter>();
    public DbSet<SeoBackgroundJob> BackgroundJobs => Set<SeoBackgroundJob>();
    public DbSet<SeoWordPressConnection> WordPressConnections => Set<SeoWordPressConnection>();
    public DbSet<SeoPublishedPage> PublishedPages => Set<SeoPublishedPage>();
    public DbSet<SeoContentPerformanceSnapshot> ContentPerformanceSnapshots => Set<SeoContentPerformanceSnapshot>();
    public DbSet<SeoTopicalMap> TopicalMaps => Set<SeoTopicalMap>();
    public DbSet<SeoSitePageInventory> SitePageInventory => Set<SeoSitePageInventory>();
    public DbSet<SeoBrandVoice> BrandVoices => Set<SeoBrandVoice>();
    public DbSet<SeoBulkJob> BulkJobs => Set<SeoBulkJob>();
    public DbSet<SeoPlagiarismCheck> PlagiarismChecks => Set<SeoPlagiarismCheck>();
    public DbSet<SeoGa4Connection> Ga4Connections => Set<SeoGa4Connection>();
    public DbSet<SeoGeoTrackingQuery> GeoTrackingQueries => Set<SeoGeoTrackingQuery>();
    public DbSet<SeoGeoMentionSnapshot> GeoMentionSnapshots => Set<SeoGeoMentionSnapshot>();
    public DbSet<SeoCannibalizationIssue> CannibalizationIssues => Set<SeoCannibalizationIssue>();
    public DbSet<SeoApiKey> ApiKeys => Set<SeoApiKey>();
    public DbSet<SeoSerpDeepCache> SerpDeepCache => Set<SeoSerpDeepCache>();
    public DbSet<SeoOrganization> Organizations => Set<SeoOrganization>();
    public DbSet<SeoOrganizationMember> OrganizationMembers => Set<SeoOrganizationMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("geek_seo");
        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
