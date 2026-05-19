using GeekAPI.Auth;
using GeekAPI.Extensions;
using GeekApplication.Interfaces.Seo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Seo;

[ApiController]
[Route("api/seo/jobs")]
public sealed class JobsController(IBackgroundJobService jobs, ICurrentUserContext user) : ControllerBase
{
    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> GetStatus(Guid jobId, CancellationToken ct)
    {
        var result = await jobs.GetJobAsync(user.RequireUserId(), jobId, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(result.Error);
        return Ok(result.Value);
    }
}
