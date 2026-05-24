using GeekApplication.Dtos;
using GeekRepository.Dtos;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Auth;

[ApiController]
[Route("repo/auth/tokens")]
public class AuthTokensController : ControllerBase
{
    private readonly IOAuthTokenRepository _repo;

    public AuthTokensController(IOAuthTokenRepository repo) => _repo = repo;

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

    [HttpPost("{jti}/revoke")]
    public async Task<IActionResult> Revoke(string jti, [FromBody] RevokeTokenRequest req) =>
        ToResponse(await _repo.RevokeTokenAsync(jti, req.Reason));

    [HttpGet("{jti}/blacklisted")]
    public async Task<IActionResult> IsBlacklisted(string jti) =>
        ToResponse(await _repo.IsTokenBlacklistedAsync(jti));

    [HttpPost("blacklist")]
    public async Task<IActionResult> AddToBlacklist([FromBody] BlacklistTokenRequest req) =>
        ToResponse(await _repo.AddToBlacklistAsync(req.Jti, req.UserId, req.ExpiresAt, req.Reason));

    [HttpPost("blacklist/cleanup")]
    public async Task<IActionResult> CleanupBlacklist() =>
        ToResponse(await _repo.CleanupExpiredBlacklistEntriesAsync());

    private IActionResult ToResponse<T>(Result<T> result) => result.Status switch
    {
        ResultStatus.Ok => Ok(new { success = true, data = result.Value }),
        ResultStatus.NotFound => NotFound(new { success = false, error = new { code = "NOT_FOUND", message = result.Error } }),
        ResultStatus.Failure => StatusCode(500, new { success = false, error = new { code = "FAILURE", message = result.Error } }),
        _ => StatusCode(500, new { success = false, error = new { code = "UNKNOWN_ERROR", message = "An unknown error occurred" } })
    };
}
