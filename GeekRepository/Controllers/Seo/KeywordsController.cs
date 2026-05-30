using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/keywords")]
public sealed class KeywordsController(IKeywordRepository keywords) : ControllerBase
{
    [HttpGet("project/{projectId:guid}")]
    public async Task<IActionResult> GetByProject(Guid projectId, CancellationToken ct)
    {
        var result = await keywords.GetByProjectAsync(projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("bulk-upsert")]
    public async Task<IActionResult> BulkUpsert(
        [FromQuery] Guid projectId,
        [FromQuery] string location,
        [FromBody] IReadOnlyList<KeywordResult> body,
        CancellationToken ct)
    {
        var result = await keywords.BulkUpsertAsync(projectId, body, location, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }
}
