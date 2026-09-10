using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/gsc/connections")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2GscConnectionsController(ContentCreatorV2DbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2GscConnection>>> List(
        [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return BadRequest("ownerUserId is required.");
        var rows = await db.GccV2GscConnections.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId)
            .OrderByDescending(x => x.ConnectedAtUtc)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2GscConnection>> Get(
        Guid id, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return BadRequest("ownerUserId is required.");
        var row = await db.GccV2GscConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2GscConnection>> Upsert(UpsertCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId) || string.IsNullOrWhiteSpace(command.SiteUrl))
            return BadRequest("ownerUserId and siteUrl are required.");
        var siteUrl = command.SiteUrl.Trim();
        var status = string.IsNullOrWhiteSpace(command.Status) ? "connected" : command.Status.Trim();
        var existing = await db.GccV2GscConnections
            .SingleOrDefaultAsync(x => x.OwnerUserId == command.OwnerUserId && x.SiteUrl == siteUrl, ct);
        if (existing is null)
        {
            existing = new GccV2GscConnection
            {
                OwnerUserId = command.OwnerUserId,
                SiteUrl = siteUrl,
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
        var row = await db.GccV2GscConnections
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
        if (row is null) return NotFound();
        db.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record UpsertCommand(
        string OwnerUserId,
        string SiteUrl,
        string? Status,
        byte[]? EncryptedRefreshToken,
        byte[]? EncryptionIv,
        byte[]? EncryptionTag);
}
