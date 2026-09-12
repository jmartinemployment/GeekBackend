using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>
/// Ad-copy few-shot templates shared across sessions — see
/// plans/make-content-creator-workable.md Milestone 1. Replaces the localStorage-only
/// corpus that could not be shared or persisted durably.
/// </summary>
[ApiController]
[Route("repo/content-creator-v2/ad-templates")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2AdTemplatesController(ContentCreatorV2DbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2AdTemplate>>> List(
        [FromQuery] bool includeArchived, CancellationToken ct)
    {
        var query = db.GccV2AdTemplates.AsNoTracking().AsQueryable();
        if (!includeArchived) query = query.Where(x => x.ArchivedAtUtc == null);
        return Ok(await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<GccV2AdTemplate>> Create(
        [FromBody] CreateAdTemplateCommand command, CancellationToken ct)
    {
        var body = command.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body) || body.Length < 8)
            return BadRequest("body must be at least 8 characters.");

        var entity = new GccV2AdTemplate
        {
            Name = string.IsNullOrWhiteSpace(command.Name) ? "Untitled" : command.Name.Trim(),
            Channel = string.IsNullOrWhiteSpace(command.Channel) ? null : command.Channel.Trim(),
            Framework = string.IsNullOrWhiteSpace(command.Framework) ? null : command.Framework.Trim(),
            Body = body,
            CreatedBy = command.Actor,
        };
        db.GccV2AdTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(entity);
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<ActionResult<GccV2AdTemplate>> Archive(Guid id, CancellationToken ct)
    {
        var entity = await db.GccV2AdTemplates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        entity.ArchivedAtUtc ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(entity);
    }

    public sealed record CreateAdTemplateCommand(string Name, string? Channel, string? Framework, string Body, string Actor);
}
