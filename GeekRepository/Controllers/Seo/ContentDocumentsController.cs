using GeekSeo.Application.Interfaces;
using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/content")]
public sealed class ContentDocumentsController(IContentDocumentService content) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid userId, [FromQuery] Guid projectId, CancellationToken ct)
    {
        var result = await content.ListByProjectAsync(userId, projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromQuery] Guid userId, CancellationToken ct)
    {
        var result = await content.GetAsync(userId, id, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(result.Error);
        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromQuery] Guid userId, [FromBody] CreateContentDocumentRequest request, CancellationToken ct)
    {
        var result = await content.CreateAsync(userId, request, ct);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id, userId }, result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{id:guid}/content")]
    public async Task<IActionResult> UpdateContent(
        Guid id, [FromQuery] Guid userId, [FromBody] UpdateContentRequest request, CancellationToken ct)
    {
        var result = await content.UpdateContentAsync(userId, id, request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromQuery] Guid userId, [FromBody] UpdateDocumentStatusRequest body, CancellationToken ct)
    {
        var result = await content.UpdateStatusAsync(userId, id, body.Status, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPatch("{id:guid}/url-research")]
    public async Task<IActionResult> AttachUrlResearch(
        Guid id, [FromQuery] Guid userId, [FromBody] AttachUrlResearchRequest request, CancellationToken ct)
    {
        var result = await content.AttachUrlResearchAsync(userId, id, request.UrlResearchId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPatch("{id:guid}/analysis-run")]
    public async Task<IActionResult> AttachAnalysisRun(
        Guid id, [FromQuery] Guid userId, [FromBody] AttachAnalysisRunPersistenceRequest request, CancellationToken ct)
    {
        var access = await content.EnsureAccessAsync(userId, id, ct);
        if (!access.IsSuccess)
            return access.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(access.Error);

        var repo = HttpContext.RequestServices.GetRequiredService<IContentDocumentRepository>();
        var result = await repo.AttachAnalysisRunAsync(
            id,
            request.AnalysisRunId,
            request.TargetKeyword,
            request.SerpKeyword,
            request.SiteProfileId,
            ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPatch("{id:guid}/featured-image")]
    public async Task<IActionResult> UpdateFeaturedImage(
        Guid id, [FromQuery] Guid userId, [FromBody] UpdateFeaturedImageRequest request, CancellationToken ct)
    {
        var access = await content.EnsureAccessAsync(userId, id, ct);
        if (!access.IsSuccess)
            return access.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(access.Error);

        var repo = HttpContext.RequestServices.GetRequiredService<IContentDocumentRepository>();
        var result = await repo.UpdateFeaturedImageAsync(id, request.FeaturedImageUrl, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{id:guid}/score")]
    public async Task<IActionResult> UpdateScore(
        Guid id,
        [FromQuery] Guid userId,
        [FromBody] UpdateDocumentScoreRequest body,
        CancellationToken ct)
    {
        var access = await content.EnsureAccessAsync(userId, id, ct);
        if (!access.IsSuccess)
            return access.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(access.Error);

        var repo = HttpContext.RequestServices.GetRequiredService<IContentDocumentRepository>();
        var result = await repo.UpdateScoreAsync(id, body.Score, body.ScoreComponentsJson, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid userId, CancellationToken ct)
    {
        var result = await content.DeleteAsync(userId, id, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }
}

public sealed record UpdateDocumentStatusRequest
{
    public required string Status { get; init; }
}

public sealed record UpdateDocumentScoreRequest
{
    public required int Score { get; init; }
    public required string ScoreComponentsJson { get; init; }
}

public sealed record UpdateFeaturedImageRequest
{
    public required string FeaturedImageUrl { get; init; }
}

public sealed record AttachUrlResearchRequest
{
    public required Guid UrlResearchId { get; init; }
}

public sealed record AttachAnalysisRunPersistenceRequest
{
    public required Guid AnalysisRunId { get; init; }
    public required string TargetKeyword { get; init; }
    public required string SerpKeyword { get; init; }
    public required Guid SiteProfileId { get; init; }
}
