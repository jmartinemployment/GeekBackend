using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>Versioned outlines for a brief. Minimal CRUD — Phase 5 (OverlapGate) reads
/// <see cref="GccV2Outline.HierarchyChildHeadingsJson"/> off the latest version.</summary>
[ApiController]
[Route("repo/content-creator-v2/outlines")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2OutlinesController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2OutlinesController(ContentCreatorV2DbContext db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2Outline>> GetById(Guid id, CancellationToken ct)
    {
        var outline = await _db.GccV2Outlines.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        return outline is null ? NotFound() : Ok(outline);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2Outline>>> ListByBrief([FromQuery] Guid briefId, CancellationToken ct)
    {
        if (briefId == Guid.Empty)
            return BadRequest("briefId is required");

        var outlines = await _db.GccV2Outlines.AsNoTracking()
            .Where(o => o.BriefId == briefId)
            .OrderByDescending(o => o.Version)
            .ToListAsync(ct);
        return Ok(outlines);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2Outline>> Create([FromBody] CreateGccV2OutlineCommand command, CancellationToken ct)
    {
        if (command is null || command.BriefId == Guid.Empty)
            return BadRequest("briefId is required");

        var maxVersion = await _db.GccV2Outlines
            .Where(o => o.BriefId == command.BriefId)
            .Select(o => (int?)o.Version)
            .MaxAsync(ct);

        var outline = new GccV2Outline
        {
            BriefId = command.BriefId,
            Version = (maxVersion ?? 0) + 1,
            OutlineJson = string.IsNullOrWhiteSpace(command.OutlineJson) ? "{}" : command.OutlineJson,
            HierarchyChildHeadingsJson = string.IsNullOrWhiteSpace(command.HierarchyChildHeadingsJson)
                ? "[]"
                : command.HierarchyChildHeadingsJson,
        };

        _db.GccV2Outlines.Add(outline);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = outline.Id }, outline);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GccV2Outline>> Patch(Guid id, [FromBody] PatchGccV2OutlineCommand command, CancellationToken ct)
    {
        var outline = await _db.GccV2Outlines.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (outline is null) return NotFound();

        if (command.OutlineJson is not null) outline.OutlineJson = command.OutlineJson;
        if (command.HierarchyChildHeadingsJson is not null) outline.HierarchyChildHeadingsJson = command.HierarchyChildHeadingsJson;
        if (command.FrozenAtUtc is not null) outline.FrozenAtUtc = command.FrozenAtUtc;

        await _db.SaveChangesAsync(ct);
        return Ok(outline);
    }

    public record CreateGccV2OutlineCommand(Guid BriefId, string? OutlineJson, string? HierarchyChildHeadingsJson);

    public record PatchGccV2OutlineCommand(string? OutlineJson, string? HierarchyChildHeadingsJson, DateTimeOffset? FrozenAtUtc);
}
