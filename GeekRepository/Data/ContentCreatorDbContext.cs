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
    public virtual DbSet<GccTask> GccTasks => Set<GccTask>();
    public virtual DbSet<GccTimeEntry> GccTimeEntries => Set<GccTimeEntry>();
    public virtual DbSet<GccDeliverable> GccDeliverables => Set<GccDeliverable>();

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
            entity.ToTable("gcc_clients", t =>
            {
                t.HasCheckConstraint(
                    "ck_gcc_clients_payment_terms_days",
                    "payment_terms_days >= 0");
                // A rate of zero is not a free client, it is an unfilled field. Absent is null.
                t.HasCheckConstraint(
                    "ck_gcc_clients_rate_positive",
                    "rate IS NULL OR rate > 0");
                t.HasCheckConstraint(
                    "ck_gcc_clients_currency_iso4217",
                    "currency ~ '^[A-Z]{3}$'");
            });
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(256);
            entity.Property(c => c.Notes).HasColumnType("text");

            entity.Property(c => c.ContactName).IsRequired().HasMaxLength(256).HasColumnName("contact_name");
            entity.Property(c => c.ContactEmail).IsRequired().HasMaxLength(320).HasColumnName("contact_email");
            entity.Property(c => c.ContactPhone).HasMaxLength(64).HasColumnName("contact_phone");
            entity.Property(c => c.BillingContactName).HasMaxLength(256).HasColumnName("billing_contact_name");
            entity.Property(c => c.BillingEmail).IsRequired().HasMaxLength(320).HasColumnName("billing_email");

            entity.Property(c => c.ContactAddressLine1).HasMaxLength(256).HasColumnName("contact_address_line1");
            entity.Property(c => c.ContactAddressLine2).HasMaxLength(256).HasColumnName("contact_address_line2");
            entity.Property(c => c.ContactCity).HasMaxLength(128).HasColumnName("contact_city");
            entity.Property(c => c.ContactRegion).HasMaxLength(128).HasColumnName("contact_region");
            entity.Property(c => c.ContactPostalCode).HasMaxLength(32).HasColumnName("contact_postal_code");
            entity.Property(c => c.ContactCountry).HasMaxLength(128).HasColumnName("contact_country");

            entity.Property(c => c.BillingAddressLine1).HasMaxLength(256).HasColumnName("billing_address_line1");
            entity.Property(c => c.BillingAddressLine2).HasMaxLength(256).HasColumnName("billing_address_line2");
            entity.Property(c => c.BillingCity).HasMaxLength(128).HasColumnName("billing_city");
            entity.Property(c => c.BillingRegion).HasMaxLength(128).HasColumnName("billing_region");
            entity.Property(c => c.BillingPostalCode).HasMaxLength(32).HasColumnName("billing_postal_code");
            entity.Property(c => c.BillingCountry).HasMaxLength(128).HasColumnName("billing_country");

            entity.Property(c => c.PaymentTermsDays).IsRequired().HasColumnName("payment_terms_days");
            entity.Property(c => c.Rate).HasColumnType("numeric(12,2)").HasColumnName("rate");
            entity.Property(c => c.Currency).IsRequired().HasColumnType("char(3)").HasColumnName("currency");
            entity.Property(c => c.TaxId).HasMaxLength(64).HasColumnName("tax_id");
            entity.Property(c => c.PoReference).HasMaxLength(64).HasColumnName("po_reference");

            entity.Property(c => c.PublishApiBaseUrl).HasMaxLength(2048).HasColumnName("publish_api_base_url");
            entity.Property(c => c.PublishOAuthTokenEndpoint).HasMaxLength(512).HasColumnName("publish_oauth_token_endpoint");
            entity.Property(c => c.PublishClientIdEnvVar).HasMaxLength(128).HasColumnName("publish_client_id_env_var");
            entity.Property(c => c.PublishClientSecretEnvVar).HasMaxLength(128).HasColumnName("publish_client_secret_env_var");
            entity.Property(c => c.PublishDefaultAuthorId).HasColumnName("publish_default_author_id");
            entity.Property(c => c.PublishCategoryStrategy).HasMaxLength(64).HasColumnName("publish_category_strategy");

            entity.Property(c => c.CreatedAtUtc).IsRequired();
            entity.Property(c => c.UpdatedAtUtc).IsRequired();
            entity.HasIndex(c => c.Name).IsUnique().HasDatabaseName("ix_gcc_clients_name_unique");
        });

        // Columns here are snake_case throughout. gcc_creates above is mixed — "SiteAnalysisId"
        // beside "brief_json" — because a column rename over live rows buys nothing; that is a
        // reason to leave it alone, not a pattern to copy into a new table.
        modelBuilder.Entity<GccProject>(entity =>
        {
            // The CHECKs are declared on the model as well as written in the migration. Declared
            // only in the migration, the model would not know they exist and the snapshot could
            // not record them — the drift that makes a later scaffolded migration wrong.
            entity.ToTable("gcc_projects", t =>
            {
                t.HasCheckConstraint(
                    "ck_gcc_projects_status",
                    "status IN ('planned', 'active', 'on_hold', 'finished', 'cancelled')");
                t.HasCheckConstraint(
                    "ck_gcc_projects_finished_date_matches_status",
                    "(status = 'finished') = (finished_date IS NOT NULL)");
                t.HasCheckConstraint(
                    "ck_gcc_projects_due_date_after_start",
                    "due_date IS NULL OR due_date >= start_date");
                t.HasCheckConstraint(
                    "ck_gcc_projects_finished_date_after_start",
                    "finished_date IS NULL OR finished_date >= start_date");
                t.HasCheckConstraint(
                    "ck_gcc_projects_budget_currency_pair",
                    "(budget IS NULL) = (budget_currency IS NULL)");
            });
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
            entity.Property(p => p.DeletedAtUtc).HasColumnName("deleted_at_utc");

            // RESTRICT, not Cascade: a client with projects is not deleted out from under them, and
            // a project that has accrued a log — every project, from its first insert — is not
            // deleted at all. Projects close through status, and "delete" is DeletedAtUtc.
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
            entity.ToTable("gcc_project_log", t =>
            {
                t.HasCheckConstraint(
                    "ck_gcc_project_log_event_type",
                    "event_type IN ('project_created', 'project_updated', 'project_status_changed')");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            // now() matches the migration's default. Declared here too, or the model believes the
            // column has no default and a later migration would try to remove one it never saw.
            entity.Property(e => e.OccurredAtUtc)
                .HasColumnName("occurred_at_utc")
                .HasDefaultValueSql("now()")
                .IsRequired();
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

        modelBuilder.Entity<GccTask>(entity =>
        {
            entity.ToTable("gcc_tasks", t =>
            {
                t.HasCheckConstraint(
                    "ck_gcc_tasks_status",
                    "status IN ('todo', 'in_progress', 'done')");
            });
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).HasColumnName("id");
            entity.Property(t => t.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(t => t.Name).HasColumnName("name").IsRequired().HasMaxLength(256);
            entity.Property(t => t.Description).HasColumnName("description").HasColumnType("text");
            entity.Property(t => t.Status).HasColumnName("status").IsRequired().HasMaxLength(32);
            entity.Property(t => t.AssigneeUserId).HasColumnName("assignee_user_id").HasMaxLength(256);
            entity.Property(t => t.DueDate).HasColumnName("due_date").HasColumnType("date");
            entity.Property(t => t.EstimatedHours).HasColumnName("estimated_hours").HasColumnType("numeric(8,2)");
            entity.Property(t => t.SortOrder).HasColumnName("sort_order").IsRequired();
            // text[], not a joined table: a small, unordered set the operator toggles, read and
            // written whole with the task — the same reasoning as PartnerUrls/CompetitorUrls on
            // GccProject. No CHECK against the twenty-value list: that list is still settling on
            // the frontend, and pinning it here would mean a migration every time it changes.
            entity.Property(t => t.ContentTypes).HasColumnName("content_types").HasColumnType("text[]").IsRequired();
            entity.Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(t => t.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            entity.HasOne<GccProject>()
                .WithMany()
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // Not redundant with the primary key: it is the target of gcc_time_entries' composite
            // foreign key, which is how the database refuses an entry whose task belongs to another
            // project.
            entity.HasAlternateKey(t => new { t.Id, t.ProjectId })
                .HasName("ak_gcc_tasks_id_project_id");

            entity.HasIndex(t => new { t.ProjectId, t.SortOrder })
                .HasDatabaseName("ix_gcc_tasks_project_id_sort_order");
        });

        modelBuilder.Entity<GccTimeEntry>(entity =>
        {
            entity.ToTable("gcc_time_entries", t =>
            {
                // Zero minutes is not a short day, it is an unsaved form.
                t.HasCheckConstraint("ck_gcc_time_entries_minutes_positive", "minutes > 0");
                // Billable without a rate and a currency cannot be invoiced, so it is refused at
                // the point of writing rather than discovered when someone tries to bill it.
                t.HasCheckConstraint(
                    "ck_gcc_time_entries_billable_has_rate",
                    "billable = false OR (rate_snapshot IS NOT NULL AND currency IS NOT NULL)");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.TaskId).HasColumnName("task_id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired().HasMaxLength(256);
            entity.Property(e => e.WorkDate).HasColumnName("work_date").HasColumnType("date").IsRequired();
            entity.Property(e => e.Minutes).HasColumnName("minutes").IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("text");
            entity.Property(e => e.Billable).HasColumnName("billable").IsRequired();
            entity.Property(e => e.RateSnapshot).HasColumnName("rate_snapshot").HasColumnType("numeric(12,2)");
            entity.Property(e => e.Currency).HasColumnName("currency").HasColumnType("char(3)");
            entity.Property(e => e.InvoicedAtUtc).HasColumnName("invoiced_at_utc");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            entity.HasOne<GccProject>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // The composite key, not a plain FK to gcc_tasks.Id: an entry's task must belong to the
            // entry's project. A plain key would happily accept a task from another engagement, and
            // the hours would be billed to the wrong client with nothing to notice it.
            entity.HasOne<GccTask>()
                .WithMany()
                .HasForeignKey(e => new { e.TaskId, e.ProjectId })
                .HasPrincipalKey(t => new { t.Id, t.ProjectId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.ProjectId, e.WorkDate })
                .HasDatabaseName("ix_gcc_time_entries_project_id_work_date");
        });

        modelBuilder.Entity<GccDeliverable>(entity =>
        {
            entity.ToTable("gcc_deliverables", t =>
            {
                t.HasCheckConstraint(
                    "ck_gcc_deliverables_status",
                    "status IN ('planned', 'in_progress', 'delivered')");
                // Delivered and its timestamp are one fact, the same pairing projects use for
                // finished/finished_date.
                t.HasCheckConstraint(
                    "ck_gcc_deliverables_delivered_at_matches_status",
                    "(status = 'delivered') = (delivered_at_utc IS NOT NULL)");
            });
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).HasColumnName("id");
            entity.Property(d => d.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(d => d.CreateId).HasColumnName("create_id").IsRequired();
            entity.Property(d => d.Name).HasColumnName("name").IsRequired().HasMaxLength(256);
            entity.Property(d => d.Type).HasColumnName("type").IsRequired().HasMaxLength(64);
            entity.Property(d => d.Status).HasColumnName("status").IsRequired().HasMaxLength(32);
            entity.Property(d => d.DueDate).HasColumnName("due_date").HasColumnType("date");
            entity.Property(d => d.DeliveredAtUtc).HasColumnName("delivered_at_utc");
            entity.Property(d => d.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(d => d.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            entity.HasOne<GccProject>()
                .WithMany()
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<GccCreate>()
                .WithMany()
                .HasForeignKey(d => d.CreateId)
                .OnDelete(DeleteBehavior.Restrict);

            // One create, one deliverable. The same piece of content listed under two projects
            // would make both schedules wrong with no way to tell which.
            entity.HasIndex(d => d.CreateId)
                .IsUnique()
                .HasDatabaseName("ix_gcc_deliverables_create_id_unique");

            entity.HasIndex(d => d.ProjectId).HasDatabaseName("ix_gcc_deliverables_project_id");
        });

        base.OnModelCreating(modelBuilder);
    }
}
