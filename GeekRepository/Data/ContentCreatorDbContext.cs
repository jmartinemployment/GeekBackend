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
    public virtual DbSet<GccSiteFinding> GccSiteFindings => Set<GccSiteFinding>();
    public virtual DbSet<GccClient> GccClients => Set<GccClient>();
    public virtual DbSet<GccProject> GccProjects => Set<GccProject>();
    public virtual DbSet<GccProjectLogEntry> GccProjectLog => Set<GccProjectLogEntry>();

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
            entity.Property(c => c.Department).IsRequired().HasMaxLength(64).HasDefaultValue("marketing");
            entity.Property(c => c.SiteSectionJson).HasColumnType("text");
            // The property was renamed; the column was not. "SiteAnalysisId" is what the live rows
            // are stored under, and a rename there is a migration over real data for no gain.
            entity.Property(c => c.ProjectSiteRunId).HasColumnName("SiteAnalysisId");
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
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.Property(e => e.ErrorMessage).HasColumnType("text");
            entity.Property(e => e.SiteModelJson).IsRequired().HasColumnType("text");
            entity.Property(e => e.GapsJson).IsRequired().HasColumnType("text");
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();
            entity.HasIndex(e => e.Domain).HasDatabaseName("ix_gcc_site_analyses_domain");
        });

        modelBuilder.Entity<GccSiteFinding>(entity =>
        {
            entity.ToTable("gcc_site_findings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SiteAnalysisId).IsRequired();
            entity.Property(e => e.FindingType).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Severity).IsRequired().HasMaxLength(32);
            entity.Property(e => e.AffectedUrl).HasMaxLength(2048);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(512);
            entity.Property(e => e.Summary).IsRequired().HasColumnType("text");
            entity.Property(e => e.DetailsJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.HasOne<GccSiteAnalysis>()
                .WithMany()
                .HasForeignKey(e => e.SiteAnalysisId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.SiteAnalysisId).HasDatabaseName("ix_gcc_site_findings_site_analysis_id");
            entity.HasIndex(e => new { e.SiteAnalysisId, e.FindingType })
                .HasDatabaseName("ix_gcc_site_findings_site_analysis_id_finding_type");
            entity.HasIndex(e => new { e.SiteAnalysisId, e.Severity })
                .HasDatabaseName("ix_gcc_site_findings_site_analysis_id_severity");
        });

        modelBuilder.Entity<GccClient>(entity =>
        {
            entity.ToTable("gcc_clients");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(256);
            entity.Property(c => c.Notes).HasColumnType("text");
            entity.Property(c => c.CreatedAtUtc).IsRequired();
            entity.Property(c => c.UpdatedAtUtc).IsRequired();
            entity.HasIndex(c => c.Name).IsUnique().HasDatabaseName("ix_gcc_clients_name_unique");
        });

        // Columns here are snake_case throughout. gcc_creates above is mixed — "SiteAnalysisId"
        // beside "brief_json" — because a column rename over live rows buys nothing; that is a
        // reason to leave it alone, not a pattern to copy into a new table.
        modelBuilder.Entity<GccProject>(entity =>
        {
            entity.ToTable("gcc_projects");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.ClientId).HasColumnName("client_id").IsRequired();
            entity.Property(p => p.IdempotencyKey).HasColumnName("idempotency_key").IsRequired();
            entity.Property(p => p.Name).HasColumnName("name").IsRequired().HasMaxLength(256);
            entity.Property(p => p.Code).HasColumnName("code").HasMaxLength(64);
            entity.Property(p => p.Description).HasColumnName("description").HasColumnType("text");
            entity.Property(p => p.Status).HasColumnName("status").IsRequired().HasMaxLength(32);
            entity.Property(p => p.SiteUrl).HasColumnName("site_url").HasMaxLength(2048);
            entity.Property(p => p.ProjectSiteRunId).HasColumnName("project_site_run_id");
            entity.Property(p => p.Department).HasColumnName("department").HasMaxLength(64);
            // text[], not a joined table: these are declarations the operator typed, read and
            // written whole with the project and never queried across projects.
            entity.Property(p => p.PartnerUrls).HasColumnName("partner_urls").HasColumnType("text[]").IsRequired();
            entity.Property(p => p.CompetitorUrls).HasColumnName("competitor_urls").HasColumnType("text[]").IsRequired();
            entity.Property(p => p.StartDate).HasColumnName("start_date").HasColumnType("date").IsRequired();
            entity.Property(p => p.DueDate).HasColumnName("due_date").HasColumnType("date");
            entity.Property(p => p.FinishedDate).HasColumnName("finished_date").HasColumnType("date");
            entity.Property(p => p.EstimatedHours).HasColumnName("estimated_hours").HasColumnType("numeric(8,2)");
            entity.Property(p => p.Budget).HasColumnName("budget").HasColumnType("numeric(12,2)");
            entity.Property(p => p.BudgetCurrency).HasColumnName("budget_currency").HasColumnType("char(3)");
            entity.Property(p => p.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(p => p.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            // RESTRICT, not Cascade: a client with projects is not deleted out from under them, and
            // a project that has accrued a log — every project, from its first insert — is not
            // deleted at all. Projects close through status.
            entity.HasOne<GccClient>()
                .WithMany()
                .HasForeignKey(p => p.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(p => p.IdempotencyKey)
                .IsUnique()
                .HasDatabaseName("ix_gcc_projects_idempotency_key_unique");
            entity.HasIndex(p => p.ClientId).HasDatabaseName("ix_gcc_projects_client_id");
            entity.HasIndex(p => new { p.ClientId, p.Code })
                .IsUnique()
                .HasFilter("code IS NOT NULL")
                .HasDatabaseName("ix_gcc_projects_client_id_code_unique");
        });

        modelBuilder.Entity<GccProjectLogEntry>(entity =>
        {
            entity.ToTable("gcc_project_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
            entity.Property(e => e.ActorUserId).HasColumnName("actor_user_id").IsRequired().HasMaxLength(256);
            entity.Property(e => e.EventType).HasColumnName("event_type").IsRequired().HasMaxLength(64);
            entity.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();

            entity.HasOne<GccProject>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.ProjectId, e.OccurredAtUtc })
                .HasDatabaseName("ix_gcc_project_log_project_id_occurred_at_utc");
        });

        base.OnModelCreating(modelBuilder);
    }
}
