using GeekApplication.Constants.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class SubscriptionService(ISubscriptionRepository subscriptions) : ISubscriptionService
{
    public async Task<Result<SubscriptionTier>> GetActiveTierAsync(Guid userId, CancellationToken ct = default)
    {
        var row = await subscriptions.GetByUserIdAsync(userId, ct);
        if (!row.IsSuccess)
            return Result<SubscriptionTier>.Failure(row.Error ?? "Subscription lookup failed");

        if (row.Value is null)
            return Result<SubscriptionTier>.Success(SubscriptionTier.Starter);

        if (!string.Equals(row.Value.Status, "active", StringComparison.OrdinalIgnoreCase))
            return Result<SubscriptionTier>.Success(SubscriptionTier.None);

        return Result<SubscriptionTier>.Success(ParseTier(row.Value.Tier));
    }

    private static SubscriptionTier ParseTier(string tier) => tier.ToLowerInvariant() switch
    {
        "starter" => SubscriptionTier.Starter,
        "professional" => SubscriptionTier.Professional,
        "team" => SubscriptionTier.Team,
        "agency" => SubscriptionTier.Agency,
        _ => SubscriptionTier.None,
    };
}
