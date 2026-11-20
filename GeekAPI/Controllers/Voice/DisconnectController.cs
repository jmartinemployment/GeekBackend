using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// The owner withdrawing a connection they made (Jeff, 2026-11-20: booking is enabled "only at the client's request and
/// configuration", and "Google drive or S3 is opt-in").
///
/// <para>
/// <b>Why these routes exist at all.</b> Opting in only means something if opting out is possible. A business could connect a
/// calendar, a Drive folder or its own bucket and had no way to take any of them back; the only remedy was to revoke this
/// application inside their Google account, which Geek-Receptionist would then discover the next time it tried to use the
/// connection. Withdrawing permission is the owner's decision and belongs on a route of its own.
/// </para>
///
/// <para>
/// <b>Withdrawing twice is still withdrawing.</b> Both routes are idempotent: removing a connection that is already gone answers
/// 200 with <c>removed: false</c>, never a 404. The owner asked for the connection not to exist, and it does not exist. A 404
/// here would read as "something went wrong" for the one outcome they wanted.
/// </para>
///
/// <para>
/// <b>These live together, apart from the routes that create them.</b> Connecting a calendar is OAuth
/// (<see cref="McpOAuthController"/>) and connecting a bucket is sealing credentials (<see cref="StorageController"/>) - two
/// different jobs. Withdrawing either is one job, with one rule (below) that spans both kinds of connection, so it is one file.
/// </para>
///
/// <para>The tenant comes from the owner's own tenant_id claim and a URL naming another tenant is refused (403). Zero Data
/// Retention is untouched: nothing here reads or writes call content, and no log line carries a token, a key or a bucket.</para>
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ReceptionistAuth.Scheme)]
public sealed class DisconnectController(IVoiceStore store, ILogger<DisconnectController> logger) : ControllerBase
{
    /// <summary>
    /// Disconnects a calendar or the Drive folder call records go to. The provider is the one the connection was made under
    /// (<see cref="VoiceMcpProviders"/>); anything else is refused rather than quietly treated as nothing to remove.
    ///
    /// <para>
    /// <b>Disconnecting a calendar turns booking off, in the same request.</b> <see cref="BookingSettingController"/> refuses to
    /// turn booking on without a calendar that works, because a receptionist told it can book with no calendar is a claim the
    /// product cannot honour - <c>ReceptionistRoleScripts</c> drops the booker and every caller is offered a message instead.
    /// Leaving <c>BookingEnabled</c> true with the calendar gone reaches that exact state by the back door, so this closes it.
    /// </para>
    ///
    /// <para>
    /// <b>The order is booking off first, then the disconnect, and the question is which failure leaves the safer state.</b>
    /// Turning booking off and then failing to disconnect leaves booking off with the calendar still connected: the receptionist
    /// offers no booking, nothing is claimed that cannot be honoured, the owner is told the disconnect failed, and asking again
    /// finishes the job. Disconnecting first and then failing to save the setting leaves booking on with no calendar - the
    /// forbidden state - and the disconnect cannot be undone, because the credentials are gone. One order has a recoverable
    /// failure and the other has an unrecoverable one, so the order is not a matter of taste.
    /// </para>
    ///
    /// <para>
    /// <b>A booking setting that will not save aborts the disconnect.</b> The failure is returned with its code; nothing is
    /// removed, and no second path tries to salvage it. Asking again reads the config afresh, so a retry after booking did go
    /// off skips the save and goes straight to the disconnect. The same holds when the config cannot be read: this cannot tell
    /// whether booking is on, and the store it would have to ask is the same store the disconnect needs, so refusing is honest
    /// rather than obstructive.
    /// </para>
    ///
    /// <para>
    /// <b>Google Drive is not a calendar.</b> It is where call records are written, and disconnecting it leaves the booking
    /// setting exactly as the owner left it. <see cref="VoiceMcpProviders.Calendars"/> is what decides, so adding a calendar
    /// provider carries this rule with it.
    /// </para>
    /// </summary>
    [HttpDelete("api/v1/tenants/{tenantId:guid}/oauth/mcp/{provider}")]
    public async Task<IActionResult> DisconnectMcp(Guid tenantId, string provider, CancellationToken ct)
    {
        if (OwnerTenantClaim.Refuse(User, tenantId) is { } refused) return refused;

        if (!VoiceMcpProviders.All.Contains(provider, StringComparer.Ordinal))
        {
            return BadRequest(new
            {
                error = $"The provider must be one of: {string.Join(", ", VoiceMcpProviders.All)}.",
                code = VoiceFailureCodes.InvalidRequest,
            });
        }

        var isCalendar = VoiceMcpProviders.Calendars.Contains(provider, StringComparer.Ordinal);
        var bookingTurnedOff = false;

        if (isCalendar)
        {
            var config = await store.GetConfigAsync(tenantId, ct).ConfigureAwait(false);
            if (!config.IsSuccess)
            {
                logger.LogError(
                    "Tenant {TenantId}: its settings could not be read ({Code}), so its {Provider} connection was left in place.",
                    tenantId, config.ErrorCode, provider);
                return StatusCode(CalendarController.StatusFor(config.ErrorCode), new { error = config.Error, code = config.ErrorCode });
            }

            if (config.Value!.BookingEnabled)
            {
                var saved = await store
                    .SaveConfigAsync(tenantId, config.Value with { BookingEnabled = false }, ct).ConfigureAwait(false);
                if (!saved.IsSuccess)
                {
                    logger.LogError(
                        "Tenant {TenantId}: booking could not be turned off ({Code}), so its {Provider} connection was left in place.",
                        tenantId, saved.ErrorCode, provider);
                    return StatusCode(CalendarController.StatusFor(saved.ErrorCode), new { error = saved.Error, code = saved.ErrorCode });
                }

                bookingTurnedOff = true;
                logger.LogInformation(
                    "Tenant {TenantId}: booking turned off because its {Provider} calendar is being disconnected.", tenantId, provider);
            }
        }

        var removed = await store.DisconnectMcpAsync(tenantId, provider, ct).ConfigureAwait(false);
        if (!removed.IsSuccess)
        {
            logger.LogError(
                "Tenant {TenantId}: its {Provider} connection could not be removed ({Code}).", tenantId, provider, removed.ErrorCode);
            return StatusCode(CalendarController.StatusFor(removed.ErrorCode), new { error = removed.Error, code = removed.ErrorCode });
        }

        logger.LogInformation(
            "Tenant {TenantId} disconnected {Provider} (removed: {Removed}).", tenantId, provider, removed.Value!.Removed);

        // bookingDisabled is reported because the owner asked to disconnect a calendar and a second setting changed with it. A
        // portal that was not told would go on showing booking as on until the next time it read the setting.
        return Ok(new { removed = removed.Value.Removed, provider, bookingDisabled = bookingTurnedOff });
    }

    /// <summary>
    /// Disconnects the business's own bucket, so call records stop being written to it. There is nothing to turn off alongside
    /// it: a bucket is where records go, not something the receptionist offers a caller, and the one setting that depends on a
    /// connection is booking, which depends on a calendar.
    ///
    /// <para>The row and the sealed keys in it are removed by the store. Nothing of the bucket is logged or returned.</para>
    /// </summary>
    [HttpDelete("api/v1/tenants/{tenantId:guid}/storage/bucket")]
    public async Task<IActionResult> DisconnectStorage(Guid tenantId, CancellationToken ct)
    {
        if (OwnerTenantClaim.Refuse(User, tenantId) is { } refused) return refused;

        var removed = await store.DisconnectStorageAsync(tenantId, ct).ConfigureAwait(false);
        if (!removed.IsSuccess)
        {
            logger.LogError("Tenant {TenantId}: its bucket connection could not be removed ({Code}).", tenantId, removed.ErrorCode);
            return StatusCode(CalendarController.StatusFor(removed.ErrorCode), new { error = removed.Error, code = removed.ErrorCode });
        }

        logger.LogInformation("Tenant {TenantId} disconnected its bucket (removed: {Removed}).", tenantId, removed.Value!.Removed);
        return Ok(new { removed = removed.Value.Removed });
    }
}
