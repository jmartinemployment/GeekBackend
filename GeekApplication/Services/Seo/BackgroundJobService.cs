using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class BackgroundJobService(IBackgroundJobRepository jobs) : IBackgroundJobService
{
    public async Task<Result<BackgroundJobStatus>> GetJobAsync(Guid userId, Guid jobId, CancellationToken ct = default)
    {
        var jobResult = await jobs.GetByIdAsync(jobId, ct);
        if (!jobResult.IsSuccess || jobResult.Value is null)
            return Result<BackgroundJobStatus>.NotFound("Job not found");
        if (jobResult.Value.UserId != userId)
            return Result<BackgroundJobStatus>.Failure("Access denied");

        var job = jobResult.Value;
        return Result<BackgroundJobStatus>.Success(new BackgroundJobStatus
        {
            JobId = job.Id,
            JobType = job.JobType,
            Status = job.Status,
            ProgressPercent = job.ProgressPercent,
            ResultId = job.ResultId,
            ErrorMessage = job.ErrorMessage,
        });
    }
}
