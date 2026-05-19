using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/writing")]
public sealed class WritingController(IAIWritingService writing) : ControllerBase
{
    [HttpPost("full-article")]
    public async Task<IActionResult> FullArticle(
        [FromQuery] Guid userId,
        [FromBody] FullArticleRequest request,
        CancellationToken ct)
    {
        var result = await writing.EnqueueFullArticleAsync(userId, request, ct);
        return result.IsSuccess ? Accepted(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("outline")]
    public async Task<IActionResult> Outline(
        [FromQuery] Guid userId,
        [FromBody] WritingOutlineRequest request,
        CancellationToken ct)
    {
        var result = await writing.GenerateOutlineAsync(userId, request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("draft")]
    public async Task<IActionResult> Draft(
        [FromQuery] Guid userId,
        [FromBody] WritingDraftRequest request,
        CancellationToken ct)
    {
        var result = await writing.GenerateDraftAsync(userId, request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("humanize")]
    public async Task<IActionResult> Humanize(
        [FromQuery] Guid userId,
        [FromBody] HumanizeRequest request,
        CancellationToken ct)
    {
        var result = await writing.HumanizeAsync(userId, request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("detect")]
    public async Task<IActionResult> Detect(
        [FromQuery] Guid userId,
        [FromBody] DetectAiRequest request,
        CancellationToken ct)
    {
        var result = await writing.DetectAsync(userId, request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
