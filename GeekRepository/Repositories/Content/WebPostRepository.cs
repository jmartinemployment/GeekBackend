using GeekApplication.Interfaces;
using GeekApplication.Models.WebPost;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Content;

public sealed class WebPostRepository : IWebPostRepository
{
    private readonly ContentWriterDbContext _context;

    public WebPostRepository(ContentWriterDbContext context)
    {
        _context = context;
    }

    public async Task<WebPostFlatDto?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var entity = await _context.WebPosts.AsNoTracking().FirstOrDefaultAsync(w => w.Slug == slug, ct);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<WebPostFlatDto> UpsertAsync(UpsertWebPostCommand command, CancellationToken ct = default)
    {
        var entity = await _context.WebPosts.FirstOrDefaultAsync(w => w.Slug == command.Slug, ct);

        var sections = command.ContentStructure.Sections
            .Select(s => new Data.Entities.ContentSection
            {
                HeadingText = s.HeadingText,
                BodyContent = s.BodyContent,
                MediaUrl = s.MediaUrl,
                MediaAlt = s.MediaAlt,
            })
            .ToList();

        if (entity is null)
        {
            entity = new Data.Entities.WebPost
            {
                Slug = command.Slug,
                Title = command.Title,
                ContentStructure = new Data.Entities.ContentStructure
                {
                    Sections = sections,
                    MainBody = command.ContentStructure.MainBody,
                },
                CreatedAtUtc = DateTime.UtcNow,
            };
            _context.WebPosts.Add(entity);
        }
        else
        {
            entity.Title = command.Title;
            entity.ContentStructure = new Data.Entities.ContentStructure
            {
                Sections = sections,
                MainBody = command.ContentStructure.MainBody,
            };
            entity.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(string slug, CancellationToken ct = default)
    {
        var entity = await _context.WebPosts.FirstOrDefaultAsync(w => w.Slug == slug, ct);
        if (entity is null)
            return false;

        _context.WebPosts.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    private static WebPostFlatDto ToDto(Data.Entities.WebPost entity) => new(
        entity.Id,
        entity.Slug,
        entity.Title,
        new ContentStructureDto(
            entity.ContentStructure.Sections
                .Select(s => new ContentSectionDto(s.HeadingText, s.BodyContent, s.MediaUrl, s.MediaAlt))
                .ToList(),
            entity.ContentStructure.MainBody),
        new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero),
        entity.UpdatedAtUtc.HasValue ? new DateTimeOffset(entity.UpdatedAtUtc.Value, TimeSpan.Zero) : null);
}
