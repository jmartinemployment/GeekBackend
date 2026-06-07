using GeekSeo.Persistence.Entities;
using GeekSeo.Persistence.Data;
using GeekRepository.Services.Seo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/google")]
public sealed class GoogleConnectionsController(SeoDbContext db) : ControllerBase
{
    [HttpGet("gsc-connection")]
    public async Task<IActionResult> GetGsc([FromQuery] Guid projectId, [FromQuery] Guid userId, CancellationToken ct)
    {
        if (!await OwnsProjectAsync(projectId, userId, ct))
            return NotFound();

        var row = await GoogleConnectionDomainService.FindGscForProjectAsync(db, projectId, userId, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpPut("gsc-connection")]
    public async Task<IActionResult> UpsertGsc([FromBody] SeoGscConnection body, CancellationToken ct)
    {
        if (!await OwnsProjectAsync(body.ProjectId, body.UserId, ct))
            return BadRequest("Project not found or access denied.");

        var existing = await db.GscConnections
            .FirstOrDefaultAsync(c => c.ProjectId == body.ProjectId, ct);
        if (existing is null)
        {
            if (body.Id == Guid.Empty)
                body.Id = Guid.NewGuid();
            db.GscConnections.Add(body);
        }
        else
        {
            existing.SiteUrl = body.SiteUrl;
            existing.EncryptedRefreshToken = body.EncryptedRefreshToken;
            existing.EncryptionIv = body.EncryptionIv;
            existing.EncryptionTag = body.EncryptionTag;
            existing.ConnectedAt = body.ConnectedAt;
            existing.UserId = body.UserId;
            body = existing;
        }

        await GoogleConnectionDomainService.SyncGscToDomainAsync(db, body, ct);
        await db.SaveChangesAsync(ct);
        return Ok(body);
    }

    [HttpGet("ga4-connection")]
    public async Task<IActionResult> GetGa4([FromQuery] Guid projectId, [FromQuery] Guid userId, CancellationToken ct)
    {
        if (!await OwnsProjectAsync(projectId, userId, ct))
            return NotFound();

        var row = await GoogleConnectionDomainService.FindGa4ForProjectAsync(db, projectId, userId, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpPut("ga4-connection")]
    public async Task<IActionResult> UpsertGa4(
        [FromQuery] Guid userId,
        [FromBody] SeoGa4Connection body,
        CancellationToken ct)
    {
        if (!await OwnsProjectAsync(body.ProjectId, userId, ct))
            return BadRequest("Project not found or access denied.");

        var existing = await db.Ga4Connections
            .FirstOrDefaultAsync(c => c.ProjectId == body.ProjectId, ct);
        if (existing is null)
        {
            if (body.Id == Guid.Empty)
                body.Id = Guid.NewGuid();
            db.Ga4Connections.Add(body);
        }
        else
        {
            existing.PropertyId = body.PropertyId;
            existing.EncryptedRefreshToken = body.EncryptedRefreshToken;
            existing.EncryptionIv = body.EncryptionIv;
            existing.EncryptionTag = body.EncryptionTag;
            existing.ConnectedAt = body.ConnectedAt;
            body = existing;
        }

        await GoogleConnectionDomainService.SyncGa4ToDomainAsync(db, body, userId, ct);
        await db.SaveChangesAsync(ct);
        return Ok(body);
    }

    [HttpDelete("connections")]
    public async Task<IActionResult> Delete([FromQuery] Guid projectId, [FromQuery] Guid userId, CancellationToken ct)
    {
        if (!await OwnsProjectAsync(projectId, userId, ct))
            return NotFound();

        await GoogleConnectionDomainService.DisconnectDomainAsync(db, projectId, userId, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<bool> OwnsProjectAsync(Guid projectId, Guid userId, CancellationToken ct) =>
        db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId && p.UserId == userId, ct);
}
