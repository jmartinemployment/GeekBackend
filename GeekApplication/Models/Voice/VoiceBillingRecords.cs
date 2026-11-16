namespace GeekApplication.Models.Voice;

// Geek-Receptionist end-of-cycle billing (docs/Geek-Receptionist.md section 5, item 2): when a tenant's billing period ends, its
// calls' billable minutes are added up, the minutes above the tier's included minutes are charged at the subscription's overage rate
// to the tenant's saved PayPal payment method, and the period moves on once PayPal has renewed the subscription. JSON camelCase on
// the wire, over /internal/v1/billing/*.

/// <summary>Where a period's overage charge stands. Only <see cref="Charging"/> is unsettled; the other three are final.</summary>
public static class VoiceOverageStatuses
{
    public const string NoOverage = "no_overage";
    public const string Charging = "charging";
    public const string Charged = "charged";
    public const string Failed = "failed";
}

/// <summary>Which subscriptions to look at: those whose period ended at or before <see cref="EndedBy"/> and whose PayPal status was last seen ACTIVE.</summary>
public sealed record ListVoiceSubscriptionsDueCommand(DateTimeOffset EndedBy);

/// <summary>A subscription whose billing period has ended and has not moved on.</summary>
public sealed record VoiceSubscriptionDue(Guid TenantId, Guid SubscriptionId, DateTimeOffset CurrentPeriodEnd);

/// <summary>
/// Close the subscription's current period, which must still end at <see cref="PeriodEnd"/>: its minutes are added up and its
/// overage charge recorded, once. Closing a period that is already closed answers the charge already recorded.
/// </summary>
public sealed record CloseVoiceBillingPeriodCommand(Guid SubscriptionId, DateTimeOffset PeriodEnd);

/// <summary>
/// One closed billing period and what it owed. <see cref="AmountUsd"/> is <see cref="OverageMinutes"/> times
/// <see cref="OverageRateUsd"/>, both read from the subscription when the period was closed. The PayPal ids say where to charge.
/// A failed charge the owner paid later carries the collected order and capture and when; it stays failed, which is what the
/// automatic charge did.
/// </summary>
public sealed record VoiceOverageCharge(
    Guid Id,
    Guid TenantId,
    Guid SubscriptionId,
    string PaypalSubscriptionId,
    string PaypalVaultTokenId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int IncludedMinutes,
    int UsedMinutes,
    int OverageMinutes,
    decimal OverageRateUsd,
    decimal AmountUsd,
    string Status,
    string? PaypalOrderId,
    string? PaypalCaptureId,
    string? Failure,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SettledAt,
    string? CollectedOrderId = null,
    string? CollectedCaptureId = null,
    DateTimeOffset? CollectedAt = null);

/// <summary>The period's charge, and whether this close recorded it. Only the caller that recorded a charge may send it to PayPal.</summary>
public sealed record VoiceBillingPeriodClosed(VoiceOverageCharge Charge, bool Created);

/// <summary>
/// The outcome of a charge that was being made: <see cref="VoiceOverageStatuses.Charged"/> with PayPal's order and capture ids, or
/// <see cref="VoiceOverageStatuses.Failed"/> with the reason. A charge is settled once.
/// </summary>
public sealed record SettleVoiceOverageChargeCommand(string Status, string? PaypalOrderId, string? PaypalCaptureId, string? Failure);

/// <summary>
/// Move the subscription to its next period, from the end it still has (<see cref="FromPeriodEnd"/>) to <see cref="NewPeriodEnd"/>,
/// PayPal's next billing time. The new period starts where the old one ended, so no call falls between two periods. Refused until
/// the old period is closed and its charge settled.
/// </summary>
public sealed record AdvanceVoiceBillingPeriodCommand(Guid SubscriptionId, DateTimeOffset FromPeriodEnd, DateTimeOffset NewPeriodEnd);

public sealed record VoiceBillingPeriod(Guid SubscriptionId, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd);

/// <summary>Record the status PayPal now gives the subscription (SUSPENDED, CANCELLED, EXPIRED). A status other than ACTIVE stops the period from being billed again.</summary>
public sealed record SetVoiceBillingStatusCommand(Guid SubscriptionId, string BillingStatus);

// ----- Standing: suspension, answering, cancellation and the owner's own payment -----

/// <summary>
/// Where a tenant stands with billing: its status, since when it is suspended, whether its number has stopped answering, its
/// subscription, and the overage charges the automatic charge could not take and nobody has paid since. A tenant may be restored to
/// active only when that list is empty and PayPal holds the subscription ACTIVE.
/// </summary>
public sealed record VoiceBillingStanding(
    Guid TenantId,
    string TenantStatus,
    DateTimeOffset? SuspendedAt,
    DateTimeOffset? AnsweringStoppedAt,
    Guid SubscriptionId,
    string TierCode,
    string BillingStatus,
    string PaypalSubscriptionId,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset CurrentPeriodEnd,
    IReadOnlyList<VoiceOverageCharge> UnpaidCharges);

/// <summary>A tenant the sweep found suspended.</summary>
public sealed record VoiceSuspendedTenant(Guid TenantId);

/// <summary>The owner paid a failed overage charge through PayPal checkout: the order and capture that took it.</summary>
public sealed record CollectVoiceOverageChargeCommand(string PaypalOrderId, string PaypalCaptureId);
