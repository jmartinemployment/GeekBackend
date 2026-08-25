using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>
/// Persists/serves <see cref="GccV2AiVisibilitySnapshot"/> rows. GeekAPI's
/// <c>GccV2AiVisibilityService</c> is the only caller that derives a snapshot's contents (SEO/GEO
/// scores, published URLs) — this controller only stores/lists the resulting record.
/// </summary>
[ApiController]
[Route("repo/content-creator-v2/ai-visibility-snapshots")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2AiVisibilitySnapshotsController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2AiVisibilitySnapshotsController(ContentCreatorV2DbContext db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2AiVisibilitySnapshot>> GetById(Guid id, CancellationToken ct)
    {
        var snapshot = await _db.GccV2AiVisibilitySnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    /// <summary>Latest-first list of snapshots for a create.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2AiVisibilitySnapshot>>> List(
        [FromQuery] Guid createId,
        CancellationToken ct)
    {
        if (createId == Guid.Empty)
            return BadRequest("createId is required");

        var results = await _db.GccV2AiVisibilitySnapshots.AsNoTracking()
            .Where(s => s.CreateId == createId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(results);
    }

    /// <summary>Most recent snapshot for a create, or 404 if none has been built yet.</summary>
    [HttpGet("latest")]
    public async Task<ActionResult<GccV2AiVisibilitySnapshot>> GetLatest(
        [FromQuery] Guid createId,
        CancellationToken ct)
    {
        if (createId == Guid.Empty)
            return BadRequest("createId is required");

        var snapshot = await _db.GccV2AiVisibilitySnapshots.AsNoTracking()
            .Where(s => s.CreateId == createId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2AiVisibilitySnapshot>> Create(
        [FromBody] CreateGccV2AiVisibilitySnapshotCommand command,
        CancellationToken ct)
    {
        if (command is null || command.CreateId == Guid.Empty || string.IsNullOrWhiteSpace(command.OwnerUserId))
            return BadRequest("createId and ownerUserId are required");

        var snapshot = new GccV2AiVisibilitySnapshot
        {
            Id = Guid.NewGuid(),
            CreateId = command.CreateId,
            JobId = command.JobId,
            OwnerUserId = command.OwnerUserId,
            Score = command.Score,
            ReportJson = string.IsNullOrWhiteSpace(command.ReportJson) ? "{}" : command.ReportJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        _db.GccV2AiVisibilitySnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = snapshot.Id }, snapshot);
    }

    public sealed record CreateGccV2AiVisibilitySnapshotCommand(
        Guid CreateId,
        Guid? JobId,
        string OwnerUserId,
        int Score,
        string? ReportJson);
}
