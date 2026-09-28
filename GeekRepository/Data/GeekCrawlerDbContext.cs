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
    public virtual DbSet<GeekCrawlerSchedule> GeekCrawlerSchedules => Set<GeekCrawlerSchedule>();

    // GeekCrawlerPages and GeekCrawlerLinks are gone. Crawl pages and links are Mongo
    // documents — MongoGeekCrawlerService writes crawl_pages and crawl_links, and no code
    // ever read either DbSet. A mapped DbSet with no reader is read as evidence that EF is
    // one of the ways this data is reached, and it is not.
    //
    // The entity CLASSES stay: GeekCrawlerPage and GeekCrawlerLink are Mongo's models too,
    // registered through BsonClassMap in MongoGeekCrawlerService. What is removed here is the
    // EF mapping, not the type.
    //
    // The existing migrations under Migrations/GeekCrawler still create and alter the
    // crawl_pages and crawl_links tables, and still apply: a migration runs its own Up(),
    // independent of the current model. Those are hand-written with no model snapshot, so
    // removing these entities cannot produce a generated DROP TABLE for a schema that may
    // still hold old rows.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("geek_crawler");

        modelBuilder.Entity<GeekCrawlerRun>(entity =>
        {
            entity.ToTable("crawl_runs");
            entity.HasKey(r => r.Id);
            // See the crawl_pages note below: Postgres is deprecated here, Mongo is the store.
            entity.Ignore(r => r.ContentReadyAt);
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

        modelBuilder.Entity<GeekCrawlerSchedule>(entity =>
        {
            entity.ToTable("crawl_schedules");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.OwnerUserId).IsRequired().HasMaxLength(128);
            entity.Property(s => s.CrawlType).IsRequired().HasMaxLength(32);
            entity.Property(s => s.SeedUrlsJson).IsRequired().HasColumnType("text");
            entity.Property(s => s.SeedKey).HasMaxLength(64);
            entity.Property(s => s.IntervalHours).IsRequired();
            entity.Property(s => s.Enabled).IsRequired();
            entity.Property(s => s.NextRunUtc).IsRequired();
            entity.Property(s => s.CreatedAtUtc).IsRequired();
            entity.HasIndex(s => new { s.Enabled, s.NextRunUtc })
                .HasDatabaseName("ix_crawl_schedules_due");
            entity.HasIndex(s => new { s.OwnerUserId, s.CrawlType, s.SeedKey })
                .IsUnique()
                .HasDatabaseName("ix_crawl_schedules_owner_slot")
                .HasFilter("\"SeedKey\" IS NOT NULL");
        });
    }
}
