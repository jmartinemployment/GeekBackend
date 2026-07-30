using GeekRepository.Data.Entities.ContentWriterV2;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

public class ContentWriterV2DbContext : DbContext
{
    public ContentWriterV2DbContext(DbContextOptions<ContentWriterV2DbContext> options) : base(options)
    {
    }

    public virtual DbSet<Blob> Blobs => Set<Blob>();
    public virtual DbSet<ToolContentCache> ToolContentCaches => Set<ToolContentCache>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("content_writer_v2");

        modelBuilder.Entity<Blob>(entity =>
        {
            entity.ToTable("blobs");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Collection).IsRequired().HasMaxLength(128).HasColumnName("collection");
            entity.Property(b => b.ItemId).IsRequired().HasColumnName("item_id");
            entity.Property(b => b.DataJson).IsRequired().HasColumnName("data").HasColumnType("jsonb");
            entity.Property(b => b.UpdatedAtUtc).IsRequired().HasColumnName("updated_at");
            entity.HasIndex(b => new { b.Collection, b.ItemId }).IsUnique().HasDatabaseName("ix_blobs_collection_item_id");
        });

        modelBuilder.Entity<ToolContentCache>(entity =>
        {
            entity.ToTable("tool_content_cache");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.NormalizedToolName).IsRequired().HasMaxLength(256).HasColumnName("normalized_tool_name");
            entity.Property(t => t.DisplayName).IsRequired().HasMaxLength(256).HasColumnName("display_name");
            entity.Property(t => t.OverviewJson).IsRequired().HasColumnName("overview_json").HasColumnType("jsonb");
            entity.Property(t => t.UpdatedAtUtc).IsRequired().HasColumnName("updated_at");
            entity.HasIndex(t => t.NormalizedToolName).IsUnique().HasDatabaseName("ix_tool_content_cache_normalized_tool_name");
        });
    }
}
