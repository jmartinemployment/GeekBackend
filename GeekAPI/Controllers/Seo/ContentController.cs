using GeekAPI.Auth;
using GeekAPI.Extensions;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Seo;

[ApiController]
[Route("api/seo/content")]
public sealed class ContentController(
    IContentDocumentService content,
    ICompetitorInsightsService competitors,
    IContentScoringService scoring,
    ICurrentUserContext user) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid projectId, CancellationToken ct)
    {
        var result = await content.ListByProjectAsync(user.RequireUserId(), projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await content.GetAsync(user.RequireUserId(), id, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(result.Error);
        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateContentDocumentRequest request, CancellationToken ct)
    {
        var result = await content.CreateAsync(user.RequireUserId(), request, ct);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{id:guid}/content")]
    public async Task<IActionResult> UpdateContent(Guid id, [FromBody] UpdateContentRequest request, CancellationToken ct)
    {
        var result = await content.UpdateContentAsync(user.RequireUserId(), id, request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateDocumentStatusBody body, CancellationToken ct)
    {
        var result = await content.UpdateStatusAsync(user.RequireUserId(), id, body.Status, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await content.DeleteAsync(user.RequireUserId(), id, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("{id:guid}/competitors")]
    public async Task<IActionResult> GetCompetitors(Guid id, CancellationToken ct)
    {
        var result = await competitors.GetForDocumentAsync(user.RequireUserId(), id, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("{id:guid}/competitors/crawl")]
    public async Task<IActionResult> RefreshCompetitorCrawl(Guid id, CancellationToken ct)
    {
        var result = await competitors.RefreshCrawlForDocumentAsync(user.RequireUserId(), id, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("{id:guid}/auto-optimize")]
    public async Task<IActionResult> AutoOptimize(Guid id, CancellationToken ct)
    {
        var result = await scoring.AutoOptimizeAsync(user.RequireUserId(), id, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}

public sealed record UpdateDocumentStatusBody
{
    public required string Status { get; init; }
}
