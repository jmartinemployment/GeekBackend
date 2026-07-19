using GeekApplication.Interfaces;
using GeekApplication.Models.WebPost;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Content;

[ApiController]
[Route("repo/content/webposts")]
public class WebPostsController : ControllerBase
{
    private readonly IWebPostRepository _repo;

    public WebPostsController(IWebPostRepository repo) => _repo = repo;

    [HttpGet("{slug}")]
    public async Task<ActionResult<WebPostFlatDto>> GetBySlug(string slug, CancellationToken ct)
    {
        var post = await _repo.GetBySlugAsync(slug, ct);
        return post is null ? NotFound() : Ok(post);
    }

    [HttpPost]
    public async Task<ActionResult<WebPostFlatDto>> Upsert([FromBody] UpsertWebPostCommand command, CancellationToken ct)
    {
        var post = await _repo.UpsertAsync(command, ct);
        return Ok(post);
    }

    [HttpDelete("{slug}")]
    public async Task<IActionResult> Delete(string slug, CancellationToken ct)
    {
        return await _repo.DeleteAsync(slug, ct) ? NoContent() : NotFound();
    }
}
