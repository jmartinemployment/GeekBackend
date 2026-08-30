using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.GeekCrawler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.GeekCrawler;

[ApiController]
[Route("repo/geek-crawler/pages")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GeekCrawlerPagesController : ControllerBase
{
    private readonly GeekCrawlerDbContext _db;

    public GeekCrawlerPagesController(GeekCrawlerDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GeekCrawlerPage>>> ListByRun(
        [FromQuery] Guid runId,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");

        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(0, offset);

        var pages = await _db.GeekCrawlerPages.AsNoTracking()
            .Where(p => p.RunId == runId)
            .OrderBy(p => p.CrawledAtUtc)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
        return Ok(pages);
    }

    [HttpPost("batch")]
    public async Task<ActionResult<object>> CreateBatch(
        [FromBody] CreateGeekCrawlerPageBatchCommand command,
        CancellationToken ct)
    {
        if (command is null || command.RunId == Guid.Empty || command.Pages is null || command.Pages.Count == 0)
            return BadRequest("runId and pages are required");

        var created = new List<CreatedGeekCrawlerPageItem>(command.Pages.Count);
        var now = DateTimeOffset.UtcNow;

        foreach (var p in command.Pages)
        {
            var id = Guid.NewGuid();
            _db.GeekCrawlerPages.Add(new GeekCrawlerPage
            {
                Id = id,
                RunId = command.RunId,
                Origin = p.Origin ?? "",
                Url = p.Url ?? "",
                FinalUrl = p.FinalUrl ?? p.Url ?? "",
                StatusCode = p.StatusCode,
                RobotsAllowed = p.RobotsAllowed,
                Html = p.Html,
                CrawledAtUtc = now,
            });
            created.Add(new CreatedGeekCrawlerPageItem(p.Url ?? "", id));
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { count = created.Count, pages = created });
    }

    public record CreateGeekCrawlerPageBatchCommand(
        Guid RunId,
        IReadOnlyList<CreateGeekCrawlerPageItem> Pages);

    public record CreateGeekCrawlerPageItem(
        string Origin,
        string Url,
        string? FinalUrl,
        int StatusCode,
        bool RobotsAllowed,
        string? Html);

    public record CreatedGeekCrawlerPageItem(string Url, Guid PageId);
}
