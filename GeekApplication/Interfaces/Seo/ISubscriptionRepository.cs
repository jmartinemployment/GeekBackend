using GeekApplication.Entities.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface ISubscriptionRepository
{
    Task<Result<SeoSubscription?>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
}
