using GeekRepository.Auth;
using GeekRepository.Data.Entities.GeekCrawler;
using GeekRepository.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.GeekCrawler;

[ApiController]
[Route("repo/geek-crawler/links")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GeekCrawlerLinksController : ControllerBase
{
    private readonly IMongoGeekCrawlerService _mongo;

    public GeekCrawlerLinksController(IMongoGeekCrawlerService mongo) => _mongo = mongo;

    [HttpGet("activity")]
    public async Task<ActionResult<object>> GetRunActivity(
        [FromQuery] Guid runId,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");

        var linkCount = await _mongo.CountLinksByRunAsync(runId, ct);
        return Ok(new { linkCount });
    }

    [HttpGet("for-resume")]
    public async Task<ActionResult<IReadOnlyList<GeekCrawlerLinkResumeRow>>> ListForResume(
        [FromQuery] Guid runId,
        [FromQuery] int limit = 500,
        [FromQuery] DateTimeOffset? afterDiscoveredAtUtc = null,
        [FromQuery] Guid? afterId = null,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");

        limit = Math.Clamp(limit, 1, 500);
        var rows = await _mongo.ListLinksByRunForResumeAsync(runId, limit, afterDiscoveredAtUtc, afterId, ct);
        return Ok(rows);
    }

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

        var links = await _mongo.ListLinksByRunAsync(runId, sameOrigin, limit, offset, ct);
        return Ok(links);
    }

    [HttpPost("batch")]
    public async Task<ActionResult<object>> CreateBatch(
        [FromBody] CreateGeekCrawlerLinkBatchCommand command,
        CancellationToken ct)
    {
        if (command is null || command.RunId == Guid.Empty || command.Links is null || command.Links.Count == 0)
            return BadRequest("runId and links are required");

        var linkTuples = command.Links.Select(l => (l.PageId, l.FromUrl, l.LinkUrl, l.IsSameOrigin)).ToList();
        var inserted = await _mongo.InsertLinksIgnoringDuplicatesAsync(command.RunId, linkTuples, ct);
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
