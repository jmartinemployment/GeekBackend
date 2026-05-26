using GeekApplication.Constants.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface ISubscriptionService
{
    Task<Result<SubscriptionTier>> GetActiveTierAsync(Guid userId, CancellationToken ct = default);
}
