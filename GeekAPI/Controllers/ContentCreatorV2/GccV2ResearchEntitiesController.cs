using GeekAPI.Auth;
using GeekAPI.HttpClients;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>
/// Canonical partner/competitor entities shared by the RAG writer (<c>/rag</c>) and every
/// task agent — see plans/make-content-creator-workable.md Milestone 1. Replaces free-text
/// entity names and the hardcoded Phase-0 <c>RagEntitySeedList</c>.
/// </summary>
[ApiController]
[Route("api/geek-content-creator-v2/research-entities")]
public sealed class GccV2ResearchEntitiesController(
    ICurrentUserContext user, HttpGccV2Repository repo) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2ResearchEntityDto>>> List(
        [FromQuery] string? role, [FromQuery] bool includeArchived, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        return Ok(await repo.ListResearchEntitiesAsync(role, includeArchived, ct));
    }

    [HttpPost]
    public async Task<ActionResult<GccV2ResearchEntityDto>> Create(
        [FromBody] CreateResearchEntityRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var created = await repo.CreateResearchEntityAsync(
            new(request.Name, request.Role, request.PrimaryUrl, request.Notes, user.UserId.ToString("D")), ct);
        return Ok(created);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GccV2ResearchEntityDto>> Update(
        Guid id, [FromBody] UpdateGccV2ResearchEntityCommand command, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        return Ok(await repo.UpdateResearchEntityAsync(id, command, ct));
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<ActionResult<GccV2ResearchEntityDto>> Archive(Guid id, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        return Ok(await repo.ArchiveResearchEntityAsync(id, ct));
    }

    public sealed record CreateResearchEntityRequest(string Name, string Role, string? PrimaryUrl, string? Notes);
}
