using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Auth;
using GeekAPI.Services.Rag;
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

    public RagController(ICurrentUserContext user, GccV2CreateLibraryWriter generate)
    {
        _user = user;
        _generate = generate;
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
