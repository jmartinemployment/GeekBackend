using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentWriterV3;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentWriterV3;

public class JobRepository : IJobRepository
{
    private readonly ContentWriterV3DbContext _db;

    public JobRepository(ContentWriterV3DbContext db)
    {
        _db = db;
    }

    public async Task<JobDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<JobDto>> GetByStatusAsync(string status, int limit = 100, CancellationToken ct = default)
    {
        var entities = await _db.Jobs
            .Where(j => j.Status == status)
            .OrderBy(j => j.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<JobDto?> GetForProcessingAsync(string jobType, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Jobs
            .Where(j => j.JobType == jobType &&
                        j.Status == "queued" &&
                        (j.LeaseExpiresAtUtc == null || j.LeaseExpiresAtUtc < now))
            .OrderBy(j => j.CreatedAtUtc)
            .Select(j => new JobDto(
                j.Id, j.JobType, j.Status, j.PayloadJson, j.IdempotencyKey,
                j.AttemptCount, j.LeaseOwner, j.LeaseExpiresAtUtc,
                j.ErrorCode, j.ErrorMessage, j.CreatedAtUtc, j.StartedAtUtc,
                j.CompletedAtUtc, j.RequeuedByUserId, j.RequeuedAtUtc))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<JobDto> CreateAsync(CreateJobCommand command, CancellationToken ct = default)
    {
        var entity = new Job
        {
            JobType = command.JobType,
            PayloadJson = command.PayloadJson,
            IdempotencyKey = command.IdempotencyKey,
            Status = "queued",
            AttemptCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Jobs.Add(entity);
        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    public async Task<JobDto> UpdateStatusAsync(UpdateJobStatusCommand command, CancellationToken ct = default)
    {
        var entity = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"Job {command.Id} not found");

        entity.Status = command.Status;
        entity.ErrorCode = command.ErrorCode;
        entity.ErrorMessage = command.ErrorMessage;
        entity.CompletedAtUtc = command.CompletedAtUtc;

        if (command.Status == "running" && entity.StartedAtUtc == null)
            entity.StartedAtUtc = DateTime.UtcNow;

        _db.Jobs.Update(entity);
        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    public async Task<JobDto> LeaseAsync(Guid id, string leaseOwner, TimeSpan duration, CancellationToken ct = default)
    {
        var entity = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct)
            ?? throw new KeyNotFoundException($"Job {id} not found");

        entity.LeaseOwner = leaseOwner;
        entity.LeaseExpiresAtUtc = DateTime.UtcNow.Add(duration);
        entity.Status = "running";
        entity.AttemptCount += 1;

        if (entity.StartedAtUtc == null)
            entity.StartedAtUtc = DateTime.UtcNow;

        _db.Jobs.Update(entity);
        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    public async Task<JobDto> ReleaseLeaseAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct)
            ?? throw new KeyNotFoundException($"Job {id} not found");

        entity.LeaseOwner = null;
        entity.LeaseExpiresAtUtc = null;
        entity.Status = "queued";

        _db.Jobs.Update(entity);
        await _db.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    private static JobDto MapToDto(Job entity) =>
        new(
            entity.Id, entity.JobType, entity.Status, entity.PayloadJson,
            entity.IdempotencyKey, entity.AttemptCount, entity.LeaseOwner,
            entity.LeaseExpiresAtUtc, entity.ErrorCode, entity.ErrorMessage,
            entity.CreatedAtUtc, entity.StartedAtUtc, entity.CompletedAtUtc,
            entity.RequeuedByUserId, entity.RequeuedAtUtc);
}
