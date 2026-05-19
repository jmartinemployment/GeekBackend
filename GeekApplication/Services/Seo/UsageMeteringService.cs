using GeekApplication.Constants.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class UsageMeteringService(IUsageMeteringRepository repository) : IUsageMeteringService
{
    public Task<Result<int>> GetUsageAsync(Guid userId, string feature, CancellationToken ct = default) =>
        repository.GetCountAsync(userId, CurrentPeriodStart(), feature, ct);

    public Task<Result<int>> GetLimitAsync(SubscriptionTier tier, string feature, CancellationToken ct = default) =>
        Task.FromResult(Result<int>.Success(UsageLimits.GetLimit(tier, feature)));

    public async Task<Result> IncrementAsync(Guid userId, string feature, int amount = 1, CancellationToken ct = default)
    {
        var result = await repository.IncrementAsync(userId, CurrentPeriodStart(), feature, amount, ct);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error ?? "Increment failed");
    }

    public async Task<Result> EnsureWithinLimitAsync(
        Guid userId, SubscriptionTier tier, string feature, CancellationToken ct = default)
    {
        var limit = UsageLimits.GetLimit(tier, feature);
        if (limit == int.MaxValue)
            return Result.Success();

        var usageResult = await GetUsageAsync(userId, feature, ct);
        if (!usageResult.IsSuccess)
            return Result.Failure(usageResult.Error ?? "Usage lookup failed");

        if (usageResult.Value! >= limit)
        {
            return Result.Failure(
                $"Monthly limit reached for {feature} ({usageResult.Value}/{limit}). Upgrade your plan.");
        }

        return Result.Success();
    }

    private static DateOnly CurrentPeriodStart()
    {
        var utc = DateTimeOffset.UtcNow;
        return new DateOnly(utc.Year, utc.Month, 1);
    }
}
