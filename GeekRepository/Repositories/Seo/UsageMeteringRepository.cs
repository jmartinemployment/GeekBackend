using GeekSeo.Persistence.Entities;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Results;
using GeekSeo.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class UsageMeteringRepository(SeoDbContext db) : IUsageMeteringRepository
{
    public async Task<Result<int>> GetCountAsync(
        Guid userId, DateOnly periodStart, string feature, CancellationToken ct = default)
    {
        var row = await db.UsageCounters.AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.UserId == userId && c.PeriodStart == periodStart && c.Feature == feature,
                ct);
        return Result<int>.Success(row?.Count ?? 0);
    }

    public async Task<Result<int>> IncrementAsync(
        Guid userId, DateOnly periodStart, string feature, int amount, CancellationToken ct = default)
    {
        var row = await db.UsageCounters
            .FirstOrDefaultAsync(
                c => c.UserId == userId && c.PeriodStart == periodStart && c.Feature == feature,
                ct);

        if (row is null)
        {
            row = new SeoUsageCounter
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PeriodStart = periodStart,
                Feature = feature,
                Count = amount,
            };
            db.UsageCounters.Add(row);
        }
        else
        {
            row.Count += amount;
        }

        await db.SaveChangesAsync(ct);
        return Result<int>.Success(row.Count);
    }
}
