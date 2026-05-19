using GeekAPI.Auth;
using GeekAPI.Extensions;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Seo;

[ApiController]
[Route("api/seo/wordpress")]
public sealed class WordPressController(IWordPressPublishService wordpress, ICurrentUserContext user) : ControllerBase
{
    [HttpPost("connect")]
    public async Task<IActionResult> Connect(
        [FromQuery] Guid projectId,
        [FromBody] WordPressConnectRequest request,
        CancellationToken ct)
    {
        var result = await wordpress.ConnectAsync(user.RequireUserId(), projectId, request, ct);
        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }

    [HttpPost("publish")]
    public async Task<IActionResult> Publish(
        [FromQuery] Guid documentId,
        [FromBody] WordPressPublishOptions options,
        CancellationToken ct)
    {
        var result = await wordpress.PublishDocumentAsync(user.RequireUserId(), documentId, options, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpDelete("{projectId:guid}")]
    public async Task<IActionResult> Disconnect(Guid projectId, CancellationToken ct)
    {
        var result = await wordpress.DisconnectAsync(user.RequireUserId(), projectId, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("{projectId:guid}/status")]
    public async Task<IActionResult> Status(Guid projectId, CancellationToken ct)
    {
        var result = await wordpress.GetStatusAsync(user.RequireUserId(), projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
