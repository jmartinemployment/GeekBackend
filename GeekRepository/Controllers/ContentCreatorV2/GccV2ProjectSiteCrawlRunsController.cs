using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/project-site/runs")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2ProjectSiteCrawlRunsController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2ProjectSiteCrawlRunsController(ContentCreatorV2DbContext db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2ProjectSiteCrawlRun>> GetById(Guid id, CancellationToken ct)
    {
        var row = await _db.GccV2ProjectSiteCrawlRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpGet("latest")]
    public async Task<ActionResult<GccV2ProjectSiteCrawlRun>> GetLatest(
        [FromQuery] string ownerUserId,
        [FromQuery] string siteUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId) || string.IsNullOrWhiteSpace(siteUrl))
            return BadRequest("ownerUserId and siteUrl are required");

        var normalizedSiteUrl = siteUrl.Trim();
        var row = await _db.GccV2ProjectSiteCrawlRuns.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId && r.SiteUrl == normalizedSiteUrl
                        && r.Status == "complete")
            .OrderByDescending(r => r.CompletedAtUtc ?? r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return row is null ? NotFound() : Ok(row);
    }

    [HttpGet("by-status/{status}")]
    public async Task<ActionResult<IReadOnlyList<GccV2ProjectSiteCrawlRun>>> ListByStatus(
        string status,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(status))
            return BadRequest("status is required");

        limit = Math.Clamp(limit, 1, 200);
        var normalized = status.Trim().ToLowerInvariant();
        var rows = await _db.GccV2ProjectSiteCrawlRuns.AsNoTracking()
            .Where(r => r.Status.ToLower() == normalized)
            .OrderBy(r => r.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2ProjectSiteCrawlRun>> Create(
        [FromBody] CreateGccV2ProjectSiteCrawlRunCommand command,
        CancellationToken ct)
    {
        if (command is null
            || string.IsNullOrWhiteSpace(command.OwnerUserId)
            || string.IsNullOrWhiteSpace(command.SiteUrl))
            return BadRequest("ownerUserId and siteUrl are required");

        var row = new GccV2ProjectSiteCrawlRun
        {
            Id = Guid.NewGuid(),
            OwnerUserId = command.OwnerUserId.Trim(),
            SiteUrl = command.SiteUrl.Trim(),
            Status = "pending",
            SeedUrlsJson = string.IsNullOrWhiteSpace(command.SeedUrlsJson) ? "[]" : command.SeedUrlsJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        _db.GccV2ProjectSiteCrawlRuns.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GccV2ProjectSiteCrawlRun>> Patch(
        Guid id,
        [FromBody] PatchGccV2ProjectSiteCrawlRunCommand command,
        CancellationToken ct)
    {
        var row = await _db.GccV2ProjectSiteCrawlRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(command.Status))
            row.Status = command.Status.Trim();
        if (command.HostProgressJson is not null)
            row.HostProgressJson = command.HostProgressJson;
        if (command.ErrorSummary is not null)
            row.ErrorSummary = command.ErrorSummary;
        if (command.StartedAtUtc is not null)
            row.StartedAtUtc = command.StartedAtUtc;
        if (command.CompletedAtUtc is not null)
            row.CompletedAtUtc = command.CompletedAtUtc;

        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    public record CreateGccV2ProjectSiteCrawlRunCommand(string OwnerUserId, string SiteUrl, string? SeedUrlsJson);

    public record PatchGccV2ProjectSiteCrawlRunCommand(
        string? Status = null,
        string? HostProgressJson = null,
        string? ErrorSummary = null,
        DateTimeOffset? StartedAtUtc = null,
        DateTimeOffset? CompletedAtUtc = null);
}
