using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.GeekCrawler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.GeekCrawler;

[ApiController]
[Route("repo/geek-crawler/links")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GeekCrawlerLinksController : ControllerBase
{
    private readonly GeekCrawlerDbContext _db;

    public GeekCrawlerLinksController(GeekCrawlerDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GeekCrawlerLink>>> ListByRun(
        [FromQuery] Guid runId,
        [FromQuery] bool? sameOrigin = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");

        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(0, offset);

        var query = _db.GeekCrawlerLinks.AsNoTracking().Where(l => l.RunId == runId);
        if (sameOrigin is not null)
            query = query.Where(l => l.IsSameOrigin == sameOrigin.Value);

        var links = await query
            .OrderBy(l => l.DiscoveredAtUtc)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
        return Ok(links);
    }

    [HttpPost("batch")]
    public async Task<ActionResult<object>> CreateBatch(
        [FromBody] CreateGeekCrawlerLinkBatchCommand command,
        CancellationToken ct)
    {
        if (command is null || command.RunId == Guid.Empty || command.Links is null || command.Links.Count == 0)
            return BadRequest("runId and links are required");

        var existing = await _db.GeekCrawlerLinks.AsNoTracking()
            .Where(l => l.RunId == command.RunId)
            .Select(l => new { l.FromUrl, l.LinkUrl })
            .ToListAsync(ct);

        var seen = new HashSet<string>(
            existing.Select(e => $"{e.FromUrl}\0{e.LinkUrl}"),
            StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var inserted = 0;

        foreach (var link in command.Links)
        {
            var from = link.FromUrl ?? "";
            var to = link.LinkUrl ?? "";
            var key = $"{from}\0{to}";
            if (!seen.Add(key)) continue;

            _db.GeekCrawlerLinks.Add(new GeekCrawlerLink
            {
                Id = Guid.NewGuid(),
                RunId = command.RunId,
                PageId = link.PageId,
                FromUrl = from,
                LinkUrl = to,
                IsSameOrigin = link.IsSameOrigin,
                DiscoveredAtUtc = now,
            });
            inserted++;
        }

        if (inserted > 0)
            await _db.SaveChangesAsync(ct);

        return Ok(new { count = inserted });
    }

    public record CreateGeekCrawlerLinkBatchCommand(
        Guid RunId,
        IReadOnlyList<CreateGeekCrawlerLinkItem> Links);

    public record CreateGeekCrawlerLinkItem(
        Guid PageId,
        string FromUrl,
        string LinkUrl,
        bool IsSameOrigin);
}
