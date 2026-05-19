using GeekApplication.Interfaces.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/jobs")]
public sealed class JobsController(IBackgroundJobService jobs) : ControllerBase
{
    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> GetStatus(Guid jobId, [FromQuery] Guid userId, CancellationToken ct)
    {
        var result = await jobs.GetJobAsync(userId, jobId, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(result.Error);
        return Ok(result.Value);
    }
}
