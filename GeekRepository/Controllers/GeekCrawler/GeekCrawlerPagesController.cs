using GeekRepository.Auth;
using GeekRepository.Data.Entities.GeekCrawler;
using GeekRepository.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.GeekCrawler;

[ApiController]
[Route("repo/geek-crawler/pages")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GeekCrawlerPagesController : ControllerBase
{
    private readonly IMongoGeekCrawlerService _mongo;

    public GeekCrawlerPagesController(IMongoGeekCrawlerService mongo) => _mongo = mongo;

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

        var pages = await _mongo.ListPagesByRunAsync(runId, limit, offset, ct);
        return Ok(pages);
    }

    [HttpGet("by-seeds")]
    public async Task<ActionResult<IReadOnlyList<GeekCrawlerPage>>> ListBySeeds(
        [FromQuery] Guid runId,
        [FromQuery] string seeds,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");
        if (string.IsNullOrWhiteSpace(seeds))
            return BadRequest("seeds is required");

        var urlList = seeds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();
        if (urlList.Count == 0)
            return BadRequest("seeds is required");

        var pages = await _mongo.ListPagesBySeedsAsync(runId, urlList, ct);
        return Ok(pages);
    }

    [HttpGet("for-resume")]
    public async Task<ActionResult<IReadOnlyList<GeekCrawlerPageResumeRow>>> ListForResume(
        [FromQuery] Guid runId,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");

        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(0, offset);

        var rows = await _mongo.ListPagesByRunForResumeAsync(runId, limit, offset, ct);
        return Ok(rows);
    }

    [HttpGet("activity")]
    public async Task<ActionResult<object>> GetRunActivity(
        [FromQuery] Guid runId,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");

        var pageCount = await _mongo.CountPagesByRunAsync(runId, ct);
        var lastCrawledAtUtc = pageCount == 0 ? null : await _mongo.GetLastCrawledTimeAsync(runId, ct);
        return Ok(new { pageCount, lastCrawledAtUtc });
    }

    [HttpPost("batch")]
    public async Task<ActionResult<object>> CreateBatch(
        [FromBody] CreateGeekCrawlerPageBatchCommand command,
        CancellationToken ct)
    {
        if (command is null || command.RunId == Guid.Empty || command.Pages is null || command.Pages.Count == 0)
            return BadRequest("runId and pages are required");

        var now = DateTimeOffset.UtcNow;
        var pagesToInsert = new List<GeekCrawlerPage>(command.Pages.Count);

        foreach (var p in command.Pages)
        {
            var id = Guid.NewGuid();
            pagesToInsert.Add(new GeekCrawlerPage
            {
                Id = id,
                RunId = command.RunId,
                Origin = p.Origin ?? "",
                Url = p.Url ?? "",
                FinalUrl = p.FinalUrl ?? p.Url ?? "",
                StatusCode = p.StatusCode,
                RobotsAllowed = p.RobotsAllowed,
                Html = p.Html,
                FailureReason = TruncateFailureReason(p.FailureReason),
                CrawledAtUtc = now,
            });
        }

        var created = await _mongo.CreatePagesBatchAsync(command.RunId, pagesToInsert, ct);
        var result = created.Select(x => new CreatedGeekCrawlerPageItem(x.Url, x.PageId)).ToList();
        return Ok(new { count = result.Count, pages = result });
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
        string? Html,
        string? FailureReason = null);

    private static string? TruncateFailureReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Length <= 512 ? reason : reason[..512];

    public record CreatedGeekCrawlerPageItem(string Url, Guid PageId);

}
