using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class PlagiarismRepository(SeoDbContext db) : IPlagiarismRepository
{
    public async Task<Result<SeoPlagiarismCheck?>> GetLatestByDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var latest = await db.PlagiarismChecks.AsNoTracking()
            .Where(p => p.DocumentId == documentId)
            .OrderByDescending(p => p.CheckedAt)
            .FirstOrDefaultAsync(ct);
        return Result<SeoPlagiarismCheck?>.Success(latest);
    }

    public async Task<Result<SeoPlagiarismCheck>> CreateAsync(SeoPlagiarismCheck check, CancellationToken ct = default)
    {
        db.PlagiarismChecks.Add(check);
        await db.SaveChangesAsync(ct);
        return Result<SeoPlagiarismCheck>.Success(check);
    }
}
