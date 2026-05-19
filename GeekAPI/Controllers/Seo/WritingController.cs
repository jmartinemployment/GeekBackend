using GeekAPI.Auth;
using GeekAPI.Extensions;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Seo;

[ApiController]
[Route("api/seo/writing")]
public sealed class WritingController(IAIWritingService writing, ICurrentUserContext user) : ControllerBase
{
    [HttpPost("full-article")]
    public async Task<IActionResult> FullArticle([FromBody] FullArticleRequest request, CancellationToken ct)
    {
        var result = await writing.EnqueueFullArticleAsync(user.RequireUserId(), request, ct);
        return result.IsSuccess ? Accepted(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("outline")]
    public async Task<IActionResult> Outline([FromBody] WritingOutlineRequest request, CancellationToken ct)
    {
        var result = await writing.GenerateOutlineAsync(user.RequireUserId(), request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("draft")]
    public async Task<IActionResult> Draft([FromBody] WritingDraftRequest request, CancellationToken ct)
    {
        var result = await writing.GenerateDraftAsync(user.RequireUserId(), request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("humanize")]
    public async Task<IActionResult> Humanize([FromBody] HumanizeRequest request, CancellationToken ct)
    {
        var result = await writing.HumanizeAsync(user.RequireUserId(), request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("detect")]
    public async Task<IActionResult> Detect([FromBody] DetectAiRequest request, CancellationToken ct)
    {
        var result = await writing.DetectAsync(user.RequireUserId(), request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
