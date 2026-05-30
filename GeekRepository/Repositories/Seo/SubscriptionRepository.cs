using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
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

    public async Task<Result<SeoSubscription>> UpsertAsync(
        Guid userId,
        UpsertSubscriptionRequest request,
        CancellationToken ct = default)
    {
        var existing = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            var created = new SeoSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Tier = request.Tier,
                Status = request.Status,
                PaypalSubscriptionId = request.PaypalSubscriptionId,
                CurrentPeriodEnd = request.CurrentPeriodEnd,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Subscriptions.Add(created);
            await db.SaveChangesAsync(ct);
            return Result<SeoSubscription>.Success(created);
        }

        existing.Tier = request.Tier;
        existing.Status = request.Status;
        existing.PaypalSubscriptionId = request.PaypalSubscriptionId;
        existing.CurrentPeriodEnd = request.CurrentPeriodEnd;
        existing.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Result<SeoSubscription>.Success(existing);
    }
}
