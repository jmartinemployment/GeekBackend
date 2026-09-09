using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

internal static class GccV2CanvasProjectConfiguration
{
    public static void ConfigureGccV2CanvasProjects(this ModelBuilder modelBuilder)
    {
        var project = modelBuilder.Entity<GccV2CanvasProject>();
        project.ToTable("gcc_v2_canvas_projects");
        project.HasKey(x => x.Id);
        project.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(128);
        project.Property(x => x.Name).IsRequired().HasMaxLength(512);
        project.Property(x => x.Description).IsRequired().HasMaxLength(4096);
        project.Property(x => x.Status).IsRequired().HasMaxLength(32);
        project.Property(x => x.ActivityJson).IsRequired().HasColumnType("text").HasDefaultValue("[]");
        project.Property(x => x.CreatedAtUtc).IsRequired();
        project.Property(x => x.UpdatedAtUtc).IsRequired();
        project.HasIndex(x => new { x.OwnerUserId, x.UpdatedAtUtc })
            .HasDatabaseName("ix_gcc_v2_canvas_projects_owner_updated");

        var asset = modelBuilder.Entity<GccV2CanvasAsset>();
        asset.ToTable("gcc_v2_canvas_assets");
        asset.HasKey(x => x.Id);
        asset.Property(x => x.Title).IsRequired().HasMaxLength(512);
        asset.Property(x => x.Kind).IsRequired().HasMaxLength(32);
        asset.Property(x => x.ParentAssetIdsJson).IsRequired().HasColumnType("text").HasDefaultValue("[]");
        asset.Property(x => x.CreatedAtUtc).IsRequired();
        asset.Property(x => x.UpdatedAtUtc).IsRequired();
        asset.HasIndex(x => x.ProjectId).HasDatabaseName("ix_gcc_v2_canvas_assets_project_id");
        asset.HasOne(x => x.Project).WithMany(x => x.Assets).HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        var version = modelBuilder.Entity<GccV2CanvasAssetVersion>();
        version.ToTable("gcc_v2_canvas_asset_versions");
        version.HasKey(x => x.Id);
        version.Property(x => x.VersionNumber).IsRequired();
        version.Property(x => x.Status).IsRequired().HasMaxLength(32);
        version.Property(x => x.Summary).IsRequired().HasMaxLength(4096);
        version.Property(x => x.EvidenceJson).IsRequired().HasColumnType("text").HasDefaultValue("[]");
        version.Property(x => x.ProvenanceJson).IsRequired().HasColumnType("text").HasDefaultValue("{}");
        version.Property(x => x.CreatedBy).IsRequired().HasMaxLength(256);
        version.Property(x => x.CreatedAtUtc).IsRequired();
        version.HasIndex(x => new { x.AssetId, x.VersionNumber }).IsUnique()
            .HasDatabaseName("ux_gcc_v2_canvas_asset_versions_asset_version");
        version.HasOne(x => x.Asset).WithMany(x => x.Versions).HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
