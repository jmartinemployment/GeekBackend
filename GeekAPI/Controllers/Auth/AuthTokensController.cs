using GeekRepository.Repositories;
using GeekRepository.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Auth;

[ApiController]
[Route("api/auth/tokens")]
public class AuthTokensController : ControllerBase
{
    private readonly IOAuthTokenRepository _repo;

    public AuthTokensController(IOAuthTokenRepository repo)
    {
        _repo = repo;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOAuthTokenRequest req)
    {
        var result = await _repo.CreateAsync(req);
        return ToResponse(result);
    }

    [HttpGet("by-access/{token}")]
    public async Task<IActionResult> FindByAccessToken(string token)
    {
        var result = await _repo.FindByAccessTokenAsync(token);
        return ToResponse(result);
    }

    [HttpGet("by-refresh/{token}")]
    public async Task<IActionResult> FindByRefreshToken(string token)
    {
        var result = await _repo.FindByRefreshTokenAsync(token);
        return ToResponse(result);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateOAuthTokenRequest req)
    {
        var result = await _repo.UpdateAsync(id, req);
        return ToResponse(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _repo.DeleteAsync(id);
        return ToResponse(result);
    }

    private IActionResult ToResponse<T>(Result<T> result)
    {
        return result.Status switch
        {
            ResultStatus.Ok => Ok(new { success = true, data = result.Value }),
            ResultStatus.NotFound => NotFound(new { success = false, error = new { code = "NOT_FOUND", message = result.Error } }),
            ResultStatus.Failure => StatusCode(500, new { success = false, error = new { code = "FAILURE", message = result.Error } }),
            _ => StatusCode(500, new { success = false, error = new { code = "UNKNOWN_ERROR", message = "An unknown error occurred" } })
        };
    }
}
