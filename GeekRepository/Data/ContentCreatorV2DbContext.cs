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
    public virtual DbSet<GccV2ProjectSiteCrawlRun> GccV2ProjectSiteCrawlRuns => Set<GccV2ProjectSiteCrawlRun>();
    public virtual DbSet<GccV2ProjectSiteCrawlPage> GccV2ProjectSiteCrawlPages => Set<GccV2ProjectSiteCrawlPage>();
    public virtual DbSet<GccV2ProjectSiteCrawlLink> GccV2ProjectSiteCrawlLinks => Set<GccV2ProjectSiteCrawlLink>();
    public virtual DbSet<GccV2ResearchEntity> GccV2ResearchEntities => Set<GccV2ResearchEntity>();
    public virtual DbSet<GccV2SkillPackage> GccV2SkillPackages => Set<GccV2SkillPackage>();
    public virtual DbSet<GccV2SkillVersion> GccV2SkillVersions => Set<GccV2SkillVersion>();
    public virtual DbSet<GccV2SkillFile> GccV2SkillFiles => Set<GccV2SkillFile>();
    public virtual DbSet<GccV2SkillApplicability> GccV2SkillApplicabilities => Set<GccV2SkillApplicability>();
    public virtual DbSet<GccV2SkillReviewFinding> GccV2SkillReviewFindings => Set<GccV2SkillReviewFinding>();
    public virtual DbSet<GccV2SkillAuditEvent> GccV2SkillAuditEvents => Set<GccV2SkillAuditEvent>();
    public virtual DbSet<GccV2Agent> GccV2Agents => Set<GccV2Agent>();
    public virtual DbSet<GccV2AgentVersion> GccV2AgentVersions => Set<GccV2AgentVersion>();
    public virtual DbSet<GccV2AgentVersionSkillVersion> GccV2AgentVersionSkillVersions => Set<GccV2AgentVersionSkillVersion>();
    public virtual DbSet<GccV2AgentStageParticipation> GccV2AgentStageParticipations => Set<GccV2AgentStageParticipation>();
    public virtual DbSet<GccV2AgentAuditEvent> GccV2AgentAuditEvents => Set<GccV2AgentAuditEvent>();
    public virtual DbSet<GccV2AgentTestRun> GccV2AgentTestRuns => Set<GccV2AgentTestRun>();
    public virtual DbSet<GccV2AgentReviewFinding> GccV2AgentReviewFindings => Set<GccV2AgentReviewFinding>();
    public virtual DbSet<GccV2JobAgentVersion> GccV2JobAgentVersions => Set<GccV2JobAgentVersion>();
    public virtual DbSet<GccV2KnowledgeAsset> GccV2KnowledgeAssets => Set<GccV2KnowledgeAsset>();
    public virtual DbSet<GccV2KnowledgeAssetVersion> GccV2KnowledgeAssetVersions => Set<GccV2KnowledgeAssetVersion>();
    public virtual DbSet<GccV2KnowledgeResource> GccV2KnowledgeResources => Set<GccV2KnowledgeResource>();
    public virtual DbSet<GccV2Audience> GccV2Audiences => Set<GccV2Audience>();
    public virtual DbSet<GccV2AudienceVersion> GccV2AudienceVersions => Set<GccV2AudienceVersion>();
    public virtual DbSet<GccV2StyleGuide> GccV2StyleGuides => Set<GccV2StyleGuide>();
    public virtual DbSet<GccV2StyleGuideVersion> GccV2StyleGuideVersions => Set<GccV2StyleGuideVersion>();
    public virtual DbSet<GccV2VisualGuideline> GccV2VisualGuidelines => Set<GccV2VisualGuideline>();
    public virtual DbSet<GccV2VisualGuidelineVersion> GccV2VisualGuidelineVersions => Set<GccV2VisualGuidelineVersion>();
    public virtual DbSet<GccV2ProductSchema> GccV2ProductSchemas => Set<GccV2ProductSchema>();
    public virtual DbSet<GccV2ProductSchemaVersion> GccV2ProductSchemaVersions => Set<GccV2ProductSchemaVersion>();
    public virtual DbSet<GccV2Product> GccV2Products => Set<GccV2Product>();
    public virtual DbSet<GccV2ProductVersion> GccV2ProductVersions => Set<GccV2ProductVersion>();
    public virtual DbSet<GccV2RunAttachment> GccV2RunAttachments => Set<GccV2RunAttachment>();
    public virtual DbSet<GccV2ContextSelection> GccV2ContextSelections => Set<GccV2ContextSelection>();
    public virtual DbSet<GccV2ContextIngestionJob> GccV2ContextIngestionJobs => Set<GccV2ContextIngestionJob>();
    public virtual DbSet<GccV2ContextIngestionEvent> GccV2ContextIngestionEvents => Set<GccV2ContextIngestionEvent>();
    public virtual DbSet<GccV2RunContextManifest> GccV2RunContextManifests => Set<GccV2RunContextManifest>();
    public virtual DbSet<GccV2RunContextManifestEntry> GccV2RunContextManifestEntries => Set<GccV2RunContextManifestEntry>();
    public virtual DbSet<GccV2ContextFinding> GccV2ContextFindings => Set<GccV2ContextFinding>();
    public virtual DbSet<GccV2ContextAuditEvent> GccV2ContextAuditEvents => Set<GccV2ContextAuditEvent>();
    public virtual DbSet<GccV2TaskAgentDefinition> GccV2TaskAgentDefinitions => Set<GccV2TaskAgentDefinition>();
    public virtual DbSet<GccV2TaskAgentVersion> GccV2TaskAgentVersions => Set<GccV2TaskAgentVersion>();
    public virtual DbSet<GccV2TaskRun> GccV2TaskRuns => Set<GccV2TaskRun>();
    public virtual DbSet<GccV2TaskRunEvent> GccV2TaskRunEvents => Set<GccV2TaskRunEvent>();
    public virtual DbSet<GccV2TaskArtifact> GccV2TaskArtifacts => Set<GccV2TaskArtifact>();
    public virtual DbSet<GccV2TaskArtifactVersion> GccV2TaskArtifactVersions => Set<GccV2TaskArtifactVersion>();
    public virtual DbSet<GccV2TaskArtifactLineage> GccV2TaskArtifactLineage => Set<GccV2TaskArtifactLineage>();
    public virtual DbSet<GccV2TaskAgentLibraryPreference> GccV2TaskAgentLibraryPreferences => Set<GccV2TaskAgentLibraryPreference>();
    public virtual DbSet<GccV2GscConnection> GccV2GscConnections => Set<GccV2GscConnection>();
    public virtual DbSet<GccV2DriveConnection> GccV2DriveConnections => Set<GccV2DriveConnection>();
    public virtual DbSet<GccV2SharePointConnection> GccV2SharePointConnections => Set<GccV2SharePointConnection>();
    public virtual DbSet<GccV2CustomerOutcome> GccV2CustomerOutcomes => Set<GccV2CustomerOutcome>();
    public virtual DbSet<GccV2CanvasProject> GccV2CanvasProjects => Set<GccV2CanvasProject>();
    public virtual DbSet<GccV2CanvasAsset> GccV2CanvasAssets => Set<GccV2CanvasAsset>();
    public virtual DbSet<GccV2CanvasAssetVersion> GccV2CanvasAssetVersions => Set<GccV2CanvasAssetVersion>();
    public virtual DbSet<GccV2Grid> GccV2Grids => Set<GccV2Grid>();
    public virtual DbSet<GccV2GridRow> GccV2GridRows => Set<GccV2GridRow>();
    public virtual DbSet<GccV2GridRun> GccV2GridRuns => Set<GccV2GridRun>();
    public virtual DbSet<GccV2PipelineDefinition> GccV2PipelineDefinitions => Set<GccV2PipelineDefinition>();
    public virtual DbSet<GccV2PipelineRun> GccV2PipelineRuns => Set<GccV2PipelineRun>();
    public virtual DbSet<GccV2PipelineWorkItem> GccV2PipelineWorkItems => Set<GccV2PipelineWorkItem>();
    public virtual DbSet<GccV2PipelineStageAttempt> GccV2PipelineStageAttempts => Set<GccV2PipelineStageAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("content_creator_v2");
        modelBuilder.ConfigureGccV2GovernedContext();
        modelBuilder.ConfigureGccV2TaskAgentKernel();
        modelBuilder.ConfigureGccV2Gsc();
        modelBuilder.ConfigureGccV2Drive();
        modelBuilder.ConfigureGccV2SharePoint();
        modelBuilder.ConfigureGccV2CustomerOutcomes();
        modelBuilder.ConfigureGccV2CanvasProjects();
        modelBuilder.ConfigureGccV2Grids();
        modelBuilder.ConfigureGccV2Pipelines();

        modelBuilder.Entity<GccV2Create>(entity =>
        {
            entity.ToTable("gcc_v2_creates");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.OwnerUserId).IsRequired().HasMaxLength(128);
            entity.Property(c => c.Title).IsRequired().HasMaxLength(1024);
            entity.Property(c => c.ContentType).IsRequired().HasMaxLength(64);
            entity.Property(c => c.SiteSectionJson).HasColumnType("text");
            entity.Property(c => c.SiteUrl).HasMaxLength(2048);
            entity.Property(c => c.ProjectSiteCrawlRunId);
            entity.Property(c => c.SelectedAgentVersionIdsJson).HasColumnType("text");
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
            entity.Property(j => j.AgentTeamSnapshotJson).HasColumnType("text");
            entity.Property(j => j.AgentTeamSnapshotDigest).HasMaxLength(64);
            entity.Property(j => j.AgentTeamSnapshotSignature).HasMaxLength(64);
            entity.Property(j => j.AgentTeamSnapshotKeyId).HasMaxLength(128);
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
            entity.Property(k => k.OwnerUserId).HasMaxLength(128);
            entity.Property(k => k.CanonicalSha256).HasMaxLength(64);
            entity.HasIndex(k => k.DerivedFromProfileId).HasDatabaseName("ix_gcc_v2_brand_kits_derived_from_profile_id");
            entity.HasIndex(k => k.ClientId).HasDatabaseName("ix_gcc_v2_brand_kits_client_id");
            entity.HasIndex(k => new { k.DerivedFromProfileId, k.Version }).IsUnique()
                .HasDatabaseName("ux_gcc_v2_brand_kits_profile_version");
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
            entity.HasIndex(b => new { b.CreateId, b.Version }).IsUnique()
                .HasDatabaseName("ux_gcc_v2_briefs_create_version");
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

        modelBuilder.Entity<GccV2ProjectSiteCrawlRun>(entity =>
        {
            entity.ToTable("gcc_v2_project_site_crawl_runs");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.OwnerUserId).IsRequired().HasMaxLength(128);
            entity.Property(r => r.SiteUrl).IsRequired().HasMaxLength(2048);
            entity.Property(r => r.Status).IsRequired().HasMaxLength(32).HasDefaultValue("pending");
            entity.Property(r => r.SeedUrlsJson).IsRequired().HasColumnType("text").HasDefaultValue("[]");
            entity.Property(r => r.HostProgressJson).HasColumnType("text");
            entity.Property(r => r.ErrorSummary).HasMaxLength(2048);
            entity.Property(r => r.CreatedAtUtc).IsRequired();
            entity.HasIndex(r => new { r.OwnerUserId, r.SiteUrl, r.CreatedAtUtc })
                .HasDatabaseName("ix_gcc_v2_project_site_crawl_runs_owner_site_created");
        });

        modelBuilder.Entity<GccV2ProjectSiteCrawlPage>(entity =>
        {
            entity.ToTable("gcc_v2_project_site_crawl_pages");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.RunId).IsRequired();
            entity.Property(p => p.Origin).IsRequired().HasMaxLength(512);
            entity.Property(p => p.Url).IsRequired().HasMaxLength(2048);
            entity.Property(p => p.FinalUrl).IsRequired().HasMaxLength(2048);
            entity.Property(p => p.Html).HasColumnType("text");
            entity.Property(p => p.CrawledAtUtc).IsRequired();
            entity.HasIndex(p => p.RunId).HasDatabaseName("ix_gcc_v2_project_site_crawl_pages_run_id");
            entity.HasIndex(p => new { p.RunId, p.Url }).HasDatabaseName("ix_gcc_v2_project_site_crawl_pages_run_url");
        });

        modelBuilder.Entity<GccV2ProjectSiteCrawlLink>(entity =>
        {
            entity.ToTable("gcc_v2_project_site_crawl_links");
            entity.HasKey(l => l.Id);
            entity.Property(l => l.RunId).IsRequired();
            entity.Property(l => l.PageId).IsRequired();
            entity.Property(l => l.FromUrl).IsRequired().HasMaxLength(2048);
            entity.Property(l => l.LinkUrl).IsRequired().HasMaxLength(2048);
            entity.Property(l => l.DiscoveredAtUtc).IsRequired();
            entity.HasIndex(l => l.RunId).HasDatabaseName("ix_gcc_v2_project_site_crawl_links_run_id");
        });

        modelBuilder.Entity<GccV2SkillPackage>(entity =>
        {
            entity.ToTable("gcc_v2_skill_packages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Slug).IsRequired().HasMaxLength(128);
            entity.Property(x => x.DisplayName).IsRequired().HasMaxLength(256);
            entity.Property(x => x.Description).IsRequired().HasMaxLength(2048);
            entity.Property(x => x.SourceRepository).IsRequired().HasMaxLength(2048);
            entity.Property(x => x.SourcePath).IsRequired().HasMaxLength(1024);
            entity.Property(x => x.Publisher).IsRequired().HasMaxLength(256);
            entity.Property(x => x.LifecycleState).IsRequired().HasMaxLength(32);
            entity.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_gcc_v2_skill_packages_slug");
        });

        modelBuilder.Entity<GccV2SkillVersion>(entity =>
        {
            entity.ToTable("gcc_v2_skill_versions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SemanticVersion).IsRequired().HasMaxLength(64);
            entity.Property(x => x.ImmutableGitRef).IsRequired().HasMaxLength(128);
            entity.Property(x => x.PackageSha256).IsRequired().HasMaxLength(64);
            entity.Property(x => x.ManifestDigest).IsRequired().HasMaxLength(64);
            entity.Property(x => x.License).IsRequired().HasMaxLength(256);
            entity.Property(x => x.Compatibility).IsRequired().HasMaxLength(1024);
            entity.Property(x => x.State).IsRequired().HasMaxLength(32);
            entity.Property(x => x.Reviewer).HasMaxLength(256);
            entity.Property(x => x.ReviewNotes).HasMaxLength(4096);
            entity.HasIndex(x => new { x.PackageId, x.SemanticVersion }).IsUnique()
                .HasDatabaseName("ux_gcc_v2_skill_versions_package_semver");
            entity.HasIndex(x => x.PackageSha256).IsUnique()
                .HasDatabaseName("ux_gcc_v2_skill_versions_package_sha256");
            entity.HasOne(x => x.Package).WithMany(x => x.Versions).HasForeignKey(x => x.PackageId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GccV2SkillFile>(entity =>
        {
            entity.ToTable("gcc_v2_skill_files");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RelativePath).IsRequired().HasMaxLength(1024);
            entity.Property(x => x.MediaType).IsRequired().HasMaxLength(128);
            entity.Property(x => x.Sha256).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Content).IsRequired().HasColumnType("text");
            entity.HasIndex(x => new { x.VersionId, x.RelativePath }).IsUnique()
                .HasDatabaseName("ux_gcc_v2_skill_files_version_path");
            entity.HasOne(x => x.Version).WithMany(x => x.Files).HasForeignKey(x => x.VersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GccV2SkillApplicability>(entity =>
        {
            entity.ToTable("gcc_v2_skill_applicabilities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Stage).IsRequired().HasMaxLength(64);
            entity.Property(x => x.ContentType).IsRequired().HasMaxLength(64);
            entity.Property(x => x.ConflictsJson).IsRequired().HasColumnType("text");
            entity.Property(x => x.RequiredToolsJson).IsRequired().HasColumnType("text");
            entity.Property(x => x.ActivationMode).IsRequired().HasMaxLength(32);
            entity.HasIndex(x => new { x.VersionId, x.Stage, x.ContentType }).IsUnique()
                .HasDatabaseName("ux_gcc_v2_skill_applicability_scope");
            entity.HasOne(x => x.Version).WithMany(x => x.Applicability).HasForeignKey(x => x.VersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GccV2SkillReviewFinding>(entity =>
        {
            entity.ToTable("gcc_v2_skill_review_findings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Severity).IsRequired().HasMaxLength(32);
            entity.Property(x => x.Scanner).IsRequired().HasMaxLength(128);
            entity.Property(x => x.Rule).IsRequired().HasMaxLength(128);
            entity.Property(x => x.FilePath).HasMaxLength(1024);
            entity.Property(x => x.Message).IsRequired().HasMaxLength(4096);
            entity.Property(x => x.Disposition).IsRequired().HasMaxLength(32);
            entity.Property(x => x.ReviewerRationale).HasMaxLength(4096);
            entity.HasOne(x => x.Version).WithMany(x => x.Findings).HasForeignKey(x => x.VersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GccV2SkillAuditEvent>(entity =>
        {
            entity.ToTable("gcc_v2_skill_audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Actor).IsRequired().HasMaxLength(256);
            entity.Property(x => x.Action).IsRequired().HasMaxLength(64);
            entity.Property(x => x.SourceIp).HasMaxLength(128);
            entity.Property(x => x.RequestId).HasMaxLength(256);
            entity.Property(x => x.BeforeState).HasMaxLength(32);
            entity.Property(x => x.AfterState).HasMaxLength(32);
            entity.HasIndex(x => new { x.PackageId, x.CreatedAtUtc })
                .HasDatabaseName("ix_gcc_v2_skill_audit_package_created");
        });

        modelBuilder.Entity<GccV2Agent>(entity =>
        {
            entity.ToTable("gcc_v2_agents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Slug).IsRequired().HasMaxLength(128);
            entity.Property(x => x.DisplayName).IsRequired().HasMaxLength(256);
            entity.Property(x => x.Description).IsRequired().HasMaxLength(2048);
            entity.Property(x => x.LifecycleState).IsRequired().HasMaxLength(32);
            entity.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_gcc_v2_agents_slug");
        });

        modelBuilder.Entity<GccV2AgentVersion>(entity =>
        {
            entity.ToTable("gcc_v2_agent_versions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SemanticVersion).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Objective).IsRequired().HasMaxLength(2000);
            entity.Property(x => x.Instructions).IsRequired().HasColumnType("text");
            entity.Property(x => x.ContentTypesJson).IsRequired().HasColumnType("text");
            entity.Property(x => x.AllowedToolsJson).IsRequired().HasColumnType("text");
            entity.Property(x => x.AllowedModelsJson).IsRequired().HasColumnType("text");
            entity.Property(x => x.ModelPolicyVersion).IsRequired().HasMaxLength(128);
            entity.Property(x => x.ModelPolicyProfile).IsRequired().HasMaxLength(128);
            entity.Property(x => x.VersionDigest).IsRequired().HasMaxLength(64);
            entity.Property(x => x.State).IsRequired().HasMaxLength(32);
            entity.Property(x => x.Reviewer).HasMaxLength(256);
            entity.Property(x => x.ReviewNotes).HasMaxLength(4096);
            entity.Property(x => x.TestResultJson).HasColumnType("text");
            entity.HasIndex(x => new { x.AgentId, x.SemanticVersion }).IsUnique()
                .HasDatabaseName("ux_gcc_v2_agent_versions_agent_semver");
            entity.HasIndex(x => x.VersionDigest).IsUnique()
                .HasDatabaseName("ux_gcc_v2_agent_versions_digest");
            entity.HasOne(x => x.Agent).WithMany(x => x.Versions).HasForeignKey(x => x.AgentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GccV2AgentVersionSkillVersion>(entity =>
        {
            entity.ToTable("gcc_v2_agent_version_skills");
            entity.HasKey(x => new { x.AgentVersionId, x.SkillVersionId });
            entity.HasOne(x => x.AgentVersion).WithMany(x => x.Skills).HasForeignKey(x => x.AgentVersionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SkillVersion).WithMany().HasForeignKey(x => x.SkillVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GccV2AgentStageParticipation>(entity =>
        {
            entity.ToTable("gcc_v2_agent_stage_participation");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Stage).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Role).IsRequired().HasMaxLength(32);
            entity.HasIndex(x => new { x.AgentVersionId, x.Stage, x.Role }).IsUnique()
                .HasDatabaseName("ux_gcc_v2_agent_stage_role");
            entity.HasOne(x => x.AgentVersion).WithMany(x => x.StageParticipation)
                .HasForeignKey(x => x.AgentVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GccV2AgentAuditEvent>(entity =>
        {
            entity.ToTable("gcc_v2_agent_audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Actor).IsRequired().HasMaxLength(256);
            entity.Property(x => x.Action).IsRequired().HasMaxLength(64);
            entity.Property(x => x.BeforeState).HasMaxLength(32);
            entity.Property(x => x.AfterState).HasMaxLength(32);
            entity.Property(x => x.DetailJson).HasColumnType("text");
            entity.Property(x => x.SourceIp).HasMaxLength(128);
            entity.Property(x => x.RequestId).HasMaxLength(256);
            entity.HasIndex(x => new { x.AgentId, x.CreatedAtUtc })
                .HasDatabaseName("ix_gcc_v2_agent_audit_agent_created");
        });

        modelBuilder.Entity<GccV2AgentTestRun>(entity =>
        {
            entity.ToTable("gcc_v2_agent_test_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.VersionDigest).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Scenario).IsRequired().HasMaxLength(128);
            entity.Property(x => x.Status).IsRequired().HasMaxLength(32);
            entity.Property(x => x.Phase).IsRequired().HasMaxLength(128);
            entity.Property(x => x.RequestedBy).IsRequired().HasMaxLength(256);
            entity.Property(x => x.ClaimedByInstanceId).HasMaxLength(128);
            entity.Property(x => x.Error).HasMaxLength(4096);
            entity.HasIndex(x => new { x.AgentVersionId, x.QueuedAtUtc })
                .HasDatabaseName("ix_gcc_v2_agent_test_version_queued");
            entity.HasIndex(x => new { x.Status, x.LeaseUntilUtc })
                .HasDatabaseName("ix_gcc_v2_agent_test_status_lease");
            entity.HasOne(x => x.AgentVersion).WithMany(x => x.TestRuns)
                .HasForeignKey(x => x.AgentVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GccV2AgentReviewFinding>(entity =>
        {
            entity.ToTable("gcc_v2_agent_review_findings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Severity).IsRequired().HasMaxLength(32);
            entity.Property(x => x.Rule).IsRequired().HasMaxLength(128);
            entity.Property(x => x.Message).IsRequired().HasMaxLength(2000);
            entity.Property(x => x.Disposition).IsRequired().HasMaxLength(32);
            entity.Property(x => x.ReviewerRationale).HasMaxLength(2000);
            entity.HasIndex(x => new { x.AgentVersionId, x.Disposition })
                .HasDatabaseName("ix_gcc_v2_agent_finding_version_disposition");
            entity.HasOne(x => x.AgentVersion).WithMany(x => x.Findings)
                .HasForeignKey(x => x.AgentVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GccV2JobAgentVersion>(entity =>
        {
            entity.ToTable("gcc_v2_job_agent_versions");
            entity.HasKey(x => new { x.JobId, x.AgentVersionId });
            entity.HasOne(x => x.Job).WithMany(x => x.AgentVersions).HasForeignKey(x => x.JobId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AgentVersion).WithMany().HasForeignKey(x => x.AgentVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GccV2ResearchEntity>(entity =>
        {
            entity.ToTable("gcc_v2_research_entities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(256);
            entity.Property(x => x.Role).IsRequired().HasMaxLength(32);
            entity.Property(x => x.PrimaryUrl).HasMaxLength(2048);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.CreatedBy).IsRequired().HasMaxLength(256);
            entity.HasIndex(x => new { x.Role, x.Name })
                .HasDatabaseName("ix_gcc_v2_research_entities_role_name");
            entity.HasIndex(x => x.Name).IsUnique()
                .HasDatabaseName("ux_gcc_v2_research_entities_name");
        });
    }
}
