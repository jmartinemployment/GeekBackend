using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/drive/connections")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2DriveConnectionsController(ContentCreatorV2DbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2DriveConnection>>> List(
        [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return BadRequest("ownerUserId is required.");
        var rows = await db.GccV2DriveConnections.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId)
            .OrderByDescending(x => x.ConnectedAtUtc)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2DriveConnection>> Get(
        Guid id, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return BadRequest("ownerUserId is required.");
        var row = await db.GccV2DriveConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2DriveConnection>> Upsert(UpsertCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId) || string.IsNullOrWhiteSpace(command.AccountLabel))
            return BadRequest("ownerUserId and accountLabel are required.");
        var accountLabel = command.AccountLabel.Trim();
        var status = string.IsNullOrWhiteSpace(command.Status) ? "connected" : command.Status.Trim();
        var existing = await db.GccV2DriveConnections
            .SingleOrDefaultAsync(x => x.OwnerUserId == command.OwnerUserId && x.AccountLabel == accountLabel, ct);
        if (existing is null)
        {
            existing = new GccV2DriveConnection
            {
                OwnerUserId = command.OwnerUserId,
                AccountLabel = accountLabel,
            };
            db.Add(existing);
        }

        existing.Status = status;
        existing.EncryptedRefreshToken = command.EncryptedRefreshToken ?? [];
        existing.EncryptionIv = command.EncryptionIv ?? [];
        existing.EncryptionTag = command.EncryptionTag ?? [];
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (existing.ConnectedAtUtc == default) existing.ConnectedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(existing);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return BadRequest("ownerUserId is required.");
        var row = await db.GccV2DriveConnections
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
        if (row is null) return NotFound();
        db.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record UpsertCommand(
        string OwnerUserId,
        string AccountLabel,
        string? Status,
        byte[]? EncryptedRefreshToken,
        byte[]? EncryptionIv,
        byte[]? EncryptionTag);
}
