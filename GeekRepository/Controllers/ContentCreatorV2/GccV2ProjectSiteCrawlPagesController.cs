using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/project-site/pages")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2ProjectSiteCrawlPagesController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2ProjectSiteCrawlPagesController(ContentCreatorV2DbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2ProjectSiteCrawlPage>>> ListByRun(
        [FromQuery] Guid runId,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");

        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(0, offset);

        var pages = await _db.GccV2ProjectSiteCrawlPages.AsNoTracking()
            .Where(p => p.RunId == runId)
            .OrderBy(p => p.CrawledAtUtc)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
        return Ok(pages);
    }

    [HttpGet("activity")]
    public async Task<ActionResult<object>> GetRunActivity(
        [FromQuery] Guid runId,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");

        var pageCount = await _db.GccV2ProjectSiteCrawlPages.AsNoTracking()
            .CountAsync(p => p.RunId == runId, ct);

        DateTimeOffset? lastCrawledAtUtc = pageCount == 0
            ? null
            : await _db.GccV2ProjectSiteCrawlPages.AsNoTracking()
                .Where(p => p.RunId == runId)
                .MaxAsync(p => (DateTimeOffset?)p.CrawledAtUtc, ct);

        return Ok(new { pageCount, lastCrawledAtUtc });
    }

    [HttpPost("batch")]
    public async Task<ActionResult<object>> CreateBatch(
        [FromBody] CreateGccV2ProjectSiteCrawlPageBatchCommand command,
        CancellationToken ct)
    {
        if (command is null || command.RunId == Guid.Empty || command.Pages is null || command.Pages.Count == 0)
            return BadRequest("runId and pages are required");

        var created = new List<CreatedGccV2ProjectSiteCrawlPageItem>(command.Pages.Count);
        var now = DateTimeOffset.UtcNow;

        foreach (var p in command.Pages)
        {
            var id = Guid.NewGuid();
            _db.GccV2ProjectSiteCrawlPages.Add(new GccV2ProjectSiteCrawlPage
            {
                Id = id,
                RunId = command.RunId,
                Origin = p.Origin ?? "",
                Url = p.Url ?? "",
                FinalUrl = string.IsNullOrWhiteSpace(p.FinalUrl) ? p.Url ?? "" : p.FinalUrl,
                StatusCode = p.StatusCode,
                RobotsAllowed = p.RobotsAllowed,
                Html = p.Html,
                CrawledAtUtc = now,
            });
            created.Add(new CreatedGccV2ProjectSiteCrawlPageItem(p.Url ?? "", id));
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { count = created.Count, pages = created });
    }

    public record CreateGccV2ProjectSiteCrawlPageBatchCommand(
        Guid RunId,
        IReadOnlyList<CreateGccV2ProjectSiteCrawlPageItemCommand> Pages);

    public record CreateGccV2ProjectSiteCrawlPageItemCommand(
        string Origin,
        string Url,
        string? FinalUrl,
        int StatusCode,
        bool RobotsAllowed,
        string? Html);

    public record CreatedGccV2ProjectSiteCrawlPageItem(string Url, Guid PageId);
}
