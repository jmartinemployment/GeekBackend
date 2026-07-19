using GeekRepository.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

/// <summary>
/// Standalone EF Core context for WebPost content — entirely separate from AppDbContext/geek_blog.
/// Maps directly to public.web_posts. Strictly content-focused: no styling tokens, layout enums,
/// type flags, or CSS helper variables anywhere in this schema.
/// </summary>
public class ContentWriterDbContext : DbContext
{
    public ContentWriterDbContext(DbContextOptions<ContentWriterDbContext> options) : base(options)
    {
    }

    public DbSet<WebPost> WebPosts => Set<WebPost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebPost>(entity =>
        {
            entity.ToTable("web_posts", schema: "public");
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Slug).HasMaxLength(512).IsRequired();
            entity.HasIndex(w => w.Slug).IsUnique();
            entity.Property(w => w.Title).HasMaxLength(512).IsRequired();

            entity.OwnsOne(w => w.ContentStructure, cs =>
            {
                cs.ToJson("content_structure");
                cs.OwnsMany(c => c.Sections);
            });
        });
    }
}
