using GeekAPI.Auth;
using GeekAPI.Services.Rag;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Rag;

/// <summary>
/// Intent-routed RAG writing for Content Creator v2 / operators.
/// Soft-disabled when Geek-Crawler-Rag is unset or <c>GEEK_RAG_GENERATE_ENABLED=false</c>.
/// </summary>
[ApiController]
[Route("api/rag")]
public sealed class RagController : ControllerBase
{
    private readonly ICurrentUserContext _user;
    private readonly RagGenerateService _generate;

    public RagController(ICurrentUserContext user, RagGenerateService generate)
    {
        _user = user;
        _generate = generate;
    }

    [HttpGet("health")]
    public ActionResult<object> Health()
    {
        var status = _generate.GetStatus();
        return Ok(new
        {
            ok = true,
            product = "geek-rag-generate",
            available = status.Available,
            userId = _user.IsAuthenticated ? _user.UserId.ToString("D") : null,
        });
    }

    /// <summary>Soft-detect availability + intent/entity catalogs for the phi UI.</summary>
    [HttpGet("status")]
    public ActionResult<RagGenerateStatusDto> Status()
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        return Ok(_generate.GetStatus());
    }

    [HttpGet("entities")]
    public ActionResult<object> Entities()
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        return Ok(new { entities = RagEntitySeedList.Names });
    }

    [HttpPost("generate")]
    public async Task<ActionResult<RagGenerateResponse>> Generate(
        [FromBody] RagGenerateRequest? request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (request is null)
            return BadRequest(new { error = "body required" });

        try
        {
            var result = await _generate.GenerateAsync(
                _user.UserId.ToString("D"),
                request,
                ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
