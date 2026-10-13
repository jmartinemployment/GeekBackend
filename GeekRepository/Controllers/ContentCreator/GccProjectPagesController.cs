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
    /// <remarks>
    /// <c>name</c>, when sent, narrows the delete to the pages of those types with one of these names:
    /// a run for some of a project's tool pages deletes those and leaves the others. Until 2026-10-12
    /// there was no such narrowing, and a run for one tool page deleted every tool page of the project
    /// before it wrote its one. A <c>name</c> that is sent blank is a bad request, never a delete of
    /// every page of the type.
    /// </remarks>
    [HttpDelete("pages")]
    public async Task<ActionResult<GccPagesDeleteResult>> DeletePages(
        Guid projectId,
        [FromQuery(Name = "type")] string[] types,
        [FromQuery(Name = "name")] string[] names,
        CancellationToken ct)
    {
        if (types is null || types.Length == 0 || types.All(string.IsNullOrWhiteSpace))
            return BadRequest("Name the types of page to delete; nothing is deleted without them.");

        var named = names is { Length: > 0 };
        if (named && names!.Any(string.IsNullOrWhiteSpace))
            return BadRequest("A page was named with a blank name; nothing is deleted on a name that is not there.");

        var result = await _pages.DeletePagesAsync(projectId, types, named ? names : null, ct);
        return result.ProjectNotFound ? NotFound() : Ok(result);
    }

    /// <summary>
    /// The pieces of the named types written from one page, deleted with their content. What a Repurpose
    /// does first. 404 when the project has no such page; no types is a bad request, not a delete of
    /// every piece.
    /// </summary>
    [HttpDelete("pages/{pageId:guid}/pieces")]
    public async Task<ActionResult<GccPiecesDeleteResult>> DeletePieces(
        Guid projectId,
        Guid pageId,
        [FromQuery(Name = "type")] string[] types,
        CancellationToken ct)
    {
        if (types is null || types.Length == 0 || types.Any(string.IsNullOrWhiteSpace))
            return BadRequest("Name the types of piece to delete; nothing is deleted without them.");

        var result = await _pages.DeletePiecesAsync(projectId, pageId, types, ct);
        return result.PageNotFound ? NotFound() : Ok(result);
    }
}
