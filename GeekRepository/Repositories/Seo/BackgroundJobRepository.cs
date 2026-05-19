using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class BackgroundJobRepository(SeoDbContext db) : IBackgroundJobRepository
{
    public async Task<Result<SeoBackgroundJob>> CreateAsync(CreateBackgroundJobRequest request, CancellationToken ct = default)
    {
        var job = new SeoBackgroundJob
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            JobType = request.JobType,
            PayloadJson = request.PayloadJson,
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return Result<SeoBackgroundJob>.Success(job);
    }

    public async Task<Result<SeoBackgroundJob>> GetByIdAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await db.BackgroundJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        return job is null
            ? Result<SeoBackgroundJob>.NotFound("Job not found")
            : Result<SeoBackgroundJob>.Success(job);
    }

    public async Task<Result> UpdateProgressAsync(Guid jobId, int progressPercent, CancellationToken ct = default)
    {
        var job = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null)
            return Result.Failure("Job not found");
        job.ProgressPercent = progressPercent;
        job.Status = "running";
        job.StartedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> MarkCompleteAsync(Guid jobId, Guid? resultId, CancellationToken ct = default)
    {
        var job = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null)
            return Result.Failure("Job not found");
        job.Status = "complete";
        job.ResultId = resultId;
        job.ProgressPercent = 100;
        job.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken ct = default)
    {
        var job = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null)
            return Result.Failure("Job not found");
        job.Status = "failed";
        job.ErrorMessage = errorMessage;
        job.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SeoBackgroundJob>>> GetPendingAsync(
        string jobType, int limit, CancellationToken ct = default)
    {
        var list = await db.BackgroundJobs
            .Where(j => j.JobType == jobType && j.Status == "pending")
            .OrderBy(j => j.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoBackgroundJob>>.Success(list);
    }
}
