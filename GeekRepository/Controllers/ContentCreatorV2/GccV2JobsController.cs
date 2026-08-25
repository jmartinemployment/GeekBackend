using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>
/// Content Creator v2 jobs + append-only event log + stage results. No pending-poll endpoint —
/// callers persist then <c>NOTIFY gcc_v2_job</c> so GeekAPI's worker wakes instead of ticking.
/// </summary>
[ApiController]
[Route("repo/content-creator-v2/jobs")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2JobsController : ControllerBase
{
    private const string NotifyChannel = "gcc_v2_job";

    private readonly ContentCreatorV2DbContext _db;
    private readonly ILogger<GccV2JobsController> _logger;

    public GccV2JobsController(ContentCreatorV2DbContext db, ILogger<GccV2JobsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2Job>> GetById(Guid id, CancellationToken ct)
    {
        var job = await _db.GccV2Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        return job is null ? NotFound() : Ok(job);
    }

    /// <summary>Latest job for a create (by <see cref="GccV2Job.CreatedAtUtc"/>) — Canvas API routes
    /// are keyed by create id (matching <c>creates/{id}/generate</c>), so this resolves "the job"
    /// for a create without the caller needing to already know its id.</summary>
    [HttpGet("by-create/{createId:guid}")]
    public async Task<ActionResult<GccV2Job>> GetLatestByCreate(Guid createId, CancellationToken ct)
    {
        var job = await _db.GccV2Jobs.AsNoTracking()
            .Where(j => j.CreateId == createId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpGet("by-status/{status}")]
    public async Task<ActionResult<IReadOnlyList<GccV2Job>>> GetByStatus(
        string status,
        [FromQuery] DateTimeOffset? leaseBefore,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var query = _db.GccV2Jobs.AsNoTracking().Where(j => j.Status == status);
        if (leaseBefore is not null)
            query = query.Where(j => j.LeaseUntilUtc != null && j.LeaseUntilUtc < leaseBefore);

        var jobs = await query.OrderBy(j => j.CreatedAtUtc).Take(limit).ToListAsync(ct);
        return Ok(jobs);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2Job>> Create([FromBody] CreateGccV2JobCommand command, CancellationToken ct)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.OwnerUserId))
            return BadRequest("ownerUserId is required");

        var job = new GccV2Job
        {
            ContentType = string.IsNullOrWhiteSpace(command.ContentType) ? "blog" : command.ContentType,
            BriefId = command.BriefId ?? Guid.Empty,
            OwnerUserId = command.OwnerUserId,
            CreateId = command.CreateId,
            SiteAnalysisProfileId = command.SiteAnalysisProfileId,
            Stage = "plan",
            Status = "pending",
        };

        _db.GccV2Jobs.Add(job);
        await _db.SaveChangesAsync(ct);
        await NotifyAsync(job.Id, ct);

        return CreatedAtAction(nameof(GetById), new { id = job.Id }, job);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GccV2Job>> Patch(Guid id, [FromBody] PatchGccV2JobCommand command, CancellationToken ct)
    {
        var job = await _db.GccV2Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return NotFound();

        if (command.Stage is not null) job.Stage = command.Stage;
        if (command.Status is not null) job.Status = command.Status;
        if (command.ResultJson is not null) job.ResultJson = command.ResultJson;
        if (command.Error is not null) job.Error = command.Error;
        if (command.TokensUsed is not null) job.TokensUsed = (job.TokensUsed ?? 0) + command.TokensUsed;
        if (command.AttemptCountIncrement is true) job.AttemptCount += 1;
        if (command.CompletedAtUtc is not null) job.CompletedAtUtc = command.CompletedAtUtc;

        if (command.ReleaseClaim is true)
        {
            job.ClaimedByInstanceId = null;
            job.ClaimedAtUtc = null;
            job.LeaseUntilUtc = null;
        }
        else if (command.LeaseUntilUtc is not null)
        {
            job.LeaseUntilUtc = command.LeaseUntilUtc;
        }

        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (command.Wake is true)
            await NotifyAsync(id, ct);

        return Ok(job);
    }

    /// <summary>
    /// Claims the job if it is <c>pending</c> or its lease has expired while <c>running</c>.
    /// Single conditional UPDATE — atomic under Postgres row locking, equivalent in effect to
    /// <c>FOR UPDATE SKIP LOCKED</c> for a single-row claim without a separate SELECT round trip.
    /// </summary>
    [HttpPost("{id:guid}/claim")]
    public async Task<ActionResult<GccV2Job>> Claim(
        Guid id,
        [FromQuery] string instanceId,
        [FromQuery] int leaseSeconds = 120,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return BadRequest("instanceId is required");

        var now = DateTimeOffset.UtcNow;
        var leaseUntil = now.AddSeconds(Math.Max(5, leaseSeconds));

        var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE content_creator_v2.gcc_v2_jobs
            SET ""ClaimedByInstanceId"" = {instanceId},
                ""ClaimedAtUtc"" = {now},
                ""LeaseUntilUtc"" = {leaseUntil},
                ""Status"" = CASE WHEN ""Status"" = 'pending' THEN 'running' ELSE ""Status"" END,
                ""AttemptCount"" = ""AttemptCount"" + 1,
                ""UpdatedAtUtc"" = {now}
            WHERE ""Id"" = {id}
              AND (
                ""Status"" = 'pending'
                OR (""Status"" = 'running' AND ""LeaseUntilUtc"" IS NOT NULL AND ""LeaseUntilUtc"" < {now})
              )", ct);

        if (rows == 0)
        {
            var exists = await _db.GccV2Jobs.AsNoTracking().AnyAsync(j => j.Id == id, ct);
            if (!exists) return NotFound();
            return Conflict(new { claimed = false });
        }

        var job = await _db.GccV2Jobs.AsNoTracking().FirstAsync(j => j.Id == id, ct);
        return Ok(job);
    }

    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<GccV2JobEvent>>> GetEvents(
        Guid id,
        [FromQuery] int afterSeq = 0,
        CancellationToken ct = default)
    {
        var events = await _db.GccV2JobEvents.AsNoTracking()
            .Where(e => e.JobId == id && e.Seq > afterSeq)
            .OrderBy(e => e.Seq)
            .ToListAsync(ct);
        return Ok(events);
    }

    [HttpPost("{id:guid}/events")]
    public async Task<ActionResult<GccV2JobEvent>> AppendEvent(
        Guid id,
        [FromBody] AppendGccV2JobEventCommand command,
        CancellationToken ct)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.Type))
            return BadRequest("type is required");

        var jobExists = await _db.GccV2Jobs.AsNoTracking().AnyAsync(j => j.Id == id, ct);
        if (!jobExists) return NotFound();

        // Small retry loop: unique (JobId, Seq) index guards against a lost-update race on the
        // next-seq read without needing an explicit table lock for this low-contention path.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var maxSeq = await _db.GccV2JobEvents
                .Where(e => e.JobId == id)
                .Select(e => (int?)e.Seq)
                .MaxAsync(ct);
            var nextSeq = (maxSeq ?? 0) + 1;

            var evt = new GccV2JobEvent
            {
                JobId = id,
                Seq = nextSeq,
                Type = command.Type,
                PayloadJson = string.IsNullOrWhiteSpace(command.PayloadJson) ? "{}" : command.PayloadJson,
            };

            _db.GccV2JobEvents.Add(evt);
            try
            {
                await _db.SaveChangesAsync(ct);
                if (command.Wake is not false)
                    await NotifyAsync(id, ct);
                return CreatedAtAction(nameof(GetEvents), new { id }, evt);
            }
            catch (DbUpdateException ex)
            {
                _db.Entry(evt).State = EntityState.Detached;
                _logger.LogWarning(ex, "Seq collision appending event to job {JobId}, retrying", id);
            }
        }

        return Conflict("Could not allocate a sequence number for this event after several attempts.");
    }

    [HttpGet("{id:guid}/stage-results")]
    public async Task<ActionResult<IReadOnlyList<GccV2StageResult>>> GetStageResults(Guid id, CancellationToken ct)
    {
        var results = await _db.GccV2StageResults.AsNoTracking()
            .Where(r => r.JobId == id)
            .OrderBy(r => r.CompletedAtUtc)
            .ToListAsync(ct);
        return Ok(results);
    }

    [HttpPost("{id:guid}/stage-results")]
    public async Task<ActionResult<GccV2StageResult>> AddStageResult(
        Guid id,
        [FromBody] CreateGccV2StageResultCommand command,
        CancellationToken ct)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.Stage))
            return BadRequest("stage is required");

        var jobExists = await _db.GccV2Jobs.AsNoTracking().AnyAsync(j => j.Id == id, ct);
        if (!jobExists) return NotFound();

        var result = new GccV2StageResult
        {
            JobId = id,
            Stage = command.Stage,
            SectionKey = command.SectionKey,
            OutputJson = string.IsNullOrWhiteSpace(command.OutputJson) ? "{}" : command.OutputJson,
            TokensUsed = command.TokensUsed ?? 0,
        };

        _db.GccV2StageResults.Add(result);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetStageResults), new { id }, result);
    }

    private Task NotifyAsync(Guid jobId, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_notify({NotifyChannel}, {jobId.ToString()})", ct);

    public record CreateGccV2JobCommand(
        Guid CreateId,
        string OwnerUserId,
        string? ContentType,
        Guid? BriefId,
        Guid? SiteAnalysisProfileId = null);

    public record PatchGccV2JobCommand(
        string? Stage,
        string? Status,
        string? ResultJson,
        string? Error,
        int? TokensUsed,
        bool? AttemptCountIncrement,
        bool? ReleaseClaim,
        DateTimeOffset? LeaseUntilUtc,
        DateTimeOffset? CompletedAtUtc,
        bool? Wake);

    public record AppendGccV2JobEventCommand(string Type, string? PayloadJson, bool? Wake);

    public record CreateGccV2StageResultCommand(string Stage, string? SectionKey, string? OutputJson, int? TokensUsed);
}
