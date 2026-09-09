using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

internal static class GccV2GridConfiguration
{
    public static void ConfigureGccV2Grids(this ModelBuilder modelBuilder)
    {
        var grid = modelBuilder.Entity<GccV2Grid>();
        grid.ToTable("gcc_v2_grids");
        grid.HasKey(x => x.Id);
        grid.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(128);
        grid.Property(x => x.Name).IsRequired().HasMaxLength(512);
        grid.Property(x => x.Description).IsRequired().HasMaxLength(4096);
        grid.Property(x => x.Status).IsRequired().HasMaxLength(32);
        grid.Property(x => x.ConfigJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
        grid.Property(x => x.CreatedAtUtc).IsRequired();
        grid.Property(x => x.UpdatedAtUtc).IsRequired();
        grid.HasIndex(x => new { x.OwnerUserId, x.UpdatedAtUtc })
            .HasDatabaseName("ix_gcc_v2_grids_owner_updated");

        var row = modelBuilder.Entity<GccV2GridRow>();
        row.ToTable("gcc_v2_grid_rows");
        row.HasKey(x => x.Id);
        row.Property(x => x.RowIndex).IsRequired();
        row.Property(x => x.InputJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
        row.Property(x => x.OutputJson).HasColumnType("text");
        row.Property(x => x.Status).IsRequired().HasMaxLength(32);
        row.Property(x => x.Error).HasColumnType("text");
        row.Property(x => x.UpdatedAtUtc).IsRequired();
        row.HasIndex(x => new { x.GridId, x.RowIndex }).IsUnique()
            .HasDatabaseName("ux_gcc_v2_grid_rows_grid_row_index");
        row.HasOne(x => x.Grid).WithMany(x => x.Rows).HasForeignKey(x => x.GridId)
            .OnDelete(DeleteBehavior.Cascade);

        var run = modelBuilder.Entity<GccV2GridRun>();
        run.ToTable("gcc_v2_grid_runs");
        run.HasKey(x => x.Id);
        run.Property(x => x.Mode).IsRequired().HasMaxLength(32);
        run.Property(x => x.Status).IsRequired().HasMaxLength(32);
        run.Property(x => x.ActorUserId).IsRequired().HasMaxLength(128);
        run.Property(x => x.StartedAtUtc).IsRequired();
        run.Property(x => x.BudgetPreviewJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
        run.Property(x => x.HistoryJson).IsRequired().HasColumnType("text").HasDefaultValue("[]");
        run.Property(x => x.OutputCount).IsRequired().HasDefaultValue(0);
        run.HasIndex(x => new { x.GridId, x.StartedAtUtc })
            .HasDatabaseName("ix_gcc_v2_grid_runs_grid_started");
        run.HasOne(x => x.Grid).WithMany(x => x.Runs).HasForeignKey(x => x.GridId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
