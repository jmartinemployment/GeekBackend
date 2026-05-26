using GeekApplication.Entities.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IBackgroundJobRepository
{
    Task<Result<SeoBackgroundJob>> CreateAsync(CreateBackgroundJobRequest request, CancellationToken ct = default);
    Task<Result<SeoBackgroundJob>> GetByIdAsync(Guid jobId, CancellationToken ct = default);
    Task<Result> UpdateProgressAsync(Guid jobId, int progressPercent, CancellationToken ct = default);
    Task<Result> MarkCompleteAsync(Guid jobId, Guid? resultId, CancellationToken ct = default);
    Task<Result> MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SeoBackgroundJob>>> GetPendingAsync(string jobType, int limit, CancellationToken ct = default);
}
