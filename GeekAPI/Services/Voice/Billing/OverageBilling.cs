using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;

namespace GeekAPI.Services.Voice.Billing;

/// <summary>Where one subscription got to in a sweep.</summary>
public enum OverageBillingStep
{
    /// <summary>The store could not close the period or record a charge's outcome; logged, and the next sweep starts again.</summary>
    StoreFailed,
    /// <summary>Another sweep recorded this period's charge moments ago and is still sending it.</summary>
    ChargeInProgress,
    /// <summary>PayPal's subscription could not be read, so the period stays where it is until the next sweep.</summary>
    RenewalUnknown,
    /// <summary>PayPal has not renewed the subscription yet; the next sweep looks again.</summary>
    AwaitingRenewal,
    /// <summary>PayPal says the subscription is no longer ACTIVE; its status is recorded and billing stops.</summary>
    Stopped,
    /// <summary>The period is closed, its charge settled and the next period begun.</summary>
    Advanced,
}

/// <summary>One subscription's result in a sweep, and the charge the period carries when it was closed.</summary>
public sealed record OverageBillingResult(Guid SubscriptionId, OverageBillingStep Step, VoiceOverageCharge? Charge);

/// <summary>
/// End-of-cycle billing (docs/Geek-Receptionist.md section 5, item 2). For each subscription whose period ended at least
/// <see cref="CloseAfter"/> ago and whose PayPal status was last seen ACTIVE: close the period once, adding up the billable minutes of
/// the calls that started in it; charge the minutes above the tier's included minutes at the subscription's rate to the saved PayPal
/// payment method; record the outcome; and, once PayPal has taken the next month's payment, start the next period where this one
/// ended. Only the sweep that recorded a charge sends it, and it is sent once: a charge left without an outcome for
/// <see cref="StaleCharge"/> (an instance that stopped mid-charge) is recorded as failed with an instruction to check PayPal, never
/// sent again. A failed charge is logged at Error and stays in the store with its reason; the period still moves on.
/// </summary>
public sealed class OverageBilling
{
    /// <summary>
    /// How long after a period ends it is closed: a call that started just before the end must have ended and been posted by Retell
    /// first. Retell ends a call at one hour unless told otherwise, so two hours leaves room for its last delivery.
    /// </summary>
    public static readonly TimeSpan CloseAfter = TimeSpan.FromHours(2);

    /// <summary>A charge this old with no outcome was left by a sweep that stopped while sending it.</summary>
    public static readonly TimeSpan StaleCharge = TimeSpan.FromMinutes(15);

    /// <summary>A subscription PayPal has not renewed this long after its period ended is logged as a warning.</summary>
    public static readonly TimeSpan RenewalOverdue = TimeSpan.FromDays(2);

    private readonly IVoiceBillingStore _store;
    private readonly PayPalCycleBilling _paypal;
    private readonly TimeProvider _clock;
    private readonly ILogger<OverageBilling> _logger;

    public OverageBilling(IVoiceBillingStore store, PayPalCycleBilling paypal, TimeProvider clock, ILogger<OverageBilling> logger)
    {
        _store = store;
        _paypal = paypal;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>One sweep. Nothing is closed when PayPal is not configured or the due list cannot be read; both are logged at Error.</summary>
    public async Task<IReadOnlyList<OverageBillingResult>> RunAsync(CancellationToken ct = default)
    {
        var missing = _paypal.Missing();
        if (missing.Count > 0)
        {
            _logger.LogError("End-of-cycle billing cannot run: {Variables} not set. No period was closed.", string.Join(", ", missing));
            return [];
        }

        var now = _clock.GetUtcNow();
        var due = await _store.ListDueAsync(new ListVoiceSubscriptionsDueCommand(now - CloseAfter), ct).ConfigureAwait(false);
        if (!due.IsSuccess)
        {
            _logger.LogError("End-of-cycle billing could not read the subscriptions due: {Code}. {Error}", due.ErrorCode, due.Error);
            return [];
        }

        var results = new List<OverageBillingResult>(due.Value!.Count);
        foreach (var subscription in due.Value!)
            results.Add(await BillAsync(subscription, now, ct).ConfigureAwait(false));
        return results;
    }

    private async Task<OverageBillingResult> BillAsync(VoiceSubscriptionDue due, DateTimeOffset now, CancellationToken ct)
    {
        var closed = await _store.CloseBillingPeriodAsync(due.TenantId, new CloseVoiceBillingPeriodCommand(due.SubscriptionId, due.CurrentPeriodEnd), ct).ConfigureAwait(false);
        if (!closed.IsSuccess)
        {
            _logger.LogError("Subscription {SubscriptionId} of tenant {TenantId}: the period ending {PeriodEnd:O} could not be closed: {Code}. {Error}",
                due.SubscriptionId, due.TenantId, due.CurrentPeriodEnd, closed.ErrorCode, closed.Error);
            return new(due.SubscriptionId, OverageBillingStep.StoreFailed, null);
        }

        var charge = closed.Value!.Charge;
        if (charge.Status == VoiceOverageStatuses.Charging)
        {
            OverageChargeOutcome outcome;
            if (closed.Value.Created)
            {
                outcome = await _paypal.ChargeAsync(charge, ct).ConfigureAwait(false);
            }
            else if (now - charge.CreatedAt >= StaleCharge)
            {
                outcome = OverageChargeOutcome.Failed(
                    $"No outcome was recorded for this charge: the sweep sending it stopped. Search PayPal for invoice {PayPalCycleBilling.InvoiceId(charge.Id)} before charging again.");
            }
            else
            {
                return new(due.SubscriptionId, OverageBillingStep.ChargeInProgress, charge);
            }

            var settled = await _store.SettleOverageChargeAsync(due.TenantId, charge.Id,
                new SettleVoiceOverageChargeCommand(outcome.Status, outcome.PaypalOrderId, outcome.PaypalCaptureId, outcome.Failure), ct).ConfigureAwait(false);
            if (!settled.IsSuccess)
            {
                // PayPal may have taken the money and the store does not know it: this line is the only record until it is fixed by hand.
                _logger.LogCritical(
                    "Overage charge {ChargeId} of tenant {TenantId} ended {Outcome} (PayPal order {OrderId}, capture {CaptureId}) and the outcome could not be recorded: {Code}. {Error}",
                    charge.Id, due.TenantId, outcome.Status, outcome.PaypalOrderId, outcome.PaypalCaptureId, settled.ErrorCode, settled.Error);
                return new(due.SubscriptionId, OverageBillingStep.StoreFailed, charge);
            }

            charge = settled.Value!;
            if (charge.Status == VoiceOverageStatuses.Charged)
                _logger.LogInformation("Overage charge {ChargeId} of tenant {TenantId}: {Minutes} minutes, {Amount} USD, charged (PayPal order {OrderId}).",
                    charge.Id, due.TenantId, charge.OverageMinutes, charge.AmountUsd, charge.PaypalOrderId);
            else
                _logger.LogError("Overage charge {ChargeId} of tenant {TenantId}: {Minutes} minutes, {Amount} USD, failed: {Failure}",
                    charge.Id, due.TenantId, charge.OverageMinutes, charge.AmountUsd, charge.Failure);
        }

        return await MoveOnAsync(due, charge, now, ct).ConfigureAwait(false);
    }

    private async Task<OverageBillingResult> MoveOnAsync(VoiceSubscriptionDue due, VoiceOverageCharge charge, DateTimeOffset now, CancellationToken ct)
    {
        var state = await _paypal.GetSubscriptionAsync(charge.PaypalSubscriptionId, ct).ConfigureAwait(false);
        if (!state.IsSuccess)
        {
            _logger.LogError("Subscription {SubscriptionId} of tenant {TenantId}: PayPal's subscription could not be read, so its period stays where it is: {Code}. {Error}",
                due.SubscriptionId, due.TenantId, state.ErrorCode, state.Error);
            return new(due.SubscriptionId, OverageBillingStep.RenewalUnknown, charge);
        }

        var paypal = state.Value!;
        if (paypal.Status != VoiceBillingStatuses.Active)
        {
            var recorded = await _store.SetBillingStatusAsync(due.TenantId, new SetVoiceBillingStatusCommand(due.SubscriptionId, paypal.Status), ct).ConfigureAwait(false);
            if (!recorded.IsSuccess)
            {
                _logger.LogError("Subscription {SubscriptionId} of tenant {TenantId}: PayPal says {Status} and that could not be recorded: {Code}. {Error}",
                    due.SubscriptionId, due.TenantId, paypal.Status, recorded.ErrorCode, recorded.Error);
                return new(due.SubscriptionId, OverageBillingStep.StoreFailed, charge);
            }

            _logger.LogWarning("Subscription {SubscriptionId} of tenant {TenantId}: PayPal says {Status}, so it is no longer billed.",
                due.SubscriptionId, due.TenantId, paypal.Status);
            return new(due.SubscriptionId, OverageBillingStep.Stopped, charge);
        }

        // Renewed means PayPal took a payment after this period began and now bills a later date than this period's end.
        var renewed = paypal.LastPaymentAt > charge.PeriodStart && paypal.NextBillingAt > due.CurrentPeriodEnd;
        if (!renewed)
        {
            if (now - due.CurrentPeriodEnd >= RenewalOverdue)
                _logger.LogWarning("Subscription {SubscriptionId} of tenant {TenantId}: PayPal has not renewed it since its period ended {PeriodEnd:O}.",
                    due.SubscriptionId, due.TenantId, due.CurrentPeriodEnd);
            return new(due.SubscriptionId, OverageBillingStep.AwaitingRenewal, charge);
        }

        var advanced = await _store.AdvanceBillingPeriodAsync(due.TenantId,
            new AdvanceVoiceBillingPeriodCommand(due.SubscriptionId, due.CurrentPeriodEnd, paypal.NextBillingAt!.Value), ct).ConfigureAwait(false);
        if (!advanced.IsSuccess)
        {
            _logger.LogError("Subscription {SubscriptionId} of tenant {TenantId}: the next period could not be begun: {Code}. {Error}",
                due.SubscriptionId, due.TenantId, advanced.ErrorCode, advanced.Error);
            return new(due.SubscriptionId, OverageBillingStep.StoreFailed, charge);
        }

        return new(due.SubscriptionId, OverageBillingStep.Advanced, charge);
    }
}
