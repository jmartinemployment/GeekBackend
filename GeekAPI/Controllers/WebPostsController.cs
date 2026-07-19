using GeekApplication.Interfaces;
using GeekApplication.Models.WebPost;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers;

/// <summary>
/// WebPost content API — protected by X-API-Key (ApiKeyMiddleware), same scheme as api/blog writes.
/// </summary>
[ApiController]
[Route("api/webposts")]
public sealed class WebPostsController : ControllerBase
{
    private readonly IWebPostRepository _webPosts;

    public WebPostsController(IWebPostRepository webPosts) => _webPosts = webPosts;

    [HttpGet("{slug}")]
    public async Task<ActionResult<WebPostFlatDto>> GetBySlug(string slug, CancellationToken ct)
    {
        var post = await _webPosts.GetBySlugAsync(slug, ct);
        return post is null ? NotFound() : Ok(post);
    }

    [HttpPost]
    public async Task<ActionResult<WebPostFlatDto>> Upsert([FromBody] UpsertWebPostCommand command, CancellationToken ct)
    {
        var post = await _webPosts.UpsertAsync(command, ct);
        return Ok(post);
    }

    [HttpDelete("{slug}")]
    public async Task<IActionResult> Delete(string slug, CancellationToken ct)
    {
        return await _webPosts.DeleteAsync(slug, ct) ? NoContent() : NotFound();
    }
}
