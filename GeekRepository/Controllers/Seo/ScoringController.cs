using GeekApplication.Interfaces.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/scoring")]
public sealed class ScoringController(IContentScoringService scoring) : ControllerBase
{
    [HttpPost("process")]
    public async Task<IActionResult> Process(
        [FromQuery] Guid userId,
        [FromBody] ProcessScoringRequest body,
        CancellationToken ct)
    {
        var result = await scoring.ProcessContentChangedAsync(
            userId, body.DocumentId, body.ContentHtml, body.TargetKeyword, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        return Ok(result.Value);
    }

    [HttpPost("keyword-changed")]
    public async Task<IActionResult> KeywordChanged(
        [FromQuery] Guid userId,
        [FromBody] KeywordChangedScoringRequest body,
        CancellationToken ct)
    {
        var result = await scoring.ProcessKeywordChangedAsync(
            userId,
            body.DocumentId,
            body.ContentHtml,
            body.TargetKeyword,
            body.TargetLocation,
            ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        return Ok(result.Value);
    }
}

public sealed record ProcessScoringRequest
{
    public required Guid DocumentId { get; init; }
    public required string ContentHtml { get; init; }
    public string TargetKeyword { get; init; } = string.Empty;
}

public sealed record KeywordChangedScoringRequest
{
    public required Guid DocumentId { get; init; }
    public required string ContentHtml { get; init; }
    public required string TargetKeyword { get; init; }
    public string TargetLocation { get; init; } = "United States";
}
