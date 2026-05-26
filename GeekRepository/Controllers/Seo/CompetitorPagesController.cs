using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/competitor-pages")]
public sealed class CompetitorPagesController(ICompetitorPageRepository pages) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid serpResultId, CancellationToken ct)
    {
        var result = await pages.GetBySerpResultAsync(serpResultId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] CompetitorPageUpsertRequest body, CancellationToken ct)
    {
        var result = await pages.UpsertAsync(body.SerpResultId, body.Page, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}

public sealed record CompetitorPageUpsertRequest
{
    public required Guid SerpResultId { get; init; }
    public required PageContent Page { get; init; }
}
