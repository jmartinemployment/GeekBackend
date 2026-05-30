using GeekSeo.Application.Interfaces.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/wordpress/publish-log")]
public sealed class WordPressPublishController(IWordPressPublishRepository publish) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Record([FromBody] WordPressPublishLogRequest body, CancellationToken ct)
    {
        var result = await publish.RecordPublishAsync(
            body.ProjectId,
            body.DocumentId,
            body.TargetKeyword,
            body.WordCount,
            body.Title,
            body.PublishedUrl,
            body.WordPressPostId,
            ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }
}

public sealed record WordPressPublishLogRequest
{
    public required Guid ProjectId { get; init; }
    public required Guid DocumentId { get; init; }
    public required string TargetKeyword { get; init; }
    public required int WordCount { get; init; }
    public required string Title { get; init; }
    public required string PublishedUrl { get; init; }
    public required int WordPressPostId { get; init; }
}
