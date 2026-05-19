using GeekAPI.Auth;
using GeekAPI.Extensions;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Seo;

[ApiController]
[Route("api/seo/keywords")]
public sealed class KeywordsController(IKeywordResearchService keywords, ICurrentUserContext user) : ControllerBase
{
    [HttpPost("research")]
    public async Task<IActionResult> Research([FromBody] KeywordResearchRequest request, CancellationToken ct)
    {
        var result = await keywords.ResearchAsync(user.RequireUserId(), request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("cluster")]
    public async Task<IActionResult> Cluster([FromBody] ClusterKeywordsRequest request, CancellationToken ct)
    {
        var result = await keywords.ClusterAsync(user.RequireUserId(), request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("project/{projectId:guid}")]
    public async Task<IActionResult> GetProjectKeywords(Guid projectId, CancellationToken ct)
    {
        var result = await keywords.GetProjectKeywordsAsync(user.RequireUserId(), projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
