using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeekRepository.Repositories.ContentCreator;

/// <summary>
/// Generate runs on a project. The one-running-per-project rule is the database's partial unique index;
/// the check before the insert only makes the refusal name the running job instead of a constraint.
/// </summary>
public class GccGenerateJobRepository : IGccGenerateJobRepository
{
    private readonly ContentCreatorDbContext _db;

    public GccGenerateJobRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<GccGenerateJobDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var job = await _db.GccGenerateJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        return job is null ? null : await MapAsync(job, ct);
    }

    /// <remarks>
    /// Newest by when it started. A project has at most one running run (the partial unique index),
    /// and a run cannot start while another is going, so the newest is the running one whenever one is.
    /// </remarks>
    public async Task<GccGenerateJobDto?> GetLatestForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var job = await _db.GccGenerateJobs.AsNoTracking()
            .Where(j => j.ProjectId == projectId)
            .OrderByDescending(j => j.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        return job is null ? null : await MapAsync(job, ct);
    }

    public async Task<IReadOnlyList<GccGenerateJobEventDto>?> AppendEventsAsync(
        Guid jobId, IReadOnlyList<GccGenerateJobEventWrite> events, CancellationToken ct = default)
    {
        if (!await _db.GccGenerateJobs.AnyAsync(j => j.Id == jobId, ct)) return null;
        if (events.Count == 0) return [];

        // The next place in the run. One writer per run -- GeekAPI records its own run's events in
        // order -- so a read-then-insert is the place; the unique index on (job, seq) is the backstop.
        var last = await _db.GccGenerateJobEvents
            .Where(e => e.JobId == jobId)
            .MaxAsync(e => (int?)e.Seq, ct) ?? 0;

        var now = DateTime.UtcNow;
        var rows = new List<GccGenerateJobEvent>(events.Count);
        foreach (var write in events)
        {
            rows.Add(new GccGenerateJobEvent
            {
                JobId = jobId,
                Seq = ++last,
                AtUtc = now,
                Kind = write.Kind.Trim(),
                Piece = string.IsNullOrWhiteSpace(write.Piece) ? null : write.Piece.Trim(),
                PayloadJson = string.IsNullOrWhiteSpace(write.PayloadJson) ? "{}" : write.PayloadJson,
            });
        }

        _db.GccGenerateJobEvents.AddRange(rows);
        await _db.SaveChangesAsync(ct);
        return [.. rows.Select(ToEventDto)];
    }

    public async Task<IReadOnlyList<GccGenerateJobEventDto>?> ListEventsAsync(Guid jobId, CancellationToken ct = default)
    {
        if (!await _db.GccGenerateJobs.AnyAsync(j => j.Id == jobId, ct)) return null;

        var rows = await _db.GccGenerateJobEvents.AsNoTracking()
            .Where(e => e.JobId == jobId)
            .OrderBy(e => e.Seq)
            .ToListAsync(ct);
        return [.. rows.Select(ToEventDto)];
    }

    private static GccGenerateJobEventDto ToEventDto(GccGenerateJobEvent e) =>
        new(e.Id, e.JobId, e.Seq, e.AtUtc, e.Kind, e.Piece, e.PayloadJson);

    public async Task<GccCreateDto?> GetBackingCreateAsync(Guid projectId, CancellationToken ct = default)
    {
        var create = await NewestCreateAsync(projectId, ct);
        return create is null ? null : GccCreateRepository.MapToDto(create);
    }

    /// <remarks>
    /// The newest revision is the brief the run reads, because the project's brief_json is written in
    /// the same SaveChanges as its newest revision, and <c>ExpectedBriefVersion</c> proves no Save has
    /// landed since GeekAPI read and validated that brief.
    ///
    /// A minted create carries the project's keyword and site run so the columns that require a value
    /// hold a true one; it carries no brief -- the project's brief is the only one a run reads.
    /// </remarks>
    public async Task<GccGenerateJobStartResult> StartAsync(
        StartGccGenerateJobCommand command,
        CancellationToken ct = default)
    {
        var project = await _db.GccProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.ProjectId && p.DeletedAtUtc == null, ct);
        if (project is null) return GccGenerateJobStartResult.NotFound();
        if (project.BriefVersion != command.ExpectedBriefVersion) return GccGenerateJobStartResult.Stale();

        var revision = await _db.GccProjectRevisions.AsNoTracking()
            .Where(r => r.ProjectId == project.Id)
            .OrderByDescending(r => r.SavedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (revision is null) return GccGenerateJobStartResult.NoBrief();

        if (await RunningAsync(project.Id, ct) is { } running)
            return GccGenerateJobStartResult.Running(await MapAsync(running, ct));

        Guid createId;
        if (command.CreateId is Guid given)
        {
            var onProject = await _db.GccCreates.AnyAsync(c => c.Id == given && c.ProjectId == project.Id, ct);
            if (!onProject) return GccGenerateJobStartResult.ForeignCreate();
            createId = given;
        }
        else
        {
            var minted = new GccCreate
            {
                ClientId = project.ClientId,
                ProjectId = project.Id,
                OwnerUserId = command.OwnerUserId,
                StartingContentType = command.RequestedTypes[0],
                Topic = project.Topic ?? project.Name,
                Department = string.IsNullOrWhiteSpace(project.Department) ? "marketing" : project.Department,
                ProjectSiteRunId = project.ProjectSiteRunId,
                Status = "draft",
            };
            _db.GccCreates.Add(minted);
            createId = minted.Id;
        }

        var job = new GccGenerateJob
        {
            Id = command.Id,
            ProjectId = project.Id,
            CreateId = createId,
            BriefRevisionId = revision.Id,
            OwnerUserId = command.OwnerUserId.ToString(),
            RequestedTypes = command.RequestedTypes.ToList(),
            Provider = command.Provider,
            Status = GccGenerateJobStatuses.Running,
            StartedAtUtc = DateTime.UtcNow,
        };
        _db.GccGenerateJobs.Add(job);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another start won the race between the check above and this insert. Nothing of this one
            // was written; name the one that won.
            _db.ChangeTracker.Clear();
            var winner = await RunningAsync(project.Id, ct);
            return winner is null
                ? GccGenerateJobStartResult.NotFound()
                : GccGenerateJobStartResult.Running(await MapAsync(winner, ct));
        }

        await transaction.CommitAsync(ct);
        return GccGenerateJobStartResult.Started(Map(job, revision.SavedAtUtc));
    }

    public Task<GccGenerateJobDto?> CompleteAsync(Guid id, string resultJson, CancellationToken ct = default) =>
        FinishAsync(id, j =>
        {
            j.Status = GccGenerateJobStatuses.Ready;
            j.ResultJson = resultJson;
        }, ct);

    public Task<GccGenerateJobDto?> FailAsync(Guid id, string error, CancellationToken ct = default) =>
        FinishAsync(id, j =>
        {
            j.Status = GccGenerateJobStatuses.Failed;
            j.Error = error;
        }, ct);

    public async Task<int> FailInterruptedAsync(string error, CancellationToken ct = default)
    {
        var running = await _db.GccGenerateJobs
            .Where(j => j.Status == GccGenerateJobStatuses.Running)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var job in running)
        {
            job.Status = GccGenerateJobStatuses.Failed;
            job.Error = error;
            job.FinishedAtUtc = now;
        }
        await _db.SaveChangesAsync(ct);
        return running.Count;
    }

    private async Task<GccGenerateJobDto?> FinishAsync(Guid id, Action<GccGenerateJob> finish, CancellationToken ct)
    {
        var job = await _db.GccGenerateJobs
            .FirstOrDefaultAsync(j => j.Id == id && j.Status == GccGenerateJobStatuses.Running, ct);
        if (job is null) return null;

        finish(job);
        job.FinishedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await MapAsync(job, ct);
    }

    private Task<GccGenerateJob?> RunningAsync(Guid projectId, CancellationToken ct) =>
        _db.GccGenerateJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.ProjectId == projectId && j.Status == GccGenerateJobStatuses.Running, ct);

    private Task<GccCreate?> NewestCreateAsync(Guid projectId, CancellationToken ct) =>
        _db.GccCreates.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    private async Task<GccGenerateJobDto> MapAsync(GccGenerateJob job, CancellationToken ct)
    {
        var savedAt = await _db.GccProjectRevisions.AsNoTracking()
            .Where(r => r.Id == job.BriefRevisionId)
            .Select(r => r.SavedAtUtc)
            .FirstAsync(ct);
        return Map(job, savedAt);
    }

    private static GccGenerateJobDto Map(GccGenerateJob job, DateTime briefRevisionSavedAtUtc) =>
        new(
            job.Id,
            job.ProjectId,
            job.CreateId,
            job.BriefRevisionId,
            briefRevisionSavedAtUtc,
            job.OwnerUserId,
            job.RequestedTypes.AsReadOnly(),
            job.Provider,
            job.Status,
            job.ResultJson,
            job.Error,
            job.StartedAtUtc,
            job.FinishedAtUtc);
}
