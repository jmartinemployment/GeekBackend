using GeekAPI.Auth;
using GeekAPI.HttpClients;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>
/// Ad-copy few-shot templates shared across sessions — see
/// plans/make-content-creator-workable.md Milestone 1. Replaces the localStorage-only
/// corpus in <c>/rag</c>.
/// </summary>
[ApiController]
[Route("api/geek-content-creator-v2/ad-templates")]
public sealed class GccV2AdTemplatesController(
    ICurrentUserContext user, HttpGccV2Repository repo) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2AdTemplateDto>>> List(
        [FromQuery] bool includeArchived, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        return Ok(await repo.ListAdTemplatesAsync(includeArchived, ct));
    }

    [HttpPost]
    public async Task<ActionResult<GccV2AdTemplateDto>> Create(
        [FromBody] CreateAdTemplateRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var created = await repo.CreateAdTemplateAsync(
            new(request.Name, request.Channel, request.Framework, request.Body, user.UserId.ToString("D")), ct);
        return Ok(created);
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<ActionResult<GccV2AdTemplateDto>> Archive(Guid id, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        return Ok(await repo.ArchiveAdTemplateAsync(id, ct));
    }

    public sealed record CreateAdTemplateRequest(string Name, string? Channel, string? Framework, string Body);
}
