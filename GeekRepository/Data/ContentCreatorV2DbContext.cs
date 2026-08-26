using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

/// <summary>Isolated schema <c>content_creator_v2</c> — never shares tables with v1.</summary>
public class ContentCreatorV2DbContext : DbContext
{
    public ContentCreatorV2DbContext(DbContextOptions<ContentCreatorV2DbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<GccV2Create> GccV2Creates => Set<GccV2Create>();
    public virtual DbSet<GccV2Job> GccV2Jobs => Set<GccV2Job>();
    public virtual DbSet<GccV2JobEvent> GccV2JobEvents => Set<GccV2JobEvent>();
    public virtual DbSet<GccV2StageResult> GccV2StageResults => Set<GccV2StageResult>();
    public virtual DbSet<GccV2Brief> GccV2Briefs => Set<GccV2Brief>();
    public virtual DbSet<GccV2BrandKit> GccV2BrandKits => Set<GccV2BrandKit>();
    public virtual DbSet<GccV2Outline> GccV2Outlines => Set<GccV2Outline>();
    public virtual DbSet<GccV2GuardrailRule> GccV2GuardrailRules => Set<GccV2GuardrailRule>();
    public virtual DbSet<GccV2PublishRecord> GccV2PublishRecords => Set<GccV2PublishRecord>();
    public virtual DbSet<GccV2AiVisibilitySnapshot> GccV2AiVisibilitySnapshots => Set<GccV2AiVisibilitySnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("content_creator_v2");

        modelBuilder.Entity<GccV2Create>(entity =>
        {
            entity.ToTable("gcc_v2_creates");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.OwnerUserId).IsRequired().HasMaxLength(128);
            entity.Property(c => c.Title).IsRequired().HasMaxLength(1024);
            entity.Property(c => c.ContentType).IsRequired().HasMaxLength(64);
            entity.Property(c => c.SiteSectionJson).HasColumnType("text");
            entity.Property(c => c.SiteUrl).HasMaxLength(2048);
            entity.Property(c => c.CreatedAtUtc).IsRequired();
            entity.HasIndex(c => c.OwnerUserId).HasDatabaseName("ix_gcc_v2_creates_owner_user_id");
        });

        modelBuilder.Entity<GccV2Job>(entity =>
        {
            entity.ToTable("gcc_v2_jobs");
            entity.HasKey(j => j.Id);
            entity.Property(j => j.ContentType).IsRequired().HasMaxLength(64);
            entity.Property(j => j.BriefId).IsRequired();
            entity.Property(j => j.OwnerUserId).IsRequired().HasMaxLength(128);
            entity.Property(j => j.CreateId).IsRequired();
            entity.Property(j => j.Stage).IsRequired().HasMaxLength(32).HasDefaultValue("plan");
            entity.Property(j => j.Status).IsRequired().HasMaxLength(32).HasDefaultValue("pending");
            entity.Property(j => j.AttemptCount).IsRequired().HasDefaultValue(0);
            entity.Property(j => j.ResultJson).HasColumnType("text");
            entity.Property(j => j.Error).HasColumnType("text");
            entity.Property(j => j.ClaimedByInstanceId).HasMaxLength(128);
            entity.Property(j => j.CreatedAtUtc).IsRequired();
            entity.HasIndex(j => j.OwnerUserId).HasDatabaseName("ix_gcc_v2_jobs_owner_user_id");
            entity.HasIndex(j => j.CreateId).HasDatabaseName("ix_gcc_v2_jobs_create_id");
            entity.HasIndex(j => new { j.Status, j.LeaseUntilUtc })
                .HasDatabaseName("ix_gcc_v2_jobs_status_lease_until_utc");
        });

        modelBuilder.Entity<GccV2BrandKit>(entity =>
        {
            entity.ToTable("gcc_v2_brand_kits");
            entity.HasKey(k => k.Id);
            entity.Property(k => k.DerivedFromProfileId).IsRequired();
            entity.Property(k => k.Version).IsRequired().HasDefaultValue(1);
            entity.Property(k => k.KitJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
            entity.Property(k => k.VoiceStatus).IsRequired().HasMaxLength(32).HasDefaultValue("provisional");
            entity.Property(k => k.DerivedAtUtc).IsRequired();
            entity.HasIndex(k => k.DerivedFromProfileId).HasDatabaseName("ix_gcc_v2_brand_kits_derived_from_profile_id");
            entity.HasIndex(k => k.ClientId).HasDatabaseName("ix_gcc_v2_brand_kits_client_id");
        });

        modelBuilder.Entity<GccV2Outline>(entity =>
        {
            entity.ToTable("gcc_v2_outlines");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.BriefId).IsRequired();
            entity.Property(o => o.Version).IsRequired().HasDefaultValue(1);
            entity.Property(o => o.OutlineJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
            entity.Property(o => o.HierarchyChildHeadingsJson).IsRequired().HasColumnType("text").HasDefaultValue("[]");
            entity.Property(o => o.CreatedAtUtc).IsRequired();
            entity.HasIndex(o => o.BriefId).HasDatabaseName("ix_gcc_v2_outlines_brief_id");
        });

        modelBuilder.Entity<GccV2JobEvent>(entity =>
        {
            entity.ToTable("gcc_v2_job_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.JobId).IsRequired();
            entity.Property(e => e.Seq).IsRequired();
            entity.Property(e => e.Type).IsRequired().HasMaxLength(128);
            entity.Property(e => e.PayloadJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.HasIndex(e => new { e.JobId, e.Seq })
                .IsUnique()
                .HasDatabaseName("ux_gcc_v2_job_events_job_id_seq");
        });

        modelBuilder.Entity<GccV2StageResult>(entity =>
        {
            entity.ToTable("gcc_v2_stage_results");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.JobId).IsRequired();
            entity.Property(r => r.Stage).IsRequired().HasMaxLength(32);
            entity.Property(r => r.SectionKey).HasMaxLength(256);
            entity.Property(r => r.OutputJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
            entity.Property(r => r.TokensUsed).IsRequired().HasDefaultValue(0);
            entity.Property(r => r.CompletedAtUtc).IsRequired();
            entity.HasIndex(r => r.JobId).HasDatabaseName("ix_gcc_v2_stage_results_job_id");
        });

        modelBuilder.Entity<GccV2GuardrailRule>(entity =>
        {
            entity.ToTable("gcc_v2_guardrail_rules");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Pattern).IsRequired().HasMaxLength(512);
            entity.Property(r => r.Action).IsRequired().HasMaxLength(32).HasDefaultValue("strip");
            entity.Property(r => r.ReplaceWith).HasMaxLength(512);
            entity.Property(r => r.Enabled).IsRequired().HasDefaultValue(true);
            entity.Property(r => r.Scope).HasMaxLength(64);
            entity.Property(r => r.ReasonCode).HasMaxLength(64);
            entity.Property(r => r.CreatedAtUtc).IsRequired();
            entity.HasIndex(r => r.Enabled).HasDatabaseName("ix_gcc_v2_guardrail_rules_enabled");
        });

        modelBuilder.Entity<GccV2Brief>(entity =>
        {
            entity.ToTable("gcc_v2_briefs");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.CreateId).IsRequired();
            entity.Property(b => b.Version).IsRequired().HasDefaultValue(1);
            entity.Property(b => b.TargetKeyword).IsRequired().HasMaxLength(512);
            entity.Property(b => b.ContentType).IsRequired().HasMaxLength(64);
            entity.Property(b => b.RawBriefJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
            entity.Property(b => b.CreatedAtUtc).IsRequired();
            entity.HasIndex(b => b.CreateId).HasDatabaseName("ix_gcc_v2_briefs_create_id");
        });

        modelBuilder.Entity<GccV2PublishRecord>(entity =>
        {
            entity.ToTable("gcc_v2_publish_records");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.OwnerUserId).IsRequired().HasMaxLength(128);
            entity.Property(r => r.Channel).IsRequired().HasMaxLength(32).HasDefaultValue("blog");
            entity.Property(r => r.Status).IsRequired().HasMaxLength(32).HasDefaultValue("draft");
            entity.Property(r => r.Slug).IsRequired().HasMaxLength(512);
            entity.Property(r => r.PublicUrl).HasMaxLength(1024);
            entity.Property(r => r.Title).IsRequired().HasMaxLength(1024);
            entity.Property(r => r.MetaDescription).HasMaxLength(512);
            entity.Property(r => r.Error).HasColumnType("text");
            entity.Property(r => r.BodyDocumentJson).HasColumnType("text");
            entity.Property(r => r.IsPublished).IsRequired().HasDefaultValue(false);
            entity.Property(r => r.CreatedAtUtc).IsRequired();
            entity.HasIndex(r => r.CreateId).HasDatabaseName("ix_gcc_v2_publish_records_create_id");
            entity.HasIndex(r => r.JobId).HasDatabaseName("ix_gcc_v2_publish_records_job_id");
            entity.HasIndex(r => r.OwnerUserId).HasDatabaseName("ix_gcc_v2_publish_records_owner_user_id");
        });

        modelBuilder.Entity<GccV2AiVisibilitySnapshot>(entity =>
        {
            entity.ToTable("gcc_v2_ai_visibility_snapshots");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.OwnerUserId).IsRequired().HasMaxLength(128);
            entity.Property(s => s.Score).IsRequired().HasDefaultValue(0);
            entity.Property(s => s.ReportJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
            entity.Property(s => s.CreatedAtUtc).IsRequired();
            entity.HasIndex(s => s.CreateId).HasDatabaseName("ix_gcc_v2_ai_visibility_snapshots_create_id");
            entity.HasIndex(s => s.JobId).HasDatabaseName("ix_gcc_v2_ai_visibility_snapshots_job_id");
            entity.HasIndex(s => s.OwnerUserId).HasDatabaseName("ix_gcc_v2_ai_visibility_snapshots_owner_user_id");
        });
    }
}
