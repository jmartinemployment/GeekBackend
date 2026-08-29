using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/tool-source-crawl-runs")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2ToolSourceCrawlRunsController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2ToolSourceCrawlRunsController(ContentCreatorV2DbContext db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2ToolSourceCrawlRun>> GetById(Guid id, CancellationToken ct)
    {
        var row = await _db.GccV2ToolSourceCrawlRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpGet("latest")]
    public async Task<ActionResult<GccV2ToolSourceCrawlRun>> GetLatest(
        [FromQuery] Guid createId,
        CancellationToken ct = default)
    {
        if (createId == Guid.Empty)
            return BadRequest("createId is required");

        var row = await _db.GccV2ToolSourceCrawlRuns.AsNoTracking()
            .Where(r => r.CreateId == createId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2ToolSourceCrawlRun>> Create(
        [FromBody] CreateGccV2ToolSourceCrawlRunCommand command,
        CancellationToken ct)
    {
        if (command is null || command.CreateId == Guid.Empty)
            return BadRequest("createId is required");

        var row = new GccV2ToolSourceCrawlRun
        {
            Id = Guid.NewGuid(),
            CreateId = command.CreateId,
            Status = "pending",
            SeedUrlsJson = string.IsNullOrWhiteSpace(command.SeedUrlsJson) ? "[]" : command.SeedUrlsJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        _db.GccV2ToolSourceCrawlRuns.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GccV2ToolSourceCrawlRun>> Patch(
        Guid id,
        [FromBody] PatchGccV2ToolSourceCrawlRunCommand command,
        CancellationToken ct)
    {
        var row = await _db.GccV2ToolSourceCrawlRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(command.Status))
            row.Status = command.Status.Trim();
        if (command.HostProgressJson is not null)
            row.HostProgressJson = command.HostProgressJson;
        if (command.PartnerResearchJson is not null)
            row.PartnerResearchJson = command.PartnerResearchJson;
        if (command.ErrorSummary is not null)
            row.ErrorSummary = command.ErrorSummary;
        if (command.StartedAtUtc is not null)
            row.StartedAtUtc = command.StartedAtUtc;
        if (command.CompletedAtUtc is not null)
            row.CompletedAtUtc = command.CompletedAtUtc;

        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    public record CreateGccV2ToolSourceCrawlRunCommand(Guid CreateId, string? SeedUrlsJson);

    public record PatchGccV2ToolSourceCrawlRunCommand(
        string? Status = null,
        string? HostProgressJson = null,
        string? PartnerResearchJson = null,
        string? ErrorSummary = null,
        DateTimeOffset? StartedAtUtc = null,
        DateTimeOffset? CompletedAtUtc = null);
}

[ApiController]
[Route("repo/content-creator-v2/tool-source-crawl-pages")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2ToolSourceCrawlPagesController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2ToolSourceCrawlPagesController(ContentCreatorV2DbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2ToolSourceCrawlPage>>> ListByRun(
        [FromQuery] Guid runId,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");

        var pages = await _db.GccV2ToolSourceCrawlPages.AsNoTracking()
            .Where(p => p.RunId == runId)
            .OrderBy(p => p.CrawledAtUtc)
            .ToListAsync(ct);
        return Ok(pages);
    }

    [HttpPost("batch")]
    public async Task<ActionResult<object>> CreateBatch(
        [FromBody] CreateGccV2ToolSourceCrawlPageBatchCommand command,
        CancellationToken ct)
    {
        if (command is null || command.RunId == Guid.Empty || command.Pages is null || command.Pages.Count == 0)
            return BadRequest("runId and pages are required");

        foreach (var p in command.Pages)
        {
            _db.GccV2ToolSourceCrawlPages.Add(new GccV2ToolSourceCrawlPage
            {
                Id = Guid.NewGuid(),
                RunId = command.RunId,
                Origin = p.Origin ?? "",
                Url = p.Url ?? "",
                FinalUrl = p.FinalUrl ?? p.Url ?? "",
                StatusCode = p.StatusCode,
                RobotsAllowed = p.RobotsAllowed,
                Html = p.Html,
                CrawledAtUtc = DateTimeOffset.UtcNow,
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { count = command.Pages.Count });
    }

    public record CreateGccV2ToolSourceCrawlPageBatchCommand(
        Guid RunId,
        IReadOnlyList<CreateGccV2ToolSourceCrawlPageItem> Pages);

    public record CreateGccV2ToolSourceCrawlPageItem(
        string Origin,
        string Url,
        string? FinalUrl,
        int StatusCode,
        bool RobotsAllowed,
        string? Html);
}
