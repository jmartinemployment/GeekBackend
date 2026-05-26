using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IUsageMeteringRepository
{
    Task<Result<int>> GetCountAsync(Guid userId, DateOnly periodStart, string feature, CancellationToken ct = default);

    Task<Result<int>> IncrementAsync(
        Guid userId, DateOnly periodStart, string feature, int amount = 1, CancellationToken ct = default);
}
