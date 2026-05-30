using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/jobs")]
public sealed class JobsController(IBackgroundJobService jobs, IBackgroundJobRepository jobRepo) : ControllerBase
{
    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> GetStatus(Guid jobId, [FromQuery] Guid userId, CancellationToken ct)
    {
        var result = await jobs.GetJobAsync(userId, jobId, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(result.Error);
        return Ok(result.Value);
    }

    [HttpGet("{jobId:guid}/entity")]
    public async Task<IActionResult> GetEntity(Guid jobId, [FromQuery] Guid userId, CancellationToken ct)
    {
        var result = await jobRepo.GetByIdAsync(jobId, ct);
        if (!result.IsSuccess || result.Value is null)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(result.Error);
        if (result.Value.UserId != userId)
            return Forbid();
        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBackgroundJobRequest request, CancellationToken ct)
    {
        var result = await jobRepo.CreateAsync(request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending(
        [FromQuery] string jobType,
        [FromQuery] int limit = 1,
        CancellationToken ct = default)
    {
        var result = await jobRepo.GetPendingAsync(jobType, limit, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPatch("{jobId:guid}/progress")]
    public async Task<IActionResult> UpdateProgress(
        Guid jobId,
        [FromBody] JobProgressRequest body,
        CancellationToken ct)
    {
        var result = await jobRepo.UpdateProgressAsync(jobId, body.ProgressPercent, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPatch("{jobId:guid}/complete")]
    public async Task<IActionResult> MarkComplete(
        Guid jobId,
        [FromBody] JobCompleteRequest body,
        CancellationToken ct)
    {
        var result = await jobRepo.MarkCompleteAsync(jobId, body.ResultId, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPatch("{jobId:guid}/failed")]
    public async Task<IActionResult> MarkFailed(
        Guid jobId,
        [FromBody] JobFailedRequest body,
        CancellationToken ct)
    {
        var result = await jobRepo.MarkFailedAsync(jobId, body.ErrorMessage, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }
}

public sealed record JobProgressRequest
{
    public required int ProgressPercent { get; init; }
}

public sealed record JobCompleteRequest
{
    public Guid? ResultId { get; init; }
}

public sealed record JobFailedRequest
{
    public required string ErrorMessage { get; init; }
}
