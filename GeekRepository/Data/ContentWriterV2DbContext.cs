using GeekRepository.Data.Entities.ContentWriterV2;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

public class ContentWriterV2DbContext : DbContext
{
    public ContentWriterV2DbContext(DbContextOptions<ContentWriterV2DbContext> options) : base(options)
    {
    }

    public virtual DbSet<Blob> Blobs => Set<Blob>();

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
    }
}
