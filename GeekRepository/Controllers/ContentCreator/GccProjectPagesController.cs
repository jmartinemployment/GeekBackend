using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

/// <summary>
/// A project's pages: where a Generate saves what it wrote. See <see cref="IGccProjectPageRepository"/>.
/// </summary>
[ApiController]
[Route("repo/content-creator/projects/{projectId:guid}")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccProjectPagesController : ControllerBase
{
    private readonly IGccProjectPageRepository _pages;

    public GccProjectPagesController(IGccProjectPageRepository pages) => _pages = pages;

    /// <summary>
    /// Every piece of one Generate, saved together. A refusal is an answer -- 200 with the reason and
    /// nothing written -- and only a project that does not exist is 404.
    /// </summary>
    [HttpPost("generated")]
    public async Task<ActionResult<GccGeneratedPiecesSaveResult>> SaveGenerated(
        Guid projectId, [FromBody] SaveGccGeneratedPiecesCommand command, CancellationToken ct)
    {
        var result = await _pages.SaveGeneratedAsync(projectId, command, ct);
        return result.ProjectNotFound ? NotFound() : Ok(result);
    }

    /// <summary>
    /// The project's pages of the named types, deleted with their content. What a Generate does first.
    /// No types is a bad request, not a delete of everything.
    /// </summary>
    [HttpDelete("pages")]
    public async Task<ActionResult<GccPagesDeleteResult>> DeletePages(
        Guid projectId, [FromQuery(Name = "type")] string[] types, CancellationToken ct)
    {
        if (types is null || types.Length == 0 || types.All(string.IsNullOrWhiteSpace))
            return BadRequest("Name the types of page to delete; nothing is deleted without them.");

        var result = await _pages.DeletePagesAsync(projectId, types, ct);
        return result.ProjectNotFound ? NotFound() : Ok(result);
    }
}
