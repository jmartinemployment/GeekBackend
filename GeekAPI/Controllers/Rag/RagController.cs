using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Auth;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Rag;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
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
    private readonly IGeekCrawlerRagClient _rag;
    private readonly HttpGeekCrawlerRepository _crawlerRepo;

    public RagController(
        ICurrentUserContext user,
        GccV2CreateLibraryWriter generate,
        IGeekCrawlerRagClient rag,
        HttpGeekCrawlerRepository crawlerRepo)
    {
        _user = user;
        _generate = generate;
        _rag = rag;
        _crawlerRepo = crawlerRepo;
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

        var results = await _rag.HostsIndexedAsync(request.Urls, ct).ConfigureAwait(false);

        // An empty result means the check did not run, not that nothing is indexed. Returning
        // "not indexed" for every URL would block creates on an answer never obtained.
        if (results.Count == 0)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "The index could not be reached, so no URL could be checked." });
        }

        // Indexed is not usable. A crawl can complete having been blocked at its first page, or
        // against a site that renders nothing without JavaScript, and still put a row in the index:
        // that passes "does an index exist" and gives a writer nothing. The run records what
        // actually landed, so the same answer carries it -- one question, one answer, and the form
        // and the project gate read the same one rather than each deciding for itself.
        var answers = new List<object>(results.Count);
        foreach (var row in results)
        {
            GeekCrawlerRunDto? run = null;
            if (row.Indexed && Guid.TryParse(row.RunId, out var runId))
                run = await _crawlerRepo.GetRunAsync(runId, ct).ConfigureAwait(false);

            var reason = GccDeclaredUrlEvidence.Unusable(row, run);
            answers.Add(new
            {
                url = row.Url,
                host = row.Host,
                indexed = row.Indexed,
                runId = row.RunId,
                usable = reason is null,
                reason,
                pages = run?.RagPagesEnglish,
                chunks = run?.RagChunksUpserted,
            });
        }

        return Ok(new { results = answers });
    }

    public sealed record HostsIndexedRequest(IReadOnlyList<string>? Urls);

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
