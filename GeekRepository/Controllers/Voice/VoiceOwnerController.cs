using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Voice;

/// <summary>Hash of a refresh token, in the body so it never appears in a URL or a request log.</summary>
public sealed record RefreshTokenHashRequest(string? TokenHash);

/// <summary>Email in the body for the same reason.</summary>
public sealed record OwnerEmailRequest(string? Email);

/// <summary>
/// /internal/v1/owner-logins and /internal/v1/owner-refresh-tokens (docs/Geek-Receptionist.md sections 6 and 7): the owner accounts of
/// Geek-Receptionist, behind X-Internal-Api-Key. The status comes from the failure code, never from its wording.
/// </summary>
[ApiController]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class VoiceOwnerController(IVoiceOwnerStore owners) : ControllerBase
{
    [HttpPost("internal/v1/owner-logins")]
    public async Task<IActionResult> Create([FromBody] CreateVoiceOwnerLoginCommand command, CancellationToken ct) =>
        command is null ? BadRequest("An owner login is required.") : ToAction(await owners.CreateOwnerLoginAsync(command, ct), StatusCodes.Status201Created);

    [HttpPost("internal/v1/owner-logins/find")]
    public async Task<IActionResult> Find([FromBody] OwnerEmailRequest request, CancellationToken ct) =>
        request is null ? BadRequest("An email is required.") : ToAction(await owners.FindOwnerLoginByEmailAsync(request.Email ?? string.Empty, ct));

    [HttpPost("internal/v1/owner-refresh-tokens")]
    public async Task<IActionResult> Store([FromBody] StoreVoiceRefreshTokenCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A refresh token is required.") : ToAction(await owners.StoreRefreshTokenAsync(command, ct), StatusCodes.Status201Created);

    [HttpPost("internal/v1/owner-refresh-tokens/rotate")]
    public async Task<IActionResult> Rotate([FromBody] RotateVoiceRefreshTokenCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A renewal is required.") : ToAction(await owners.RotateRefreshTokenAsync(command, ct));

    [HttpPost("internal/v1/owner-refresh-tokens/revoke")]
    public async Task<IActionResult> Revoke([FromBody] RefreshTokenHashRequest request, CancellationToken ct) =>
        request is null ? BadRequest("A token hash is required.") : ToAction(await owners.RevokeRefreshTokenFamilyAsync(request.TokenHash ?? string.Empty, ct));

    private IActionResult ToAction<T>(Result<T> result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.IsSuccess) return StatusCode(successStatus, result.Value);
        var message = string.IsNullOrWhiteSpace(result.Error) ? "The owner store failed." : result.Error;
        if (result.Status == ResultStatus.NotFound) return NotFound(message);
        return result.ErrorCode switch
        {
            VoiceFailureCodes.NotFound => NotFound(message),
            VoiceFailureCodes.InvalidRequest => BadRequest(message),
            VoiceFailureCodes.Conflict => Conflict(message),
            VoiceOwnerFailureCodes.RefreshTokenReused => Conflict(new { error = message, code = result.ErrorCode }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, message),
        };
    }
}
