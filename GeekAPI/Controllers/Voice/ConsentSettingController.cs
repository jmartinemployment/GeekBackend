using GeekAPI.Services.Voice.OwnerAuth;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Voice;

/// <summary>How this business wants its callers told, and asked, before the AI engages.</summary>
/// <param name="Mode">One of <see cref="VoiceConsentModes"/>.</param>
public sealed record ConsentSettingRequest(string Mode);

/// <summary>
/// The owner's own choice of consent mode. Jeff, 2026-11-20: *"Client configurable. I don't feel comfortable making that decision for
/// others."*
///
/// <para>
/// <b>There is no recommended answer here and this route does not imply one.</b> The two modes trade different things and the trade is
/// the business's to make. Strict keypress is affirmative consent, which is better evidence in an all-party state, and an autodialer
/// playing a recording cannot press a key - which matters more than it looks, because this product bills by the minute, so every
/// robocall the AI answers is a minute billed to the business for a call it never wanted. Passive disclosure asks nothing of anybody and
/// is what comparable products do, and it deters nothing at all: staying on the line is exactly what a recording does. One costs a
/// business its callers' patience, the other costs it minutes. Which is worse depends on who is calling, and the business knows that and
/// we do not.
/// </para>
///
/// <para>
/// Neither direction is refused and neither asks a question back. Unlike booking, there is nothing to check: both modes work for every
/// tenant, all of the time.
/// </para>
///
/// <para>The tenant comes from the owner's own tenant_id claim; a URL naming another tenant is refused. Zero Data Retention is untouched:
/// this reads and writes one business setting and no call content.</para>
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ReceptionistAuth.Scheme)]
public sealed class ConsentSettingController(IVoiceStore store, ILogger<ConsentSettingController> logger) : ControllerBase
{
    /// <summary>What this business's callers hear now, and the modes it may choose between.</summary>
    [HttpGet("api/v1/tenants/{tenantId:guid}/receptionist/consent")]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        if (OwnerTenantClaim.Refuse(User, tenantId) is { } refused) return refused;

        var config = await store.GetConfigAsync(tenantId, ct).ConfigureAwait(false);
        if (!config.IsSuccess)
            return StatusCode(CalendarController.StatusFor(config.ErrorCode), new { error = config.Error, code = config.ErrorCode });

        return Ok(new { mode = config.Value!.ConsentMode, modes = VoiceConsentModes.All });
    }

    /// <summary>Changes it. Either direction, no questions, no preconditions.</summary>
    [HttpPut("api/v1/tenants/{tenantId:guid}/receptionist/consent")]
    public async Task<IActionResult> Put(Guid tenantId, [FromBody] ConsentSettingRequest? request, CancellationToken ct)
    {
        if (OwnerTenantClaim.Refuse(User, tenantId) is { } refused) return refused;

        // A mode this product does not have would be saved and then silently fall through to passive on the next call, which is a
        // business being given a setting it did not ask for by a route that said yes.
        if (request is null || !VoiceConsentModes.All.Contains(request.Mode, StringComparer.Ordinal))
            return BadRequest(new
            {
                error = $"Choose one of: {string.Join(", ", VoiceConsentModes.All)}.",
                code = VoiceFailureCodes.InvalidRequest,
            });

        var config = await store.GetConfigAsync(tenantId, ct).ConfigureAwait(false);
        if (!config.IsSuccess)
            return StatusCode(CalendarController.StatusFor(config.ErrorCode), new { error = config.Error, code = config.ErrorCode });

        var saved = await store
            .SaveConfigAsync(tenantId, config.Value! with { ConsentMode = request.Mode }, ct).ConfigureAwait(false);

        if (!saved.IsSuccess)
        {
            logger.LogError("Tenant {TenantId}: its consent mode could not be saved ({Code}).", tenantId, saved.ErrorCode);
            return StatusCode(CalendarController.StatusFor(saved.ErrorCode), new { error = saved.Error, code = saved.ErrorCode });
        }

        logger.LogInformation("Tenant {TenantId} set its consent mode to {Mode}.", tenantId, saved.Value!.ConsentMode);
        return Ok(new { mode = saved.Value!.ConsentMode, modes = VoiceConsentModes.All });
    }
}
