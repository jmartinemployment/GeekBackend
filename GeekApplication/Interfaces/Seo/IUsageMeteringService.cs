using GeekApplication.Constants.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IUsageMeteringService
{
    Task<Result<int>> GetUsageAsync(Guid userId, string feature, CancellationToken ct = default);

    Task<Result<int>> GetLimitAsync(SubscriptionTier tier, string feature, CancellationToken ct = default);

    Task<Result> IncrementAsync(Guid userId, string feature, int amount = 1, CancellationToken ct = default);

    Task<Result> EnsureWithinLimitAsync(
        Guid userId, SubscriptionTier tier, string feature, CancellationToken ct = default);
}
