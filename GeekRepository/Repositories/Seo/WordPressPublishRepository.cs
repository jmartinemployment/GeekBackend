using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Results;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class WordPressPublishRepository(SeoDbContext db) : IWordPressPublishRepository
{
    public async Task<Result> RecordPublishAsync(
        Guid projectId,
        Guid documentId,
        string targetKeyword,
        int wordCount,
        string title,
        string publishedUrl,
        int wordPressPostId,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var existingPage = await db.PublishedPages
            .FirstOrDefaultAsync(p => p.DocumentId == documentId, ct);
        if (existingPage is null)
        {
            db.PublishedPages.Add(new SeoPublishedPage
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                DocumentId = documentId,
                Url = publishedUrl,
                WordPressPostId = wordPressPostId,
                TargetKeyword = targetKeyword,
            });
        }
        else
        {
            existingPage.Url = publishedUrl;
            existingPage.WordPressPostId = wordPressPostId;
        }

        var inventory = await db.SitePageInventory
            .FirstOrDefaultAsync(i => i.ProjectId == projectId && i.Url == publishedUrl, ct);
        if (inventory is null)
        {
            db.SitePageInventory.Add(new SeoSitePageInventory
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Url = publishedUrl,
                Title = title,
                WordCount = wordCount,
                CrawledAt = now,
            });
        }
        else
        {
            inventory.Title = title;
            inventory.WordCount = wordCount;
            inventory.CrawledAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
