using GeekAPI.Services.Voice.Onboarding;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// Everything a business has decided about how its receptionist behaves, and the owner's own way to change any of it. Jeff,
/// 2026-11-20: *"I am not comfortable making decisions on behalf of others."* The answers given at signup were permanent until this
/// existed: fourteen of the sixteen settings had no write path at any status, so a business that chose wrong at signup, or changed how
/// it works, had nothing to change.
///
/// <para>
/// The same merge the signup form uses (<see cref="ReceptionistConfigWriter"/>), behind a different gate. There the caller is the
/// portal's server key and the tenant must still be pending payment; here the caller is the owner, holding their own token, and the
/// tenant comes from their <c>tenant_id</c> claim - a URL naming another business is refused on both verbs, because being shown what
/// another business tells its callers is a disclosure and not a harmless GET.
/// </para>
///
/// <para>
/// A field left out of the body is a field not being changed, so a dashboard card that owns one setting sends one field. Clearing a
/// setting that can be empty is saying so: <c>""</c> or <c>[]</c>, never null. Zero Data Retention is untouched - this reads and
/// writes business settings and no call content, and <see cref="VoiceReceptionistConfig.IncludeTranscript"/> continues to mean only
/// that the end-of-call email the business asked for carries what was said. Nothing persists a transcript because it is on.
/// </para>
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ReceptionistAuth.Scheme)]
public sealed class ReceptionistConfigController(
    IVoiceStore store, ReceptionistConfigWriter writer, ILogger<ReceptionistConfigController> logger) : ControllerBase
{
    /// <summary>Its settings as they stand, with the lists it may choose between so a page never has to carry its own copy of them.</summary>
    [HttpGet("api/v1/tenants/{tenantId:guid}/receptionist/config")]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        if (OwnerTenantClaim.Refuse(User, tenantId) is { } refused) return refused;

        var config = await store.GetConfigAsync(tenantId, ct).ConfigureAwait(false);
        if (!config.IsSuccess)
            return StatusCode(CalendarController.StatusFor(config.ErrorCode), new { error = config.Error, code = config.ErrorCode });

        return Ok(Answered(config.Value!));
    }

    /// <summary>Changes the settings named in the body and leaves every other one alone.</summary>
    [HttpPut("api/v1/tenants/{tenantId:guid}/receptionist/config")]
    public async Task<IActionResult> Put(Guid tenantId, [FromBody] ReceptionistConfigAnswers? answers, CancellationToken ct)
    {
        if (OwnerTenantClaim.Refuse(User, tenantId) is { } refused) return refused;

        var saved = await writer.ApplyForOwnerAsync(tenantId, answers, ct).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            logger.LogError("Tenant {TenantId}: its receptionist settings could not be saved ({Code}).", tenantId, saved.ErrorCode);
            return StatusCode(CalendarController.StatusFor(saved.ErrorCode), new { error = saved.Error, code = saved.ErrorCode });
        }

        // Which settings it changed, never what it changed them to: a greeting, a script, a transfer number and a destination
        // address all pass through here.
        logger.LogInformation(
            "Tenant {TenantId} changed its receptionist settings: {Settings}.",
            tenantId,
            string.Join(", ", answers!.AnsweredFields));
        return Ok(Answered(saved.Value!));
    }

    /// <summary>The settings, and every choice each closed question has, so the dashboard offers what the product supports and no more.</summary>
    private static object Answered(VoiceReceptionistConfig config) => new
    {
        settings = config,
        choices = new
        {
            transferModes = VoiceTransferModes.All,
            transferCallerIdModes = VoiceTransferCallerIdModes.All,
            consentModes = VoiceConsentModes.All,
        },
    };
}
