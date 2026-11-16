using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Voice;

/// <summary>
/// How every Geek-Receptionist route of GeekRepository answers a store result, written once so the three controllers cannot answer the
/// same failure differently. The status comes from the failure code, never from its wording: not_found 404, invalid_request 400,
/// conflict 409, a replayed refresh token 409 with its code in a JSON body (so GeekAPI can tell it from an ordinary conflict), and
/// anything else 503. A refusal's message is plain text.
/// </summary>
public abstract class VoiceStoreController : ControllerBase
{
    protected IActionResult ToAction<T>(Result<T> result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.IsSuccess) return successStatus == StatusCodes.Status200OK ? Ok(result.Value) : StatusCode(successStatus, result.Value);
        var message = string.IsNullOrWhiteSpace(result.Error) ? "The voice store failed." : result.Error;
        // A result made with Result<T>.NotFound carries a status and no code; it is a 404 all the same.
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
