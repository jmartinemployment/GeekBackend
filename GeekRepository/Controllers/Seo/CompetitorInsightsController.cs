using GeekApplication.Interfaces.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/content")]
public sealed class CompetitorInsightsController(ICompetitorInsightsService insights) : ControllerBase
{
    [HttpGet("{documentId:guid}/competitors")]
    public async Task<IActionResult> GetForDocument(
        Guid documentId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var result = await insights.GetForDocumentAsync(userId, documentId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("{documentId:guid}/competitors/crawl")]
    public async Task<IActionResult> RefreshCrawl(
        Guid documentId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var result = await insights.RefreshCrawlForDocumentAsync(userId, documentId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
