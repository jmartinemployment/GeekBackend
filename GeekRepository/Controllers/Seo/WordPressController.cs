using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/wordpress")]
public sealed class WordPressController(IWordPressPublishService wordpress) : ControllerBase
{
    [HttpPost("connect")]
    public async Task<IActionResult> Connect(
        [FromQuery] Guid userId,
        [FromQuery] Guid projectId,
        [FromBody] WordPressConnectRequest request,
        CancellationToken ct)
    {
        var result = await wordpress.ConnectAsync(userId, projectId, request, ct);
        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }

    [HttpPost("publish")]
    public async Task<IActionResult> Publish(
        [FromQuery] Guid userId,
        [FromQuery] Guid documentId,
        [FromBody] WordPressPublishOptions options,
        CancellationToken ct)
    {
        var result = await wordpress.PublishDocumentAsync(userId, documentId, options, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpDelete("{projectId:guid}")]
    public async Task<IActionResult> Disconnect(
        Guid projectId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var result = await wordpress.DisconnectAsync(userId, projectId, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("{projectId:guid}/status")]
    public async Task<IActionResult> Status(
        Guid projectId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var result = await wordpress.GetStatusAsync(userId, projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
