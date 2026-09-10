using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

internal static class GccV2PipelineConfiguration
{
    public static void ConfigureGccV2Pipelines(this ModelBuilder modelBuilder)
    {
        var definition = modelBuilder.Entity<GccV2PipelineDefinition>();
        definition.ToTable("gcc_v2_pipeline_definitions");
        definition.HasKey(x => x.Id);
        definition.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(128);
        definition.Property(x => x.Name).IsRequired().HasMaxLength(512);
        definition.Property(x => x.Description).IsRequired().HasMaxLength(4096);
        definition.Property(x => x.Status).IsRequired().HasMaxLength(32);
        definition.Property(x => x.VersionNumber).IsRequired();
        definition.Property(x => x.Digest).IsRequired().HasMaxLength(64);
        definition.Property(x => x.StagesJson).IsRequired().HasColumnType("text");
        definition.Property(x => x.PolicyJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
        definition.Property(x => x.CreatedAtUtc).IsRequired();
        definition.Property(x => x.UpdatedAtUtc).IsRequired();
        definition.HasIndex(x => new { x.OwnerUserId, x.UpdatedAtUtc })
            .HasDatabaseName("ix_gcc_v2_pipeline_definitions_owner_updated");

        var run = modelBuilder.Entity<GccV2PipelineRun>();
        run.ToTable("gcc_v2_pipeline_runs");
        run.HasKey(x => x.Id);
        run.Property(x => x.DefinitionVersionNumber).IsRequired();
        run.Property(x => x.DefinitionDigest).IsRequired().HasMaxLength(64);
        run.Property(x => x.Status).IsRequired().HasMaxLength(32);
        run.Property(x => x.ActorUserId).IsRequired().HasMaxLength(128);
        run.Property(x => x.InputJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
        run.Property(x => x.HistoryJson).IsRequired().HasColumnType("text").HasDefaultValue("[]");
        run.Property(x => x.StartedAtUtc).IsRequired();
        run.Property(x => x.Error).HasColumnType("text");
        run.HasIndex(x => new { x.PipelineDefinitionId, x.StartedAtUtc })
            .HasDatabaseName("ix_gcc_v2_pipeline_runs_definition_started");
        run.HasOne(x => x.PipelineDefinition).WithMany(x => x.Runs)
            .HasForeignKey(x => x.PipelineDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        var workItem = modelBuilder.Entity<GccV2PipelineWorkItem>();
        workItem.ToTable("gcc_v2_pipeline_work_items");
        workItem.HasKey(x => x.Id);
        workItem.Property(x => x.WorkItemIndex).IsRequired();
        workItem.Property(x => x.InputJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
        workItem.Property(x => x.Status).IsRequired().HasMaxLength(32);
        workItem.Property(x => x.Error).HasColumnType("text");
        workItem.Property(x => x.UpdatedAtUtc).IsRequired();
        workItem.HasIndex(x => new { x.PipelineRunId, x.WorkItemIndex }).IsUnique()
            .HasDatabaseName("ux_gcc_v2_pipeline_work_items_run_index");
        workItem.HasOne(x => x.PipelineRun).WithMany(x => x.WorkItems)
            .HasForeignKey(x => x.PipelineRunId)
            .OnDelete(DeleteBehavior.Cascade);

        var attempt = modelBuilder.Entity<GccV2PipelineStageAttempt>();
        attempt.ToTable("gcc_v2_pipeline_stage_attempts");
        attempt.HasKey(x => x.Id);
        attempt.Property(x => x.StageKey).IsRequired().HasMaxLength(128);
        attempt.Property(x => x.LifecycleStage).IsRequired().HasMaxLength(32);
        attempt.Property(x => x.Kind).IsRequired().HasMaxLength(32);
        attempt.Property(x => x.DisplayName).IsRequired().HasMaxLength(256);
        attempt.Property(x => x.CapabilityId).HasMaxLength(128);
        attempt.Property(x => x.Handoff).HasMaxLength(64);
        attempt.Property(x => x.AttemptNumber).IsRequired();
        attempt.Property(x => x.Status).IsRequired().HasMaxLength(32);
        attempt.Property(x => x.OutputJson).HasColumnType("text");
        attempt.Property(x => x.Error).HasColumnType("text");
        attempt.Property(x => x.StartedAtUtc).IsRequired();
        attempt.HasIndex(x => new { x.PipelineWorkItemId, x.StageKey, x.AttemptNumber }).IsUnique()
            .HasDatabaseName("ux_gcc_v2_pipeline_stage_attempts_item_stage_attempt");
        attempt.HasOne(x => x.WorkItem).WithMany(x => x.StageAttempts)
            .HasForeignKey(x => x.PipelineWorkItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
