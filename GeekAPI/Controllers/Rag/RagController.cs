using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Auth;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Rag;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.GeekCrawler;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Rag;

/// <summary>
/// RAG evidence library for Content Creator v2: status, entity catalog, template index.
/// Drafting uses <see cref="GccV2CreateLibraryWriter"/> with <c>CreateLibraryDraft</c> (query + pages only).
/// </summary>
[ApiController]
[Route("api/rag")]
public sealed class RagController : ControllerBase
{
    private readonly ICurrentUserContext _user;
    private readonly GccV2CreateLibraryWriter _generate;
    private readonly GccDeclaredUrlValidator _declaredUrls;

    public RagController(
        ICurrentUserContext user,
        GccV2CreateLibraryWriter generate,
        GccDeclaredUrlValidator declaredUrls)
    {
        _user = user;
        _generate = generate;
        _declaredUrls = declaredUrls;
    }

    [HttpGet("health")]
    public ActionResult<object> Health()
    {
        var status = _generate.GetStatus();
        return Ok(new
        {
            ok = true,
            product = "geek-rag-library",
            available = status.RagClientEnabled,
            userId = _user.IsAuthenticated ? _user.UserId.ToString("D") : null,
        });
    }

    /// <summary>Library availability + intent/entity catalogs for the phi UI.</summary>
    [HttpGet("status")]
    public ActionResult<CreateLibraryStatusDto> Status()
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        return Ok(_generate.GetStatus());
    }

    [HttpGet("entities")]
    public ActionResult<object> Entities()
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        return Ok(new { entities = RagEntitySeedList.Names });
    }

    /// <summary>
    /// Whether an index exists for each entered URL — the one question that decides whether a create
    /// can use it.
    ///
    /// Asked of the index, never of crawl_runs: a crawl can complete with pages in Mongo and nothing
    /// indexed, and pages that were never indexed cannot be cited. A URL that will not parse has no
    /// host and so reports no index, which is why nothing here checks syntax separately.
    ///
    /// Lives on the RAG controller because it is a RAG question. Neither Create nor Geek-Crawler
    /// answers it.
    /// </summary>
    [HttpPost("hosts-indexed")]
    public async Task<IActionResult> HostsIndexed(
        [FromBody] HostsIndexedRequest? request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (request?.Urls is null || request.Urls.Count == 0)
            return BadRequest(new { error = "urls required" });

        string? crawlType = null;
        if (!string.IsNullOrWhiteSpace(request.CrawlType))
        {
            crawlType = request.CrawlType.Trim().ToLowerInvariant();
            if (crawlType is not (CrawlTypes.ProjectSite or CrawlTypes.Partner or CrawlTypes.Competitors))
            {
                return BadRequest(new
                {
                    error = $"crawlType must be one of: {CrawlTypes.ProjectSite}, {CrawlTypes.Partner}, {CrawlTypes.Competitors}.",
                });
            }
        }

        var urls = request.Urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(u => u, _ => crawlType, StringComparer.OrdinalIgnoreCase);
        if (urls.Count == 0)
            return BadRequest(new { error = "urls required" });

        // One answer per URL, from the same check the Profile save and Generate run
        // (GccDeclaredUrlValidator). This route used to decide for itself, from the crawl's own page
        // and chunk counts -- so the form showed green for a URL whose crawl recorded writing pages
        // the index does not hold, and the save, which searches the index, refused it.
        var answered = await _declaredUrls.AnswerAsync(urls, ct).ConfigureAwait(false);

        // Not asked is not "nothing is indexed". Returning "not indexed" for every URL would block
        // creates on an answer never obtained.
        if (answered.Unreachable is { } unreachable)
            return StatusCode(StatusCodes.Status502BadGateway, new { error = unreachable });

        return Ok(new
        {
            results = answered.Answers.Select(a => new
            {
                url = a.Url,
                host = a.Host,
                indexed = a.Indexed,
                runId = a.RunId,
                usable = a.Usable,
                reason = a.Reason,
                pages = a.Pages,
                chunks = a.Chunks,
                // What tells "40 pages, the site is small" from "40 pages, 460 were error bodies".
                skippedUnusable = a.SkippedUnusable,
            }),
        });
    }

    /// <param name="CrawlType">The list the URLs are entered in: project-site, partner or competitors.
    /// Generate searches each URL's crawl as that kind, so the check does too when it is sent. Absent,
    /// the crawl is searched without that filter.</param>
    public sealed record HostsIndexedRequest(IReadOnlyList<string>? Urls, string? CrawlType = null);

    /// <summary>Phase D2 — upsert ad templates into Geek-Crawler-Rag (owned by content-creator-v2).</summary>
    [HttpPost("templates")]
    public async Task<IActionResult> IndexTemplates(
        [FromBody] List<RagAdTemplateDto>? templates,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (templates is null || templates.Count == 0)
            return BadRequest(new { error = "templates required" });

        var result = await _generate.IndexAdTemplatesAsync(templates, ct).ConfigureAwait(false);
        return Ok(result);
    }
}
