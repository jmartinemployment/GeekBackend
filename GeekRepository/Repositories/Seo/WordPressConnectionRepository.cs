using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Results;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class WordPressConnectionRepository(SeoDbContext db) : IWordPressConnectionRepository
{
    public async Task<Result<SeoWordPressConnection?>> GetByProjectAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var row = await db.WordPressConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);
        return Result<SeoWordPressConnection?>.Success(row);
    }

    public async Task<Result<SeoWordPressConnection>> UpsertAsync(
        SeoWordPressConnection connection, CancellationToken ct = default)
    {
        var existing = await db.WordPressConnections
            .FirstOrDefaultAsync(c => c.ProjectId == connection.ProjectId, ct);
        if (existing is null)
        {
            db.WordPressConnections.Add(connection);
        }
        else
        {
            existing.SiteUrl = connection.SiteUrl;
            existing.Username = connection.Username;
            existing.EncryptedAppPassword = connection.EncryptedAppPassword;
            existing.EncryptionIv = connection.EncryptionIv;
            existing.EncryptionTag = connection.EncryptionTag;
            existing.DefaultPostStatus = connection.DefaultPostStatus;
            existing.ConnectedAt = connection.ConnectedAt;
            connection = existing;
        }

        await db.SaveChangesAsync(ct);
        return Result<SeoWordPressConnection>.Success(connection);
    }

    public async Task<Result> DeleteByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var row = await db.WordPressConnections.FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);
        if (row is not null)
        {
            db.WordPressConnections.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        return Result.Success();
    }
}
