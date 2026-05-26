using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Results;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class SubscriptionRepository(SeoDbContext db) : ISubscriptionRepository
{
    public async Task<Result<SeoSubscription?>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var row = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);
        return Result<SeoSubscription?>.Success(row);
    }
}
