using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeekRepository.Data;

internal static class GccV2GovernedContextConfiguration
{
    public static void ConfigureGccV2GovernedContext(this ModelBuilder modelBuilder)
    {
        ConfigureCatalog(modelBuilder.Entity<GccV2KnowledgeAsset>(), "gcc_v2_knowledge_assets");
        ConfigureVersion(modelBuilder.Entity<GccV2KnowledgeAssetVersion>(), "gcc_v2_knowledge_asset_versions");
        modelBuilder.Entity<GccV2KnowledgeAsset>().Property(x => x.TagsJson).HasColumnType("text");
        modelBuilder.Entity<GccV2KnowledgeAsset>().Property(x => x.Kind).HasMaxLength(64);
        modelBuilder.Entity<GccV2KnowledgeAsset>().Property(x => x.Visibility).HasMaxLength(32);
        modelBuilder.Entity<GccV2KnowledgeAssetVersion>().Property(x => x.SourceDescriptorJson).HasColumnType("text");
        modelBuilder.Entity<GccV2KnowledgeAssetVersion>().Property(x => x.ContentSha256).HasMaxLength(64);
        modelBuilder.Entity<GccV2KnowledgeAssetVersion>().Property(x => x.CanonicalSha256).HasMaxLength(64);
        modelBuilder.Entity<GccV2KnowledgeAssetVersion>().Property(x => x.MediaType).HasMaxLength(128);
        modelBuilder.Entity<GccV2KnowledgeAssetVersion>().Property(x => x.Language).HasMaxLength(32);
        modelBuilder.Entity<GccV2KnowledgeAssetVersion>().Property(x => x.ExtractionState).HasMaxLength(32);
        modelBuilder.Entity<GccV2KnowledgeAssetVersion>().Property(x => x.IndexState).HasMaxLength(32);
        modelBuilder.Entity<GccV2KnowledgeAssetVersion>().Property(x => x.ProvenanceJson).HasColumnType("text");
        modelBuilder.Entity<GccV2KnowledgeAssetVersion>()
            .HasOne(x => x.Asset).WithMany(x => x.Versions).HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GccV2KnowledgeAssetVersion>().HasIndex(x => new { x.AssetId, x.VersionNumber }).IsUnique();

        var resource = modelBuilder.Entity<GccV2KnowledgeResource>();
        resource.ToTable("gcc_v2_knowledge_resources");
        resource.HasKey(x => x.Id);
        resource.Property(x => x.ResourceKind).HasMaxLength(32);
        resource.Property(x => x.ObjectKey).HasMaxLength(1024);
        resource.Property(x => x.Sha256).HasMaxLength(64);
        resource.Property(x => x.MediaType).HasMaxLength(128);
        resource.Property(x => x.SafeFileName).HasMaxLength(256);
        resource.Property(x => x.ParserName).HasMaxLength(128);
        resource.Property(x => x.ParserVersion).HasMaxLength(64);
        resource.Property(x => x.ExtractionSha256).HasMaxLength(64);
        resource.Property(x => x.CoordinatesJson).HasColumnType("text");
        resource.Property(x => x.ScanState).HasMaxLength(32);
        resource.HasIndex(x => new { x.KnowledgeAssetVersionId, x.ResourceKind, x.Sha256 }).IsUnique();
        resource.HasOne(x => x.Version).WithMany(x => x.Resources).HasForeignKey(x => x.KnowledgeAssetVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        ConfigureCatalog(modelBuilder.Entity<GccV2Audience>(), "gcc_v2_audiences");
        ConfigureVersion(modelBuilder.Entity<GccV2AudienceVersion>(), "gcc_v2_audience_versions");
        modelBuilder.Entity<GccV2AudienceVersion>().Property(x => x.DefinitionJson).HasColumnType("text");
        modelBuilder.Entity<GccV2AudienceVersion>().Property(x => x.Locale).HasMaxLength(32);
        modelBuilder.Entity<GccV2AudienceVersion>().Property(x => x.ProvenanceJson).HasColumnType("text");
        modelBuilder.Entity<GccV2AudienceVersion>().HasOne(x => x.Audience).WithMany(x => x.Versions)
            .HasForeignKey(x => x.AudienceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GccV2AudienceVersion>().HasIndex(x => new { x.AudienceId, x.VersionNumber }).IsUnique();

        ConfigureCatalog(modelBuilder.Entity<GccV2StyleGuide>(), "gcc_v2_style_guides");
        ConfigureVersion(modelBuilder.Entity<GccV2StyleGuideVersion>(), "gcc_v2_style_guide_versions");
        modelBuilder.Entity<GccV2StyleGuideVersion>().Property(x => x.PolicyJson).HasColumnType("text");
        modelBuilder.Entity<GccV2StyleGuideVersion>().Property(x => x.Locale).HasMaxLength(32);
        modelBuilder.Entity<GccV2StyleGuideVersion>().HasOne(x => x.StyleGuide).WithMany(x => x.Versions)
            .HasForeignKey(x => x.StyleGuideId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GccV2StyleGuideVersion>().HasIndex(x => new { x.StyleGuideId, x.VersionNumber }).IsUnique();

        ConfigureCatalog(modelBuilder.Entity<GccV2VisualGuideline>(), "gcc_v2_visual_guidelines");
        ConfigureVersion(modelBuilder.Entity<GccV2VisualGuidelineVersion>(), "gcc_v2_visual_guideline_versions");
        modelBuilder.Entity<GccV2VisualGuidelineVersion>().Property(x => x.PolicyJson).HasColumnType("text");
        modelBuilder.Entity<GccV2VisualGuidelineVersion>().Property(x => x.Locale).HasMaxLength(32);
        modelBuilder.Entity<GccV2VisualGuidelineVersion>().HasOne(x => x.VisualGuideline).WithMany(x => x.Versions)
            .HasForeignKey(x => x.VisualGuidelineId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GccV2VisualGuidelineVersion>().HasIndex(x => new { x.VisualGuidelineId, x.VersionNumber }).IsUnique();

        ConfigureCatalog(modelBuilder.Entity<GccV2ProductSchema>(), "gcc_v2_product_schemas");
        ConfigureVersion(modelBuilder.Entity<GccV2ProductSchemaVersion>(), "gcc_v2_product_schema_versions");
        modelBuilder.Entity<GccV2ProductSchemaVersion>().Property(x => x.FieldsJson).HasColumnType("text");
        modelBuilder.Entity<GccV2ProductSchemaVersion>().HasOne(x => x.ProductSchema).WithMany(x => x.Versions)
            .HasForeignKey(x => x.ProductSchemaId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GccV2ProductSchemaVersion>().HasIndex(x => new { x.ProductSchemaId, x.VersionNumber }).IsUnique();

        ConfigureCatalog(modelBuilder.Entity<GccV2Product>(), "gcc_v2_products");
        ConfigureVersion(modelBuilder.Entity<GccV2ProductVersion>(), "gcc_v2_product_versions");
        modelBuilder.Entity<GccV2ProductVersion>().Property(x => x.FieldValuesJson).HasColumnType("text");
        modelBuilder.Entity<GccV2ProductVersion>().Property(x => x.AttributeProvenanceJson).HasColumnType("text");
        modelBuilder.Entity<GccV2ProductVersion>().Property(x => x.ApprovedClaimsJson).HasColumnType("text");
        modelBuilder.Entity<GccV2ProductVersion>().Property(x => x.ProhibitedClaimsJson).HasColumnType("text");
        modelBuilder.Entity<GccV2ProductVersion>().Property(x => x.MandatoryDisclaimersJson).HasColumnType("text");
        modelBuilder.Entity<GccV2ProductVersion>().HasOne(x => x.Product).WithMany(x => x.Versions)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GccV2ProductVersion>().HasOne<GccV2ProductSchemaVersion>()
            .WithMany().HasForeignKey(x => x.ProductSchemaVersionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GccV2ProductVersion>().HasIndex(x => new { x.ProductId, x.VersionNumber }).IsUnique();

        var attachment = modelBuilder.Entity<GccV2RunAttachment>();
        attachment.ToTable("gcc_v2_run_attachments");
        attachment.HasKey(x => x.Id);
        attachment.Property(x => x.OwnerUserId).HasMaxLength(128);
        attachment.Property(x => x.ObjectKey).HasMaxLength(1024);
        attachment.Property(x => x.SafeFileName).HasMaxLength(256);
        attachment.Property(x => x.MediaType).HasMaxLength(128);
        attachment.Property(x => x.Sha256).HasMaxLength(64);
        attachment.Property(x => x.IngestionState).HasMaxLength(32);
        attachment.HasIndex(x => new { x.OwnerUserId, x.CreateId });
        attachment.HasIndex(x => x.ObjectKey).IsUnique();

        var selection = modelBuilder.Entity<GccV2ContextSelection>();
        selection.ToTable("gcc_v2_context_selections");
        selection.HasKey(x => x.Id);
        selection.Property(x => x.OwnerUserId).HasMaxLength(128);
        selection.Property(x => x.CreatedBy).HasMaxLength(128);
        selection.Property(x => x.SelectionJson).HasColumnType("text");
        selection.HasIndex(x => new { x.OwnerUserId, x.CreateId, x.CreatedAtUtc });

        var ingestion = modelBuilder.Entity<GccV2ContextIngestionJob>();
        ingestion.ToTable("gcc_v2_context_ingestion_jobs");
        ingestion.HasKey(x => x.Id);
        ingestion.Property(x => x.OwnerUserId).HasMaxLength(128);
        ingestion.Property(x => x.TargetKind).HasMaxLength(32);
        ingestion.Property(x => x.Status).HasMaxLength(32);
        ingestion.Property(x => x.ClaimedByInstanceId).HasMaxLength(128);
        ingestion.Property(x => x.TerminalError).HasMaxLength(4096);
        ingestion.HasIndex(x => new { x.Status, x.LeaseUntilUtc });
        ingestion.HasIndex(x => new { x.OwnerUserId, x.TargetKind, x.TargetId });

        var ingestionEvent = modelBuilder.Entity<GccV2ContextIngestionEvent>();
        ingestionEvent.ToTable("gcc_v2_context_ingestion_events");
        ingestionEvent.HasKey(x => x.Id);
        ingestionEvent.Property(x => x.Type).HasMaxLength(64);
        ingestionEvent.Property(x => x.PayloadJson).HasColumnType("text");
        ingestionEvent.HasIndex(x => new { x.JobId, x.Seq }).IsUnique();
        ingestionEvent.HasOne(x => x.Job).WithMany(x => x.Events).HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        var manifest = modelBuilder.Entity<GccV2RunContextManifest>();
        manifest.ToTable("gcc_v2_run_context_manifests");
        manifest.HasKey(x => x.Id);
        manifest.Property(x => x.OwnerUserId).HasMaxLength(128);
        manifest.Property(x => x.CanonicalJson).HasColumnType("text");
        manifest.Property(x => x.Sha256).HasMaxLength(64);
        manifest.Property(x => x.Signature).HasMaxLength(128);
        manifest.Property(x => x.SigningKeyId).HasMaxLength(128);
        manifest.Property(x => x.ResolverIdentity).HasMaxLength(128);
        manifest.HasIndex(x => new { x.JobId, x.Attempt }).IsUnique();
        manifest.HasIndex(x => x.Sha256);
        manifest.HasOne<GccV2Job>().WithOne().HasForeignKey<GccV2RunContextManifest>(x => x.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        var entry = modelBuilder.Entity<GccV2RunContextManifestEntry>();
        entry.ToTable("gcc_v2_run_context_manifest_entries");
        entry.HasKey(x => x.Id);
        entry.Property(x => x.ContextKind).HasMaxLength(64);
        entry.Property(x => x.ContentSha256).HasMaxLength(64);
        entry.Property(x => x.LifecycleDecision).HasMaxLength(32);
        entry.Property(x => x.PermissionDecision).HasMaxLength(32);
        entry.Property(x => x.FreshnessDecision).HasMaxLength(32);
        entry.Property(x => x.SelectionSource).HasMaxLength(32);
        entry.Property(x => x.SelectedFieldIdsJson).HasColumnType("text");
        entry.HasIndex(x => new { x.ManifestId, x.ContextKind, x.StableId, x.VersionId }).IsUnique();
        entry.HasOne(x => x.Manifest).WithMany(x => x.Entries).HasForeignKey(x => x.ManifestId)
            .OnDelete(DeleteBehavior.Restrict);

        var finding = modelBuilder.Entity<GccV2ContextFinding>();
        finding.ToTable("gcc_v2_context_findings");
        finding.HasKey(x => x.Id);
        finding.Property(x => x.OwnerUserId).HasMaxLength(128);
        finding.Property(x => x.TargetKind).HasMaxLength(64);
        finding.Property(x => x.Severity).HasMaxLength(32);
        finding.Property(x => x.Code).HasMaxLength(128);
        finding.Property(x => x.Message).HasMaxLength(4096);
        finding.Property(x => x.Disposition).HasMaxLength(32);
        finding.Property(x => x.ReviewerUserId).HasMaxLength(128);
        finding.Property(x => x.ReviewerRationale).HasMaxLength(4096);
        finding.Property(x => x.Revision).IsConcurrencyToken();
        finding.HasIndex(x => new { x.OwnerUserId, x.TargetKind, x.TargetId });

        var audit = modelBuilder.Entity<GccV2ContextAuditEvent>();
        audit.ToTable("gcc_v2_context_audit_events");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.OwnerUserId).HasMaxLength(128);
        audit.Property(x => x.TargetKind).HasMaxLength(64);
        audit.Property(x => x.ActorUserId).HasMaxLength(128);
        audit.Property(x => x.Action).HasMaxLength(64);
        audit.Property(x => x.BeforeState).HasMaxLength(32);
        audit.Property(x => x.AfterState).HasMaxLength(32);
        audit.Property(x => x.DetailJson).HasColumnType("text");
        audit.Property(x => x.RequestId).HasMaxLength(256);
        audit.HasIndex(x => new { x.OwnerUserId, x.TargetKind, x.TargetId, x.CreatedAtUtc });
    }

    private static void ConfigureCatalog<T>(EntityTypeBuilder<T> entity, string table) where T : GccV2OwnedCatalog
    {
        entity.ToTable(table);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(128);
        entity.Property(x => x.Name).IsRequired().HasMaxLength(256);
        entity.Property(x => x.Description).HasMaxLength(2048);
        entity.Property(x => x.Revision).IsConcurrencyToken();
        entity.HasIndex(x => new { x.OwnerUserId, x.Name });
    }

    private static void ConfigureVersion<T>(EntityTypeBuilder<T> entity, string table) where T : GccV2GovernedVersion
    {
        entity.ToTable(table);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.CanonicalSha256).IsRequired().HasMaxLength(64);
        entity.Property(x => x.LifecycleState).IsRequired().HasMaxLength(32);
        entity.Property(x => x.CreatedBy).IsRequired().HasMaxLength(128);
        entity.Property(x => x.ReviewedBy).HasMaxLength(128);
        entity.HasIndex(x => x.CanonicalSha256);
    }
}
