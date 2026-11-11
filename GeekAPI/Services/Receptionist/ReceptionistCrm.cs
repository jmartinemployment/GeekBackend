using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;

namespace GeekAPI.Services.Receptionist;

/// <summary>
/// What the receptionist writes to Geek-CRM. Recording never affects the call: every method
/// swallows and logs its own failure, and gives up after a few seconds so Twilio's webhook
/// deadline is never at risk.
/// </summary>
public interface IReceptionistCrm
{
    Task RecordMissedCallAsync(string callSid, string? caller, MissedCallOutcome outcome, string? textBody, CancellationToken ct);
    Task RecordInboundTextAsync(string messageSid, string? from, string body, int mediaCount, string? optOutType, CancellationToken ct);
    Task RecordVoicemailAsync(string callSid, string? caller, string recordingUrl, int? durationSeconds, CancellationToken ct);
}

/// <summary>
/// Writes calls, texts, voicemails and opt-outs into geek_crm through <see cref="ICrmStore"/>
/// (repo/crm/* on GeekRepository). Every write is idempotent on the Twilio SID, so a webhook
/// retry records nothing twice. The tenant is <c>RECEPTIONIST_CRM_TENANT_ID</c>, or Geek @ Your
/// Spot (tenant #1) when unset.
/// </summary>
public sealed class CrmReceptionistRecorder(
    IServiceScopeFactory scopes,
    ReceptionistOptions options,
    TimeProvider clock,
    ILogger<CrmReceptionistRecorder> log) : IReceptionistCrm
{
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);
    private const string CreatedBy = "receptionist";
    private const int MaxBodyChars = 1600;

    private static readonly HashSet<string> OptOutWords =
        new(["STOP", "STOPALL", "UNSUBSCRIBE", "CANCEL", "END", "QUIT", "OPTOUT", "REVOKE"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> OptInWords =
        new(["START", "YES", "UNSTOP"], StringComparer.OrdinalIgnoreCase);

    public Task RecordMissedCallAsync(string callSid, string? caller, MissedCallOutcome outcome, string? textBody, CancellationToken ct) =>
        GuardedAsync("missed call", callSid, async (crm, token) =>
        {
            var participantId = await EnsureAsync(crm, caller, ParticipantSources.PhoneCall, token);
            await RecordAsync(crm, participantId, ActivityChannels.Call, ActivityDirections.Inbound, ActivityOutcomes.Missed,
                callSid, body: participantId is null ? $"Caller ID: {caller ?? "blocked"}" : null, token);

            if (outcome == MissedCallOutcome.Texted)
                await RecordAsync(crm, participantId, ActivityChannels.Sms, ActivityDirections.Outbound, ActivityOutcomes.TextedBack, callSid, textBody, token);
            else if (outcome == MissedCallOutcome.TextFailed)
                await RecordAsync(crm, participantId, ActivityChannels.Sms, ActivityDirections.Outbound, ActivityOutcomes.Failed, callSid, textBody, token);
        }, ct);

    public Task RecordInboundTextAsync(string messageSid, string? from, string body, int mediaCount, string? optOutType, CancellationToken ct) =>
        GuardedAsync("inbound text", messageSid, async (crm, token) =>
        {
            var participantId = await EnsureAsync(crm, from, ParticipantSources.Text, token);
            var text = body.Length > MaxBodyChars ? body[..MaxBodyChars] : body;
            if (mediaCount > 0) text += $" [{mediaCount} attachment(s) deleted, not stored]";
            await RecordAsync(crm, participantId, ActivityChannels.Sms, ActivityDirections.Inbound, outcome: null, messageSid, text, token);

            var keyword = (optOutType ?? body).Trim();
            bool? optedOut = OptOutWords.Contains(keyword) ? true : OptInWords.Contains(keyword) ? false : null;
            if (optedOut is { } value && CrmText.IsE164(from))
            {
                var result = await crm.SetSmsOptOutAsync(new SetSmsOptOutCommand(TenantId, from!, value), token);
                if (!result.IsSuccess)
                    log.LogError("CRM opt-out update failed for {MessageSid}: {Code}", messageSid, result.ErrorCode);
            }
        }, ct);

    public Task RecordVoicemailAsync(string callSid, string? caller, string recordingUrl, int? durationSeconds, CancellationToken ct) =>
        GuardedAsync("voicemail", callSid, async (crm, token) =>
        {
            var participantId = await EnsureAsync(crm, caller, ParticipantSources.PhoneCall, token);
            var command = new RecordActivityCommand(TenantId, participantId, null, ActivityChannels.Voicemail, ActivityDirections.Inbound,
                clock.GetUtcNow().UtcDateTime, null, null, null, recordingUrl, durationSeconds, ActivityOutcomes.VoicemailLeft, callSid, CreatedBy);
            var result = await crm.RecordActivityAsync(command, token);
            if (!result.IsSuccess)
                log.LogError("CRM voicemail write failed for {CallSid}: {Code}", callSid, result.ErrorCode);
        }, ct);

    private Guid TenantId => options.CrmTenantId;

    private async Task<Guid?> EnsureAsync(ICrmStore crm, string? phone, string source, CancellationToken ct)
    {
        if (!CrmText.IsE164(phone)) return null;
        var result = await crm.EnsureParticipantByPhoneAsync(
            new EnsureParticipantByPhoneCommand(TenantId, phone!, source, $"Geek-Receptionist {clock.GetUtcNow():yyyy-MM-dd}"), ct);
        if (result.IsSuccess) return result.Value!.ParticipantId;
        log.LogError("CRM participant lookup failed: {Code}", result.ErrorCode);
        return null;
    }

    private async Task RecordAsync(ICrmStore crm, Guid? participantId, string channel, string direction, string? outcome,
        string externalRef, string? body, CancellationToken ct)
    {
        var command = new RecordActivityCommand(TenantId, participantId, null, channel, direction, clock.GetUtcNow().UtcDateTime,
            null, body, null, null, null, outcome, externalRef, CreatedBy);
        var result = await crm.RecordActivityAsync(command, ct);
        if (!result.IsSuccess)
            log.LogError("CRM {Channel} activity write failed for {ExternalRef}: {Code}", channel, externalRef, result.ErrorCode);
    }

    private async Task GuardedAsync(string what, string sid, Func<ICrmStore, CancellationToken, Task> work, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Deadline);
        try
        {
            using var scope = scopes.CreateScope();
            var crm = scope.ServiceProvider.GetRequiredService<ICrmStore>();
            await work(crm, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogError("CRM write for {What} {Sid} gave up after {Seconds}s", what, sid, Deadline.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "CRM write for {What} {Sid} failed", what, sid);
        }
    }
}
