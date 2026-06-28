using GeekRepository.Repositories.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/analysis-runs")]
public sealed class AnalysisRunsController(SiteAnalyzerAnalysisRunReader reader) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid projectId, [FromQuery] Guid userId, CancellationToken ct)
    {
        _ = userId;
        if (projectId == Guid.Empty)
            return BadRequest(new { error = "projectId is required." });

        var result = await reader.ListByProjectAsync(projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{id:guid}/content-writer-export")]
    public async Task<IActionResult> GetContentWriterExport(Guid id, [FromQuery] Guid userId, CancellationToken ct)
    {
        _ = userId;
        var result = await reader.GetContentWriterExportAsync(id, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                ? NotFound()
                : BadRequest(result.Error);
        return Ok(result.Value);
    }
}
