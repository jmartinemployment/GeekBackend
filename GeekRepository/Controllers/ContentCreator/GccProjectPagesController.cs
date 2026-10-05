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

    [HttpPost("merge-duplicate-drafts")]
    public async Task<ActionResult<GccDraftMergeResult>> MergeDuplicateDrafts(Guid projectId, CancellationToken ct)
    {
        var result = await _pages.MergeDuplicateDraftsAsync(projectId, ct);
        return result.ProjectNotFound ? NotFound() : Ok(result);
    }
}
