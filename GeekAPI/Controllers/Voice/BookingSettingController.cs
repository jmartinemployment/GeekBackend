using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.Onboarding;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Voice;

/// <summary>Whether this business lets its receptionist put an appointment in its calendar, as the owner asked for it.</summary>
/// <param name="Enabled">What the owner is asking for.</param>
public sealed record BookingSettingRequest(bool Enabled);

/// <summary>
/// The owner's own switch for booking (plans/client-and-app-plan.md; Jeff, 2026-11-20: booking is enabled "only at the client's request
/// and configuration", and a business that later turns it off allows no booking).
///
/// <para>
/// <b>It starts off and stays off until the owner turns it on.</b> A new tenant's settings leave it off, so a business that never asks
/// never has its calendar written to by a telephone call. That is the whole reason this is a switch and not a consequence of having
/// connected a calendar: connecting one is how an owner lets the receptionist read their availability, and agreeing to let a stranger on
/// the telephone put something in it is a separate decision.
/// </para>
///
/// <para>
/// <b>Turning it on is refused without a calendar that works.</b> Not ignored, refused, with a code the portal turns into a sentence. A
/// setting saved as on with nothing behind it would show the owner a receptionist that books while every caller is offered a message
/// instead - the roster drops a booker with no calendar (<c>ReceptionistRoleScripts</c>), so the switch would be a claim the product
/// cannot honour. The check runs the same call a telephone call would.
/// </para>
///
/// <para>
/// <b>Turning it off is never refused.</b> It is the owner withdrawing permission, it takes effect on the next call, and nothing about
/// the state of their calendar can stand in the way of it.
/// </para>
///
/// <para>The tenant comes from the owner's own tenant_id claim; a URL naming another tenant is refused. Zero Data Retention is untouched:
/// this reads and writes one business setting and no call content.</para>
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ReceptionistAuth.Scheme)]
public sealed class BookingSettingController(IVoiceStore store, CalendarTools calendar, ILogger<BookingSettingController> logger)
    : ControllerBase
{
    /// <summary>
    /// Asked for booking with no calendar behind it. The owner is told to connect one, not told it worked. One definition, in
    /// <see cref="ReceptionistConfigWriter"/>, because the whole-config route enforces the same rule and the two must not answer with
    /// different codes - the portal turns this one into a sentence.
    /// </summary>
    public const string CalendarRequired = ReceptionistConfigWriter.CalendarRequired;

    /// <summary>
    /// What the receptionist does about appointments today, and whether the calendar behind it works. Both, because "booking is on" and
    /// "booking can happen" are different facts and an owner looking at this page needs to know when they have come apart.
    /// </summary>
    [HttpGet("api/v1/tenants/{tenantId:guid}/receptionist/booking")]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        if (OwnerTenantClaim.Refuse(User, tenantId) is { } refused) return refused;

        var config = await store.GetConfigAsync(tenantId, ct).ConfigureAwait(false);
        if (!config.IsSuccess)
            return StatusCode(CalendarController.StatusFor(config.ErrorCode), new { error = config.Error, code = config.ErrorCode });

        return Ok(new
        {
            enabled = config.Value!.BookingEnabled,
            // What the business answered on the signup form, which is why the dashboard can tell an owner their yes is waiting on a
            // calendar rather than showing them booking plainly off and leaving them to remember they asked for it.
            requested = config.Value.BookingRequested,
            calendarConnected = await ConnectedAsync(tenantId, ct).ConfigureAwait(false),
        });
    }

    /// <summary>Turns booking on or off for this business. On takes a working calendar; off takes nothing at all.</summary>
    [HttpPut("api/v1/tenants/{tenantId:guid}/receptionist/booking")]
    public async Task<IActionResult> Put(Guid tenantId, [FromBody] BookingSettingRequest? request, CancellationToken ct)
    {
        if (OwnerTenantClaim.Refuse(User, tenantId) is { } refused) return refused;
        if (request is null)
            return BadRequest(new { error = "Say whether booking is to be on or off.", code = VoiceFailureCodes.InvalidRequest });

        var config = await store.GetConfigAsync(tenantId, ct).ConfigureAwait(false);
        if (!config.IsSuccess)
            return StatusCode(CalendarController.StatusFor(config.ErrorCode), new { error = config.Error, code = config.ErrorCode });

        // Turning it on is the only direction that can be refused, and only for the one reason the owner can do something about.
        if (request.Enabled && !await ConnectedAsync(tenantId, ct).ConfigureAwait(false))
        {
            logger.LogWarning("Tenant {TenantId} asked to turn booking on without a calendar behind it; it stays off.", tenantId);
            return Conflict(new
            {
                error = "Connect a calendar that works before the receptionist can book into it.",
                code = CalendarRequired,
            });
        }

        // The whole record goes back, because that is how it is saved; only this one field differs from what was read a moment ago.
        var saved = await store
            .SaveConfigAsync(tenantId, config.Value! with { BookingEnabled = request.Enabled }, ct).ConfigureAwait(false);

        if (!saved.IsSuccess)
        {
            logger.LogError("Tenant {TenantId}: its booking setting could not be saved ({Code}).", tenantId, saved.ErrorCode);
            return StatusCode(CalendarController.StatusFor(saved.ErrorCode), new { error = saved.Error, code = saved.ErrorCode });
        }

        logger.LogInformation("Tenant {TenantId} turned booking {State}.", tenantId, saved.Value!.BookingEnabled ? "on" : "off");
        return Ok(new
        {
            enabled = saved.Value!.BookingEnabled,
            requested = saved.Value.BookingRequested,
            // Turning it on has just checked the calendar, so that is known. Turning it off checked nothing, and said `true` here
            // anyway until now - a claim about the owner's calendar that nothing had looked at. It is asked instead.
            calendarConnected = request.Enabled || await ConnectedAsync(tenantId, ct).ConfigureAwait(false),
        });
    }

    /// <summary>
    /// Whether this business has a calendar a call could actually book into, asked the way a call asks. A connection that exists but no
    /// longer works is not a calendar: it is the missed booking this check exists to prevent.
    /// </summary>
    private async Task<bool> ConnectedAsync(Guid tenantId, CancellationToken ct)
    {
        var check = await calendar.CallAsync(tenantId, GoogleCalendarOptions.Provider, "list_calendars", null, ct).ConfigureAwait(false);
        return check.IsSuccess;
    }
}
