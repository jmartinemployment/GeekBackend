using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/wordpress/connections")]
public sealed class WordPressConnectionsController(IWordPressConnectionRepository connections) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid projectId, CancellationToken ct)
    {
        var result = await connections.GetByProjectAsync(projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] SeoWordPressConnection body, CancellationToken ct)
    {
        var result = await connections.UpsertAsync(body, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] Guid projectId, CancellationToken ct)
    {
        var result = await connections.DeleteByProjectAsync(projectId, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }
}
