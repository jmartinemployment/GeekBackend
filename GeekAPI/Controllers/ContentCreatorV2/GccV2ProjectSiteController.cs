using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekAPI.Services.ContentCreatorV2.ProjectSite;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

[ApiController]
[Route("api/geek-content-creator-v2/project-site")]
public class GccV2ProjectSiteController : ControllerBase
{
    private readonly ICurrentUserContext _user;
    private readonly HttpGccV2Repository _repo;
    private readonly GccV2ProjectSiteCrawlService _crawlService;

    public GccV2ProjectSiteController(
        ICurrentUserContext user,
        HttpGccV2Repository repo,
        GccV2ProjectSiteCrawlService crawlService)
    {
        _user = user;
        _repo = repo;
        _crawlService = crawlService;
    }

    [HttpPost("crawl")]
    public async Task<ActionResult<object>> StartCrawl([FromBody] StartProjectSiteCrawlRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (request is null || string.IsNullOrWhiteSpace(request.SiteUrl))
            return BadRequest(new { error = "siteUrl is required." });

        var run = await _crawlService.StartCrawlAsync(_user.UserId.ToString("D"), request.SiteUrl, ct);
        return Accepted(new
        {
            runId = run.Id,
            siteUrl = run.SiteUrl,
            status = run.Status,
        });
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<ActionResult<object>> GetRun(Guid runId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var run = await _repo.GetProjectSiteCrawlRunAsync(runId, ct);
        if (run is null || !IsOwner(run.OwnerUserId)) return NotFound();

        var activity = await _repo.GetProjectSiteCrawlPageActivityAsync(runId, ct);
        return Ok(new
        {
            runId = run.Id,
            siteUrl = run.SiteUrl,
            status = run.Status,
            errorSummary = run.ErrorSummary,
            pageCount = activity?.PageCount ?? 0,
            createdAtUtc = run.CreatedAtUtc,
            startedAtUtc = run.StartedAtUtc,
            completedAtUtc = run.CompletedAtUtc,
        });
    }

    [HttpGet("runs/latest")]
    public async Task<ActionResult<object>> GetLatestRun([FromQuery] string siteUrl, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(siteUrl))
            return BadRequest(new { error = "siteUrl is required." });

        var normalized = GccV2ProjectSiteCrawlService.NormalizeSiteUrl(siteUrl);
        if (normalized is null)
            return BadRequest(new { error = "siteUrl is invalid." });

        var run = await _repo.GetLatestProjectSiteCrawlRunAsync(_user.UserId.ToString("D"), normalized, ct);
        if (run is null) return NotFound();

        var activity = await _repo.GetProjectSiteCrawlPageActivityAsync(run.Id, ct);
        return Ok(new
        {
            runId = run.Id,
            siteUrl = run.SiteUrl,
            status = run.Status,
            pageCount = activity?.PageCount ?? 0,
            completedAtUtc = run.CompletedAtUtc,
        });
    }

    [HttpPost("runs/{runId:guid}/cancel")]
    public async Task<ActionResult<object>> CancelCrawl(Guid runId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var run = await _repo.GetProjectSiteCrawlRunAsync(runId, ct);
        if (run is null || !IsOwner(run.OwnerUserId)) return NotFound();

        await _crawlService.CancelRunAsync(runId, ct);
        run = await _repo.GetProjectSiteCrawlRunAsync(runId, ct);
        if (run is null) return NotFound();

        return Ok(new
        {
            runId = run.Id,
            siteUrl = run.SiteUrl,
            status = run.Status,
        });
    }

    [HttpGet("runs/{runId:guid}/pages")]
    public async Task<ActionResult<object>> ListPages(Guid runId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var run = await _repo.GetProjectSiteCrawlRunAsync(runId, ct);
        if (run is null || !IsOwner(run.OwnerUserId)) return NotFound();

        var stored = await LoadAllPagesAsync(runId, ct);
        var pages = stored
            .Where(p => !string.IsNullOrWhiteSpace(p.Html))
            .Select(GccV2ProjectSitePageMapper.ToRelatedPage)
            .Select(p => new
            {
                url = p.Url,
                title = p.Title,
                headings = p.Headings,
                excerpt = p.Excerpt,
            })
            .ToList();

        return Ok(new { runId, siteUrl = run.SiteUrl, status = run.Status, pages });
    }

    [HttpGet("runs/{runId:guid}/site-hierarchy")]
    public async Task<ActionResult<object>> GetSiteHierarchy(Guid runId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var run = await _repo.GetProjectSiteCrawlRunAsync(runId, ct);
        if (run is null || !IsOwner(run.OwnerUserId)) return NotFound();

        var stored = await LoadAllPagesAsync(runId, ct);
        var hierarchy = GccV2SiteHierarchyFromCrawl.Build(run.SiteUrl, stored);

        return Ok(new { siteHierarchy = hierarchy });
    }

    private async Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> LoadAllPagesAsync(Guid runId, CancellationToken ct)
    {
        var all = new List<GccV2ProjectSiteCrawlPageDto>();
        var offset = 0;
        const int batch = 100;
        while (true)
        {
            var chunk = await _repo.ListProjectSiteCrawlPagesAsync(runId, batch, offset, ct);
            if (chunk.Count == 0) break;
            all.AddRange(chunk);
            if (chunk.Count < batch) break;
            offset += chunk.Count;
        }

        return all;
    }

    private bool IsOwner(string ownerUserId) =>
        _user.IsAuthenticated && string.Equals(ownerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase);

    public record StartProjectSiteCrawlRequest(string SiteUrl);
}
