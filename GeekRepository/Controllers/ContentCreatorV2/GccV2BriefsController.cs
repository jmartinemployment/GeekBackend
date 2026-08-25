using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>Versioned content briefs for a v2 create. Minimal CRUD — Phase 4 fills out fields.</summary>
[ApiController]
[Route("repo/content-creator-v2/briefs")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2BriefsController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2BriefsController(ContentCreatorV2DbContext db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2Brief>> GetById(Guid id, CancellationToken ct)
    {
        var brief = await _db.GccV2Briefs.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        return brief is null ? NotFound() : Ok(brief);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2Brief>>> ListByCreate([FromQuery] Guid createId, CancellationToken ct)
    {
        if (createId == Guid.Empty)
            return BadRequest("createId is required");

        var briefs = await _db.GccV2Briefs.AsNoTracking()
            .Where(b => b.CreateId == createId)
            .OrderByDescending(b => b.Version)
            .ToListAsync(ct);
        return Ok(briefs);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2Brief>> Create([FromBody] CreateGccV2BriefCommand command, CancellationToken ct)
    {
        if (command is null || command.CreateId == Guid.Empty)
            return BadRequest("createId is required");

        var maxVersion = await _db.GccV2Briefs
            .Where(b => b.CreateId == command.CreateId)
            .Select(b => (int?)b.Version)
            .MaxAsync(ct);
        var nextVersion = (maxVersion ?? 0) + 1;

        var brief = new GccV2Brief
        {
            CreateId = command.CreateId,
            Version = nextVersion,
            TargetKeyword = command.TargetKeyword ?? string.Empty,
            ContentType = string.IsNullOrWhiteSpace(command.ContentType) ? "blog" : command.ContentType,
            RawBriefJson = string.IsNullOrWhiteSpace(command.RawBriefJson) ? "{}" : command.RawBriefJson,
        };

        _db.GccV2Briefs.Add(brief);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = brief.Id }, brief);
    }

    public record CreateGccV2BriefCommand(Guid CreateId, string? TargetKeyword, string? ContentType, string? RawBriefJson);
}
