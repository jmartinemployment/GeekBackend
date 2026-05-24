using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Auth;

[ApiController]
[Route("repo/auth/user-secrets")]
public sealed class UserSecretsController : ControllerBase
{
    private readonly IUserSecretsRepository _repo;

    public UserSecretsController(IUserSecretsRepository repo) => _repo = repo;

    [HttpGet("{userId}/totp")]
    public async Task<IActionResult> GetTotp(string userId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
            return BadRequest(new { error = "Invalid user ID format" });
        return ToResponse(await _repo.GetTotpSecretAsync(parsedUserId, cancellationToken));
    }

    [HttpPut("{userId}/totp")]
    public async Task<IActionResult> SetTotp(string userId, [FromBody] SetTotpSecretRequest req, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
            return BadRequest(new { error = "Invalid user ID format" });
        return ToResponse(await _repo.SetTotpSecretAsync(parsedUserId, req.Secret, cancellationToken));
    }

    [HttpDelete("{userId}/totp")]
    public async Task<IActionResult> ClearTotp(string userId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
            return BadRequest(new { error = "Invalid user ID format" });
        return ToResponse(await _repo.ClearTotpSecretAsync(parsedUserId, cancellationToken));
    }

    [HttpGet("{userId}/recovery-codes")]
    public async Task<IActionResult> GetRecoveryCodes(string userId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
            return BadRequest(new { error = "Invalid user ID format" });
        return ToResponse(await _repo.GetRecoveryCodeHashesAsync(parsedUserId, cancellationToken));
    }

    [HttpPut("{userId}/recovery-codes")]
    public async Task<IActionResult> SetRecoveryCodes(string userId, [FromBody] SetRecoveryCodesRequest req, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
            return BadRequest(new { error = "Invalid user ID format" });
        return ToResponse(await _repo.SetRecoveryCodeHashesAsync(parsedUserId, req.Hashes, cancellationToken));
    }

    private IActionResult ToResponse<T>(Result<T> result) => result.Status switch
    {
        ResultStatus.Ok => Ok(new { success = true, data = result.Value }),
        ResultStatus.NotFound => NotFound(new { success = false, error = new { code = "NOT_FOUND", message = result.Error } }),
        ResultStatus.Failure => StatusCode(500, new { success = false, error = new { code = "FAILURE", message = result.Error } }),
        _ => StatusCode(500, new { success = false, error = new { code = "UNKNOWN_ERROR", message = "An unknown error occurred" } })
    };
}
