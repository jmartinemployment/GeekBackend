using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>
/// Content Creator v2 "creates" (top-level content requests). Isolated from v1
/// <c>gcc_creates</c> — separate table, separate schema.
/// </summary>
[ApiController]
[Route("repo/content-creator-v2/creates")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2CreatesController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2CreatesController(ContentCreatorV2DbContext db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2Create>> GetById(Guid id, CancellationToken ct)
    {
        var create = await _db.GccV2Creates.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return create is null ? NotFound() : Ok(create);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2Create>>> List(
        [FromQuery] string? ownerUserId,
        CancellationToken ct)
    {
        var query = _db.GccV2Creates.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(ownerUserId))
            query = query.Where(c => c.OwnerUserId == ownerUserId);

        var results = await query.OrderByDescending(c => c.CreatedAtUtc).ToListAsync(ct);
        return Ok(results);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2Create>> Create([FromBody] CreateGccV2CreateCommand command, CancellationToken ct)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.OwnerUserId) || string.IsNullOrWhiteSpace(command.Title))
            return BadRequest("ownerUserId and title are required");

        var create = new GccV2Create
        {
            OwnerUserId = command.OwnerUserId,
            Title = command.Title,
            ContentType = string.IsNullOrWhiteSpace(command.ContentType) ? "blog" : command.ContentType,
            SiteSectionJson = string.IsNullOrWhiteSpace(command.SiteSectionJson) ? null : command.SiteSectionJson,
            SiteUrl = string.IsNullOrWhiteSpace(command.SiteUrl) ? null : command.SiteUrl.Trim(),
        };

        _db.GccV2Creates.Add(create);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = create.Id }, create);
    }

    public record CreateGccV2CreateCommand(
        string OwnerUserId,
        string Title,
        string? ContentType,
        string? SiteSectionJson = null,
        string? SiteUrl = null);
}
