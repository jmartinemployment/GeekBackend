using GeekRepository.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

/// <summary>
/// Standalone context for WebPost content — entirely separate from AppDbContext/geek_blog,
/// maps only to public.web_posts. Same physical database, isolated schema/table.
/// </summary>
public class ContentWriterDbContext : DbContext
{
    public ContentWriterDbContext(DbContextOptions<ContentWriterDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<WebPost> WebPosts => Set<WebPost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebPost>(entity =>
        {
            entity.ToTable("web_posts", "public");

            entity.HasKey(w => w.Id);
            entity.Property(w => w.Slug).IsRequired().HasMaxLength(512);
            entity.Property(w => w.Title).IsRequired().HasMaxLength(512);
            entity.HasIndex(w => w.Slug).IsUnique();

            entity.OwnsOne(w => w.ContentStructure, structureBuilder =>
            {
                structureBuilder.ToJson("content_structure");

                structureBuilder.Property(s => s.MainBody).HasJsonPropertyName("mainBody");

                structureBuilder.OwnsMany(s => s.Sections, sectionBuilder =>
                {
                    sectionBuilder.Property(s => s.HeadingText).HasJsonPropertyName("headingText");
                    sectionBuilder.Property(s => s.BodyContent).HasJsonPropertyName("bodyContent");
                    sectionBuilder.Property(s => s.MediaUrl).HasJsonPropertyName("mediaUrl");
                    sectionBuilder.Property(s => s.MediaAlt).HasJsonPropertyName("mediaAlt");
                });
            });
        });
    }
}
