using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/keywords")]
public sealed class KeywordsController(IKeywordResearchService keywords) : ControllerBase
{
    [HttpPost("research")]
    public async Task<IActionResult> Research(
        [FromQuery] Guid userId,
        [FromBody] KeywordResearchRequest request,
        CancellationToken ct)
    {
        var result = await keywords.ResearchAsync(userId, request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("cluster")]
    public async Task<IActionResult> Cluster(
        [FromQuery] Guid userId,
        [FromBody] ClusterKeywordsRequest request,
        CancellationToken ct)
    {
        var result = await keywords.ClusterAsync(userId, request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("project/{projectId:guid}")]
    public async Task<IActionResult> GetProjectKeywords(
        Guid projectId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var result = await keywords.GetProjectKeywordsAsync(userId, projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
