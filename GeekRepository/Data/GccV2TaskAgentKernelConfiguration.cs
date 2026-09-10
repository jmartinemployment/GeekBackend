using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

internal static class GccV2TaskAgentKernelConfiguration
{
    public static void ConfigureGccV2TaskAgentKernel(this ModelBuilder modelBuilder)
    {
        var definition = modelBuilder.Entity<GccV2TaskAgentDefinition>();
        definition.ToTable("gcc_v2_task_agent_definitions");
        definition.HasKey(x => x.Id);
        definition.Property(x => x.CapabilityId).IsRequired().HasMaxLength(128);
        definition.Property(x => x.DisplayName).IsRequired().HasMaxLength(256);
        definition.Property(x => x.Description).IsRequired().HasMaxLength(2048);
        definition.Property(x => x.LifecycleState).IsRequired().HasMaxLength(32);
        definition.HasIndex(x => x.CapabilityId).IsUnique();

        var version = modelBuilder.Entity<GccV2TaskAgentVersion>();
        version.ToTable("gcc_v2_task_agent_versions");
        version.HasKey(x => x.Id);
        version.Property(x => x.SemanticVersion).IsRequired().HasMaxLength(64);
        version.Property(x => x.WorkflowGroup).IsRequired().HasMaxLength(128);
        foreach (var property in new[]
        {
            nameof(GccV2TaskAgentVersion.FacetsJson),
            nameof(GccV2TaskAgentVersion.InputSchemaJson),
            nameof(GccV2TaskAgentVersion.OutputSchemaJson),
            nameof(GccV2TaskAgentVersion.WorkflowJson),
            nameof(GccV2TaskAgentVersion.ContextPolicyJson),
            nameof(GccV2TaskAgentVersion.ResultRendererJson),
            nameof(GccV2TaskAgentVersion.CompatibleArtifactTypesJson),
            nameof(GccV2TaskAgentVersion.AllowedToolsJson),
            nameof(GccV2TaskAgentVersion.AllowedModelsJson),
            nameof(GccV2TaskAgentVersion.SkillVersionIdsJson),
            nameof(GccV2TaskAgentVersion.EvaluationThresholdsJson),
        })
            version.Property<string>(property).IsRequired().HasColumnType("text");
        foreach (var property in new[]
        {
            nameof(GccV2TaskAgentVersion.InputSchemaDigest),
            nameof(GccV2TaskAgentVersion.OutputSchemaDigest),
            nameof(GccV2TaskAgentVersion.WorkflowDigest),
            nameof(GccV2TaskAgentVersion.ContextPolicyDigest),
            nameof(GccV2TaskAgentVersion.ResultRendererDigest),
            nameof(GccV2TaskAgentVersion.EvaluationThresholdsDigest),
            nameof(GccV2TaskAgentVersion.VersionDigest),
        })
            version.Property<string>(property).IsRequired().HasMaxLength(64);
        version.Property(x => x.State).IsRequired().HasMaxLength(32);
        version.Property(x => x.CreatedBy).IsRequired().HasMaxLength(128);
        version.Property(x => x.ReviewedBy).HasMaxLength(128);
        version.HasIndex(x => new { x.DefinitionId, x.SemanticVersion }).IsUnique();
        version.HasIndex(x => x.VersionDigest).IsUnique();
        version.HasOne(x => x.Definition).WithMany(x => x.Versions).HasForeignKey(x => x.DefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        var run = modelBuilder.Entity<GccV2TaskRun>();
        run.ToTable("gcc_v2_task_runs");
        run.HasKey(x => x.Id);
        run.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(128);
        run.Property(x => x.TaskAgentVersionDigest).IsRequired().HasMaxLength(64);
        run.Property(x => x.InputJson).IsRequired().HasColumnType("text");
        run.Property(x => x.InputDigest).IsRequired().HasMaxLength(64);
        run.Property(x => x.ContextManifestDigest).HasMaxLength(64);
        foreach (var property in new[]
        {
            nameof(GccV2TaskRun.ModelSnapshotJson),
            nameof(GccV2TaskRun.BudgetSnapshotJson),
            nameof(GccV2TaskRun.SourceSnapshotJson),
        })
            run.Property<string>(property).IsRequired().HasColumnType("text");
        foreach (var property in new[]
        {
            nameof(GccV2TaskRun.ModelSnapshotDigest),
            nameof(GccV2TaskRun.BudgetSnapshotDigest),
            nameof(GccV2TaskRun.SourceSnapshotDigest),
        })
            run.Property<string>(property).IsRequired().HasMaxLength(64);
        run.Property(x => x.Status).IsRequired().HasMaxLength(32);
        run.Property(x => x.Phase).IsRequired().HasMaxLength(128);
        run.Property(x => x.CreatedByActor).IsRequired().HasMaxLength(128);
        run.Property(x => x.LastActor).HasMaxLength(128);
        run.Property(x => x.ClaimedByInstanceId).HasMaxLength(128);
        run.Property(x => x.TerminalError).HasMaxLength(4096);
        run.HasIndex(x => new { x.OwnerUserId, x.CreatedAtUtc });
        run.HasIndex(x => new { x.Status, x.LeaseUntilUtc });
        run.HasIndex(x => x.RootRunId);
        run.HasOne(x => x.Definition).WithMany().HasForeignKey(x => x.TaskAgentDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        run.HasOne(x => x.Version).WithMany().HasForeignKey(x => x.TaskAgentVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        run.HasOne<GccV2TaskRun>().WithMany().HasForeignKey(x => x.RetryOfRunId)
            .OnDelete(DeleteBehavior.Restrict);
        run.HasOne<GccV2RunContextManifest>().WithMany().HasForeignKey(x => x.ContextManifestId)
            .OnDelete(DeleteBehavior.Restrict);

        var runEvent = modelBuilder.Entity<GccV2TaskRunEvent>();
        runEvent.ToTable("gcc_v2_task_run_events");
        runEvent.HasKey(x => x.Id);
        runEvent.Property(x => x.Type).IsRequired().HasMaxLength(128);
        runEvent.Property(x => x.PayloadJson).IsRequired().HasColumnType("text");
        runEvent.Property(x => x.Actor).IsRequired().HasMaxLength(128);
        runEvent.HasIndex(x => new { x.RunId, x.Seq }).IsUnique();
        runEvent.HasOne(x => x.Run).WithMany(x => x.Events).HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Restrict);

        var artifact = modelBuilder.Entity<GccV2TaskArtifact>();
        artifact.ToTable("gcc_v2_task_artifacts");
        artifact.HasKey(x => x.Id);
        artifact.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(128);
        artifact.Property(x => x.ArtifactType).IsRequired().HasMaxLength(128);
        artifact.HasIndex(x => new { x.OwnerUserId, x.RunId, x.ArtifactType });
        artifact.HasOne(x => x.Run).WithMany(x => x.Artifacts).HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Restrict);

        var artifactVersion = modelBuilder.Entity<GccV2TaskArtifactVersion>();
        artifactVersion.ToTable("gcc_v2_task_artifact_versions");
        artifactVersion.HasKey(x => x.Id);
        artifactVersion.Property(x => x.PayloadJson).IsRequired().HasColumnType("text");
        artifactVersion.Property(x => x.EvidenceJson).IsRequired().HasColumnType("text");
        artifactVersion.Property(x => x.CitationsJson).IsRequired().HasColumnType("text");
        artifactVersion.Property(x => x.ValidationState).IsRequired().HasMaxLength(32);
        artifactVersion.Property(x => x.ValidationJson).IsRequired().HasColumnType("text");
        artifactVersion.Property(x => x.Digest).IsRequired().HasMaxLength(64);
        artifactVersion.Property(x => x.CreatedByActor).IsRequired().HasMaxLength(128);
        artifactVersion.HasIndex(x => new { x.ArtifactId, x.VersionNumber }).IsUnique();
        artifactVersion.HasIndex(x => x.Digest);
        artifactVersion.HasOne(x => x.Artifact).WithMany(x => x.Versions).HasForeignKey(x => x.ArtifactId)
            .OnDelete(DeleteBehavior.Restrict);

        var lineage = modelBuilder.Entity<GccV2TaskArtifactLineage>();
        lineage.ToTable("gcc_v2_task_artifact_lineage");
        lineage.HasKey(x => new { x.ParentArtifactVersionId, x.ChildArtifactVersionId });
        lineage.Property(x => x.Relationship).IsRequired().HasMaxLength(64);
        lineage.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentArtifactVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        lineage.HasOne(x => x.Child).WithMany(x => x.Parents).HasForeignKey(x => x.ChildArtifactVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        var library = modelBuilder.Entity<GccV2TaskAgentLibraryPreference>();
        library.ToTable("gcc_v2_task_agent_library_preferences");
        library.HasKey(x => x.Id);
        library.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(128);
        library.Property(x => x.FavoritesJson).IsRequired().HasColumnType("text");
        library.Property(x => x.SavedConfigsJson).IsRequired().HasColumnType("text");
        library.HasIndex(x => x.OwnerUserId).IsUnique();
    }
}
