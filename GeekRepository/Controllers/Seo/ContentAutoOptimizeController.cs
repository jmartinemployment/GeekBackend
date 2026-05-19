using GeekApplication.Interfaces.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/content")]
public sealed class ContentAutoOptimizeController(IContentScoringService scoring) : ControllerBase
{
    [HttpPost("{documentId:guid}/auto-optimize")]
    public async Task<IActionResult> AutoOptimize(
        Guid documentId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var result = await scoring.AutoOptimizeAsync(userId, documentId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
