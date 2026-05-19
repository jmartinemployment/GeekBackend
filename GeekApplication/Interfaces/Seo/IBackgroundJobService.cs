using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IBackgroundJobService
{
    Task<Result<BackgroundJobStatus>> GetJobAsync(Guid userId, Guid jobId, CancellationToken ct = default);
}
