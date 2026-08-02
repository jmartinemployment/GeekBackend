using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

public class ContentCreatorDbContext : DbContext
{
    public ContentCreatorDbContext(DbContextOptions<ContentCreatorDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<GccCreate> GccCreates => Set<GccCreate>();
    public virtual DbSet<GccArtifact> GccArtifacts => Set<GccArtifact>();
    public virtual DbSet<GccArtifactVersion> GccArtifactVersions => Set<GccArtifactVersion>();
    public virtual DbSet<GccApprovalEvent> GccApprovalEvents => Set<GccApprovalEvent>();
    public virtual DbSet<GccSiteAnalysis> GccSiteAnalyses => Set<GccSiteAnalysis>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("content_creator");

        modelBuilder.Entity<GccCreate>(entity =>
        {
            entity.ToTable("gcc_creates");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.ClientId).IsRequired();
            entity.Property(c => c.OwnerUserId).IsRequired();
            entity.Property(c => c.StartingContentType).IsRequired().HasMaxLength(64);
            entity.Property(c => c.Topic).IsRequired().HasMaxLength(1024);
            entity.Property(c => c.Notes).HasColumnType("text");
            entity.Property(c => c.SiteSectionJson).HasColumnType("text");
            entity.Property(c => c.BriefJson).HasColumnName("brief_json").HasColumnType("text");
            entity.Property(c => c.ResearchJson).HasColumnName("research_json").HasColumnType("text");
            entity.Property(c => c.Status).IsRequired().HasMaxLength(32);
            entity.Property(c => c.CreatedAtUtc).IsRequired();
            entity.Property(c => c.UpdatedAtUtc).IsRequired();
            entity.HasIndex(c => c.ClientId).HasDatabaseName("ix_gcc_creates_client_id");
            entity.HasIndex(c => c.OwnerUserId).HasDatabaseName("ix_gcc_creates_owner_user_id");
        });

        modelBuilder.Entity<GccArtifact>(entity =>
        {
            entity.ToTable("gcc_artifacts");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.CreateId).IsRequired();
            entity.Property(a => a.Type).IsRequired().HasMaxLength(64);
            entity.Property(a => a.Name).IsRequired().HasMaxLength(256);
            entity.Property(a => a.Status).IsRequired().HasMaxLength(32);
            entity.Property(a => a.CreatedAtUtc).IsRequired();
            entity.Property(a => a.UpdatedAtUtc).IsRequired();
            entity.HasIndex(a => a.CreateId).HasDatabaseName("ix_gcc_artifacts_create_id");
        });

        modelBuilder.Entity<GccArtifactVersion>(entity =>
        {
            entity.ToTable("gcc_artifact_versions");
            entity.HasKey(v => v.Id);
            entity.Property(v => v.ArtifactId).IsRequired();
            entity.Property(v => v.VersionNumber).IsRequired();
            entity.Property(v => v.BodyJson).IsRequired().HasColumnName("body_json").HasColumnType("text");
            entity.Property(v => v.MetadataJson).HasColumnName("metadata_json").HasColumnType("text");
            entity.Property(v => v.RowVersion).IsConcurrencyToken();
            entity.Property(v => v.CreatedAtUtc).IsRequired();
            entity.HasIndex(v => v.ArtifactId).HasDatabaseName("ix_gcc_artifact_versions_artifact_id");
        });

        modelBuilder.Entity<GccApprovalEvent>(entity =>
        {
            entity.ToTable("gcc_approval_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ArtifactVersionId).IsRequired();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Action).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Notes).HasColumnType("text");
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.HasIndex(e => e.ArtifactVersionId).HasDatabaseName("ix_gcc_approval_events_artifact_version_id");
        });

        modelBuilder.Entity<GccSiteAnalysis>(entity =>
        {
            entity.ToTable("gcc_site_analyses");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Domain).IsRequired().HasMaxLength(512);
            entity.Property(e => e.SeedTopic).HasMaxLength(512);
            entity.Property(e => e.GapsJson).IsRequired().HasColumnType("text");
            entity.Property(e => e.IsDemo).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.HasIndex(e => e.Domain).HasDatabaseName("ix_gcc_site_analyses_domain");
        });

        base.OnModelCreating(modelBuilder);
    }
}
