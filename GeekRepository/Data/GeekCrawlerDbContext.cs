using GeekRepository.Data.Entities.GeekCrawler;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

/// <summary>Isolated schema <c>geek_crawler</c> for Geek-Crawler product.</summary>
public class GeekCrawlerDbContext : DbContext
{
    public GeekCrawlerDbContext(DbContextOptions<GeekCrawlerDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<GeekCrawlerRun> GeekCrawlerRuns => Set<GeekCrawlerRun>();
    public virtual DbSet<GeekCrawlerPage> GeekCrawlerPages => Set<GeekCrawlerPage>();
    public virtual DbSet<GeekCrawlerLink> GeekCrawlerLinks => Set<GeekCrawlerLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("geek_crawler");

        modelBuilder.Entity<GeekCrawlerRun>(entity =>
        {
            entity.ToTable("crawl_runs");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.OwnerUserId).IsRequired().HasMaxLength(128);
            entity.Property(r => r.CrawlType).IsRequired().HasMaxLength(32);
            entity.Property(r => r.Status).IsRequired().HasMaxLength(32);
            entity.Property(r => r.SeedUrlsJson).IsRequired().HasColumnType("text");
            entity.Property(r => r.SeedKey).HasMaxLength(64);
            entity.Property(r => r.HostProgressJson).HasColumnType("text");
            entity.Property(r => r.ErrorSummary).HasMaxLength(2048);
            entity.Property(r => r.CreatedAtUtc).IsRequired();
            entity.HasIndex(r => new { r.OwnerUserId, r.CrawlType, r.CreatedAtUtc })
                .HasDatabaseName("ix_crawl_runs_owner_type_created");
            entity.HasIndex(r => new { r.CrawlType, r.Status })
                .HasDatabaseName("ix_crawl_runs_type_status");
        });

        modelBuilder.Entity<GeekCrawlerPage>(entity =>
        {
            entity.ToTable("crawl_pages");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Origin).IsRequired().HasMaxLength(512);
            entity.Property(p => p.Url).IsRequired().HasMaxLength(2048);
            entity.Property(p => p.FinalUrl).IsRequired().HasMaxLength(2048);
            entity.Property(p => p.Html).HasColumnType("text");
            entity.Property(p => p.CrawledAtUtc).IsRequired();
            entity.HasIndex(p => p.RunId).HasDatabaseName("ix_crawl_pages_run_id");
            entity.HasIndex(p => new { p.RunId, p.Url }).HasDatabaseName("ix_crawl_pages_run_url");
        });

        modelBuilder.Entity<GeekCrawlerLink>(entity =>
        {
            entity.ToTable("crawl_links");
            entity.HasKey(l => l.Id);
            entity.Property(l => l.FromUrl).IsRequired().HasMaxLength(2048);
            entity.Property(l => l.LinkUrl).IsRequired().HasMaxLength(2048);
            entity.Property(l => l.DiscoveredAtUtc).IsRequired();
            entity.HasIndex(l => l.RunId).HasDatabaseName("ix_crawl_links_run_id");
            entity.HasIndex(l => new { l.RunId, l.FromUrl }).HasDatabaseName("ix_crawl_links_run_from");
            entity.HasIndex(l => new { l.RunId, l.IsSameOrigin }).HasDatabaseName("ix_crawl_links_run_same_origin");
            entity.HasIndex(l => new { l.RunId, l.IsSameOrigin, l.DiscoveredAtUtc, l.Id })
                .HasDatabaseName("ix_crawl_links_run_same_origin_discovered_id");
            entity.HasIndex(l => new { l.RunId, l.FromUrl, l.LinkUrl })
                .IsUnique()
                .HasDatabaseName("ux_crawl_links_run_from_link");
        });
    }
}
