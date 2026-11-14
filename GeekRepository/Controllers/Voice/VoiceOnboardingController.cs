using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Voice;

/// <summary>
/// /internal/v1/onboarding/tenants (docs/Geek-Receptionist.md section 6): a business signs up and, once it has paid and its
/// telephony exists, is activated. Behind X-Internal-Api-Key. The status comes from the failure code, never from its wording.
/// </summary>
[ApiController]
[Route("internal/v1/onboarding/tenants")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class VoiceOnboardingController(IVoiceStore voice) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVoiceTenantCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A tenant is required.") : ToAction(await voice.CreateTenantAsync(command, ct));

    [HttpGet("{tenantId:guid}")]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct) =>
        ToAction(await voice.GetTenantAsync(tenantId, ct));

    [HttpPost("{tenantId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid tenantId, [FromBody] ActivateVoiceTenantCommand command, CancellationToken ct) =>
        command is null ? BadRequest("An activation is required.") : ToAction(await voice.ActivateTenantAsync(tenantId, command, ct));

    private IActionResult ToAction<T>(Result<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        var message = string.IsNullOrWhiteSpace(result.Error) ? "The voice store failed." : result.Error;
        // A result made with Result<T>.NotFound carries a status and no code; it is a 404 all the same.
        if (result.Status == ResultStatus.NotFound) return NotFound(message);
        return result.ErrorCode switch
        {
            VoiceFailureCodes.NotFound => NotFound(message),
            VoiceFailureCodes.InvalidRequest => BadRequest(message),
            VoiceFailureCodes.Conflict => Conflict(message),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, message),
        };
    }
}
