using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Billing;

/// <summary>What a look at a suspended tenant did.</summary>
public enum TenantStandingStep
{
    /// <summary>Still owed or still held by PayPal, inside its seven days of answering, or already stopped: nothing to change.</summary>
    StillSuspended,
    /// <summary>Its seven days ended, so its number stopped answering.</summary>
    AnsweringStopped,
    /// <summary>Nothing is owed and PayPal holds the subscription ACTIVE: active again, its number answering.</summary>
    Restored,
    /// <summary>PayPal cancelled or expired the subscription: the tenant is cancelled and its number and agent released.</summary>
    Cancelled,
    /// <summary>A step failed; it is logged, and the next sweep looks again.</summary>
    Failed,
}

/// <summary>
/// A tenant's standing with billing (docs/Geek-Receptionist.md section 5, item 2, decided 2026-11-16). A failed overage charge, or a
/// subscription PayPal stops holding ACTIVE, suspends the tenant: its owner can still sign in, to see why and pay, and its number keeps
/// answering for <see cref="AnsweringGrace"/>, after which Retell stops answering it. Once no overage is unpaid and PayPal holds the
/// subscription ACTIVE, the number answers again and the tenant is active. A subscription PayPal cancels or expires cancels the tenant
/// and releases its Twilio subaccount (and with it the number) and its Retell number, agent and model, so nothing bills on. A change to
/// Retell is made before the record says so, so the record never claims a state the phone is not in.
/// </summary>
public sealed class TenantStanding
{
    public static readonly TimeSpan AnsweringGrace = TimeSpan.FromDays(7);

    /// <summary>PayPal statuses after which a subscription never bills again.</summary>
    public static readonly IReadOnlySet<string> FinalPayPalStatuses = new HashSet<string>(StringComparer.Ordinal) { "CANCELLED", "EXPIRED" };

    private readonly IVoiceBillingStore _store;
    private readonly IVoiceStore _voice;
    private readonly RetellProvisioner _retell;
    private readonly TwilioProvisioner _twilio;
    private readonly ILogger<TenantStanding> _logger;

    public TenantStanding(IVoiceBillingStore store, IVoiceStore voice, RetellProvisioner retell, TwilioProvisioner twilio, ILogger<TenantStanding> logger)
    {
        _store = store;
        _voice = voice;
        _retell = retell;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task<Result<VoiceBillingStanding>> SuspendAsync(Guid tenantId, string why, CancellationToken ct = default)
    {
        var suspended = await _store.SuspendTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (suspended.IsSuccess)
            _logger.LogWarning("Tenant {TenantId} is suspended ({Why}); its number answers until {Until:O}.", tenantId, why, suspended.Value!.SuspendedAt + AnsweringGrace);
        else
            _logger.LogError("Tenant {TenantId} should be suspended ({Why}) and could not be: {Code}. {Error}", tenantId, why, suspended.ErrorCode, suspended.Error);
        return suspended;
    }

    /// <summary>The hourly look at a suspended tenant: restore it when it is clear, otherwise stop its number once its seven days end.</summary>
    public async Task<TenantStandingStep> ReviewAsync(VoiceBillingStanding standing, DateTimeOffset now, CancellationToken ct = default)
    {
        if (standing.TenantStatus != VoiceTenantStatuses.Suspended) return TenantStandingStep.StillSuspended;

        if (IsClear(standing))
            return (await RestoreAsync(standing, ct).ConfigureAwait(false)).IsSuccess ? TenantStandingStep.Restored : TenantStandingStep.Failed;

        if (standing.AnsweringStoppedAt is not null || standing.SuspendedAt is null || now < standing.SuspendedAt.Value + AnsweringGrace)
            return TenantStandingStep.StillSuspended;

        var telephony = await _voice.GetTelephonyAsync(standing.TenantId, ct).ConfigureAwait(false);
        if (!telephony.IsSuccess || telephony.Value!.RetellAgentId is null)
        {
            _logger.LogError("Tenant {TenantId}'s seven days of answering ended and its number could not be found to stop: {Code}. {Error}",
                standing.TenantId, telephony.ErrorCode, telephony.Error ?? "it has no Retell agent");
            return TenantStandingStep.Failed;
        }

        var stopped = await _retell.SetAnsweringAsync(standing.TenantId, telephony.Value.AssignedPhoneNumber, null, ct).ConfigureAwait(false);
        if (!stopped.IsSuccess)
        {
            _logger.LogError("Tenant {TenantId}'s seven days of answering ended and Retell did not stop its number: {Error}", standing.TenantId, stopped.Error);
            return TenantStandingStep.Failed;
        }

        var marked = await _store.MarkAnsweringStoppedAsync(standing.TenantId, ct).ConfigureAwait(false);
        if (!marked.IsSuccess)
        {
            _logger.LogError("Tenant {TenantId}'s number stopped answering and that could not be recorded: {Code}. {Error}", standing.TenantId, marked.ErrorCode, marked.Error);
            return TenantStandingStep.Failed;
        }

        _logger.LogWarning("Tenant {TenantId}'s number stopped answering: suspended since {SuspendedAt:O} and still owing or held by PayPal.", standing.TenantId, standing.SuspendedAt);
        return TenantStandingStep.AnsweringStopped;
    }

    /// <summary>Restores a suspended tenant that is clear, turning its number back on first when it had stopped. A tenant that is not clear is refused (conflict).</summary>
    public async Task<Result<VoiceBillingStanding>> RestoreAsync(VoiceBillingStanding standing, CancellationToken ct = default)
    {
        if (!IsClear(standing))
            return Result<VoiceBillingStanding>.Failure("An overage is unpaid or PayPal does not hold the subscription ACTIVE.", VoiceFailureCodes.Conflict);

        if (standing.AnsweringStoppedAt is not null)
        {
            var telephony = await _voice.GetTelephonyAsync(standing.TenantId, ct).ConfigureAwait(false);
            if (!telephony.IsSuccess || telephony.Value!.RetellAgentId is null)
            {
                _logger.LogError("Tenant {TenantId} is clear and its number could not be found to turn back on: {Code}. {Error}",
                    standing.TenantId, telephony.ErrorCode, telephony.Error ?? "it has no Retell agent");
                return Result<VoiceBillingStanding>.Failure("The tenant's number could not be found.", telephony.ErrorCode ?? VoiceFailureCodes.NotFound);
            }

            var resumed = await _retell.SetAnsweringAsync(standing.TenantId, telephony.Value.AssignedPhoneNumber, telephony.Value.RetellAgentId, ct).ConfigureAwait(false);
            if (!resumed.IsSuccess)
            {
                _logger.LogError("Tenant {TenantId} is clear and Retell did not turn its number back on: {Error}", standing.TenantId, resumed.Error);
                return Result<VoiceBillingStanding>.Failure(resumed.Error!, resumed.ErrorCode!);
            }
        }

        var restored = await _store.RestoreTenantAsync(standing.TenantId, ct).ConfigureAwait(false);
        if (restored.IsSuccess)
            _logger.LogInformation("Tenant {TenantId} is active again.", standing.TenantId);
        else
            _logger.LogError("Tenant {TenantId} is clear and could not be restored: {Code}. {Error}", standing.TenantId, restored.ErrorCode, restored.Error);
        return restored;
    }

    /// <summary>Cancels the tenant and releases its Twilio subaccount and Retell number, agent and model, one attempt each.</summary>
    public async Task<TenantStandingStep> CancelAsync(Guid tenantId, string paypalStatus, CancellationToken ct = default)
    {
        var cancelled = await _store.CancelTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (!cancelled.IsSuccess)
        {
            _logger.LogError("Tenant {TenantId}: PayPal says {Status} and the tenant could not be cancelled: {Code}. {Error}", tenantId, paypalStatus, cancelled.ErrorCode, cancelled.Error);
            return TenantStandingStep.Failed;
        }

        _logger.LogWarning("Tenant {TenantId} is cancelled: PayPal says {Status}.", tenantId, paypalStatus);

        var telephony = await _voice.GetTelephonyAsync(tenantId, ct).ConfigureAwait(false);
        if (!telephony.IsSuccess)
        {
            _logger.LogError("Cancelled tenant {TenantId}'s number and agent could not be found to release: {Code}. {Error}", tenantId, telephony.ErrorCode, telephony.Error);
            return TenantStandingStep.Failed;
        }

        var held = telephony.Value!;
        var retellGone = held.RetellAgentId is null
            || await _retell.ReleaseAsync(tenantId, held.RetellAgentId, held.AssignedPhoneNumber, ct).ConfigureAwait(false);
        var twilioGone = await _twilio.ReleaseAsync(held.TwilioSubaccountSid, tenantId, ct).ConfigureAwait(false);
        if (!retellGone || !twilioGone)
        {
            _logger.LogError("Cancelled tenant {TenantId}: not everything was released (Retell {Retell}, Twilio {Twilio}); the ids are in the lines above.",
                tenantId, retellGone ? "released" : "left", twilioGone ? "released" : "left");
            return TenantStandingStep.Failed;
        }

        var marked = await _store.MarkTelephonyReleasedAsync(tenantId, ct).ConfigureAwait(false);
        if (!marked.IsSuccess)
        {
            _logger.LogError("Cancelled tenant {TenantId}'s number and agent were released and that could not be recorded: {Code}. {Error}", tenantId, marked.ErrorCode, marked.Error);
            return TenantStandingStep.Failed;
        }

        return TenantStandingStep.Cancelled;
    }

    public static bool IsClear(VoiceBillingStanding standing) =>
        standing.UnpaidCharges.Count == 0 && standing.BillingStatus == VoiceBillingStatuses.Active;
}
