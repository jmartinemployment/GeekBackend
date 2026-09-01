using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.GeekCrawler;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.GeekCrawler;

[ApiController]
[Route("api/geek-crawler")]
public class GeekCrawlerController : ControllerBase
{
    private readonly ICurrentUserContext _user;
    private readonly HttpGeekCrawlerRepository _repo;
    private readonly GeekCrawlerService _crawler;

    public GeekCrawlerController(
        ICurrentUserContext user,
        HttpGeekCrawlerRepository repo,
        GeekCrawlerService crawler)
    {
        _user = user;
        _repo = repo;
        _crawler = crawler;
    }

    [HttpGet("health")]
    public ActionResult<object> Health() =>
        Ok(new
        {
            ok = true,
            product = "geek-crawler",
            userId = _user.IsAuthenticated ? _user.UserId.ToString("D") : null,
        });

    [HttpPost("crawls")]
    public async Task<IActionResult> StartCrawl(
        [FromBody] StartGeekCrawlerRequest request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (request is null || !CrawlTypes.IsValid(request.CrawlType))
            return BadRequest("crawlType must be one of: competitors, partner, local.");

        var validationError = GeekCrawlerSeedNormalizer.ValidateRawSeeds(request.Seeds);
        if (validationError is not null)
            return BadRequest(validationError);

        var seeds = GeekCrawlerSeedNormalizer.NormalizeSeeds(request.Seeds);
        if (seeds.Count == 0)
            return BadRequest("At least one valid seed URL is required.");

        try
        {
            var run = await _crawler.StartCrawlAsync(
                _user.UserId.ToString("D"),
                request.CrawlType.Trim(),
                seeds,
                ct).ConfigureAwait(false);
            return Ok(GeekCrawlerService.ToSnapshot(run));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("crawls/latest")]
    public async Task<IActionResult> GetLatestCrawl(
        [FromQuery] string crawlType,
        [FromQuery] string[] seeds,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (!CrawlTypes.IsValid(crawlType))
            return BadRequest("crawlType must be one of: competitors, partner, local.");

        var normalized = GeekCrawlerSeedNormalizer.NormalizeSeeds(seeds);
        if (normalized.Count == 0)
            return BadRequest("At least one valid seed URL is required.");

        var seedsJson = GeekCrawlerSeedNormalizer.SerializeSeeds(normalized);
        var run = await _repo.GetLatestRunAsync(
            _user.UserId.ToString("D"),
            crawlType.Trim(),
            seedsJson,
            ct).ConfigureAwait(false);

        return run is null ? NotFound() : Ok(GeekCrawlerService.ToSnapshot(run));
    }

    [HttpPost("crawls/{runId:guid}/rebuild-links")]
    public async Task<IActionResult> RebuildLinks(Guid runId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (!await OwnsRunAsync(runId, ct)) return NotFound();

        var count = await _crawler.RebuildLinksAsync(runId, ct).ConfigureAwait(false);
        return Ok(new { linksRebuilt = count });
    }

    [HttpGet("crawls/{runId:guid}")]
    public async Task<IActionResult> GetRun(Guid runId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var run = await _repo.GetRunAsync(runId, ct).ConfigureAwait(false);
        if (run is null) return NotFound();
        if (!string.Equals(run.OwnerUserId, _user.UserId.ToString("D"), StringComparison.Ordinal))
            return NotFound();
        return Ok(GeekCrawlerService.ToSnapshot(run));
    }

    [HttpGet("crawls/{runId:guid}/pages")]
    public async Task<IActionResult> ListPages(
        Guid runId,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (!await OwnsRunAsync(runId, ct)) return NotFound();

        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(0, offset);
        var pages = await _repo.ListPagesAsync(runId, limit, offset, ct).ConfigureAwait(false);
        return Ok(pages);
    }

    [HttpGet("crawls/{runId:guid}/links")]
    public async Task<IActionResult> ListLinks(
        Guid runId,
        [FromQuery] bool? sameOrigin = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (!await OwnsRunAsync(runId, ct)) return NotFound();

        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(0, offset);
        var links = await _repo.ListLinksAsync(runId, sameOrigin, limit, offset, ct).ConfigureAwait(false);
        return Ok(links);
    }

    private async Task<bool> OwnsRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await _repo.GetRunAsync(runId, ct).ConfigureAwait(false);
        return run is not null
               && string.Equals(run.OwnerUserId, _user.UserId.ToString("D"), StringComparison.Ordinal);
    }

    public record StartGeekCrawlerRequest(string CrawlType, string[]? Seeds);
}
