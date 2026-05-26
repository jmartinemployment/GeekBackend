using GeekApplication.Entities.Seo;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

public partial class SeoDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SeoProject>(e =>
        {
            e.ToTable("seo_projects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<SeoContentDocument>(e =>
        {
            e.ToTable("seo_content_documents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasOne(x => x.Project).WithMany(p => p.ContentDocuments).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<SeoKeywordCluster>(e =>
        {
            e.ToTable("seo_keyword_clusters");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        });

        modelBuilder.Entity<SeoKeyword>(e =>
        {
            e.ToTable("seo_keywords");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        });

        modelBuilder.Entity<SeoSerpResult>(e =>
        {
            e.ToTable("seo_serp_results");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => new { x.Keyword, x.Location, x.LanguageCode }).IsUnique();
        });

        modelBuilder.Entity<SeoCompetitorPage>(e =>
        {
            e.ToTable("seo_competitor_pages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasOne(x => x.SerpResult).WithMany(s => s.CompetitorPages).HasForeignKey(x => x.SerpResultId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SerpResultId, x.Url }).IsUnique();
        });

        modelBuilder.Entity<SeoPageAudit>(e => { e.ToTable("seo_page_audits"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<SeoSiteAudit>(e => { e.ToTable("seo_site_audits"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<SeoSiteAuditPage>(e =>
        {
            e.ToTable("seo_site_audit_pages");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.SiteAudit).WithMany(a => a.Pages).HasForeignKey(x => x.SiteAuditId);
        });

        modelBuilder.Entity<SeoRankTracking>(e => { e.ToTable("seo_rank_tracking"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<SeoGscConnection>(e => { e.ToTable("seo_gsc_connections"); e.HasKey(x => x.Id); e.HasIndex(x => x.ProjectId).IsUnique(); });
        modelBuilder.Entity<SeoSubscription>(e => { e.ToTable("seo_subscriptions"); e.HasKey(x => x.Id); e.HasIndex(x => x.UserId).IsUnique(); });
        modelBuilder.Entity<SeoReport>(e => { e.ToTable("seo_reports"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<SeoAlert>(e => { e.ToTable("seo_alerts"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<SeoUsageCounter>(e =>
        {
            e.ToTable("seo_usage_counters");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.PeriodStart, x.Feature }).IsUnique();
        });

        modelBuilder.Entity<SeoBackgroundJob>(e =>
        {
            e.ToTable("seo_background_jobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => new { x.UserId, x.Status });
        });

        modelBuilder.Entity<SeoWordPressConnection>(e => { e.ToTable("seo_wordpress_connections"); e.HasKey(x => x.Id); e.HasIndex(x => x.ProjectId).IsUnique(); });
        modelBuilder.Entity<SeoPublishedPage>(e => { e.ToTable("seo_published_pages"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ProjectId, x.Url }).IsUnique(); });
        modelBuilder.Entity<SeoContentPerformanceSnapshot>(e =>
        {
            e.ToTable("seo_content_performance_snapshots");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.PublishedPage).WithMany().HasForeignKey(x => x.PublishedPageId);
            e.HasIndex(x => new { x.PublishedPageId, x.Date }).IsUnique();
        });

        modelBuilder.Entity<SeoTopicalMap>(e => { e.ToTable("seo_topical_maps"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<SeoSitePageInventory>(e => { e.ToTable("seo_site_page_inventory"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ProjectId, x.Url }).IsUnique(); });
        modelBuilder.Entity<SeoBrandVoice>(e => { e.ToTable("seo_brand_voices"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<SeoBulkJob>(e => { e.ToTable("seo_bulk_jobs"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<SeoPlagiarismCheck>(e => { e.ToTable("seo_plagiarism_checks"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<SeoGa4Connection>(e => { e.ToTable("seo_ga4_connections"); e.HasKey(x => x.Id); e.HasIndex(x => x.ProjectId).IsUnique(); });
        modelBuilder.Entity<SeoGeoTrackingQuery>(e => { e.ToTable("seo_geo_tracking_queries"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<SeoGeoMentionSnapshot>(e =>
        {
            e.ToTable("seo_geo_mention_snapshots");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Query).WithMany(q => q.Snapshots).HasForeignKey(x => x.QueryId);
        });

        modelBuilder.Entity<SeoCannibalizationIssue>(e => { e.ToTable("seo_cannibalization_issues"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<SeoApiKey>(e => { e.ToTable("seo_api_keys"); e.HasKey(x => x.Id); });
        modelBuilder.Entity<SeoSerpDeepCache>(e =>
        {
            e.ToTable("seo_serp_deep_cache");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Keyword, x.Location, x.ResultCount }).IsUnique();
        });

        modelBuilder.Entity<SeoOrganization>(e => { e.ToTable("seo_organizations"); e.HasKey(x => x.Id); e.HasIndex(x => x.Slug).IsUnique(); });
        modelBuilder.Entity<SeoOrganizationMember>(e =>
        {
            e.ToTable("seo_organization_members");
            e.HasKey(x => new { x.OrgId, x.UserId });
            e.HasOne(x => x.Organization).WithMany(o => o.Members).HasForeignKey(x => x.OrgId);
        });

        modelBuilder.Entity<SeoProject>()
            .HasOne<SeoOrganization>()
            .WithMany()
            .HasForeignKey(x => x.OrgId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
