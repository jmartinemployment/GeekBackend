using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

internal static class GccV2CustomerOutcomeConfiguration
{
    public static void ConfigureGccV2CustomerOutcomes(this ModelBuilder modelBuilder)
    {
        var outcome = modelBuilder.Entity<GccV2CustomerOutcome>();
        outcome.ToTable("gcc_v2_customer_outcomes");
        outcome.HasKey(x => x.Id);
        outcome.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(128);
        outcome.Property(x => x.Title).IsRequired().HasMaxLength(256);
        outcome.Property(x => x.MetricDefinition).IsRequired().HasMaxLength(2048);
        outcome.Property(x => x.Baseline).HasMaxLength(1024);
        outcome.Property(x => x.Denominator).HasMaxLength(1024);
        outcome.Property(x => x.ObservedValue).HasMaxLength(1024);
        outcome.Property(x => x.Source).IsRequired().HasMaxLength(1024);
        outcome.Property(x => x.EvidenceStatus).IsRequired().HasMaxLength(64);
        outcome.Property(x => x.AttributionMethod).HasMaxLength(512);
        outcome.Property(x => x.WorkflowVersionsJson).IsRequired().HasColumnType("text");
        outcome.Property(x => x.Notes).HasMaxLength(4000);
        outcome.HasIndex(x => x.OwnerUserId);
        outcome.HasIndex(x => new { x.OwnerUserId, x.PeriodEnd });
    }
}
