using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>
/// Canonical partner/competitor entities shared by the RAG writer and every task agent —
/// see plans/make-content-creator-workable.md Milestone 1. Replaces free-text entity
/// names and the hardcoded GeekAPI RagEntitySeedList.
/// </summary>
[ApiController]
[Route("repo/content-creator-v2/research-entities")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2ResearchEntitiesController(ContentCreatorV2DbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2ResearchEntity>>> List(
        [FromQuery] string? role, [FromQuery] bool includeArchived, CancellationToken ct)
    {
        var query = db.GccV2ResearchEntities.AsNoTracking().AsQueryable();
        if (!includeArchived) query = query.Where(x => x.ArchivedAtUtc == null);
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(x => x.Role == role);
        return Ok(await query.OrderBy(x => x.Name).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2ResearchEntity>> Get(Guid id, CancellationToken ct)
    {
        var entity = await db.GccV2ResearchEntities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? NotFound() : Ok(entity);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2ResearchEntity>> Create(
        [FromBody] CreateResearchEntityCommand command, CancellationToken ct)
    {
        var name = command.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("name is required.");
        if (command.Role is not ("partner" or "competitor")) return BadRequest("role must be 'partner' or 'competitor'.");
        if (await db.GccV2ResearchEntities.AnyAsync(x => x.Name == name, ct))
            return Conflict($"A research entity named '{name}' already exists.");

        var entity = new GccV2ResearchEntity
        {
            Name = name,
            Role = command.Role,
            PrimaryUrl = string.IsNullOrWhiteSpace(command.PrimaryUrl) ? null : command.PrimaryUrl.Trim(),
            Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim(),
            CreatedBy = command.Actor,
        };
        db.GccV2ResearchEntities.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, entity);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GccV2ResearchEntity>> Update(
        Guid id, [FromBody] UpdateResearchEntityCommand command, CancellationToken ct)
    {
        var entity = await db.GccV2ResearchEntities.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (command.Role is not null and not ("partner" or "competitor"))
            return BadRequest("role must be 'partner' or 'competitor'.");

        if (command.Role is not null) entity.Role = command.Role;
        if (command.PrimaryUrl is not null) entity.PrimaryUrl = string.IsNullOrWhiteSpace(command.PrimaryUrl) ? null : command.PrimaryUrl.Trim();
        if (command.Notes is not null) entity.Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(entity);
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<ActionResult<GccV2ResearchEntity>> Archive(Guid id, CancellationToken ct)
    {
        var entity = await db.GccV2ResearchEntities.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        entity.ArchivedAtUtc ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(entity);
    }

    public sealed record CreateResearchEntityCommand(string Name, string Role, string? PrimaryUrl, string? Notes, string Actor);
    public sealed record UpdateResearchEntityCommand(string? Role, string? PrimaryUrl, string? Notes);
}
