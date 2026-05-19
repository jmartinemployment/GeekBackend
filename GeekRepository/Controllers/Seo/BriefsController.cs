using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/briefs")]
public sealed class BriefsController(IContentBriefService briefs) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<IActionResult> Generate(
        [FromQuery] Guid userId,
        [FromBody] GenerateBriefRequest request,
        CancellationToken ct)
    {
        var result = await briefs.GenerateBriefAsync(userId, request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
