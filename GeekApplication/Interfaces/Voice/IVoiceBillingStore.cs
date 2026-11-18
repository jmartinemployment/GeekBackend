using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Voice;

/// <summary>
/// Geek-Receptionist's end-of-cycle billing store (docs/Geek-Receptionist.md section 5, item 2): GeekRepository implements it over
/// Postgres. <see cref="ListDueAsync"/> and <see cref="ListSuspendedAsync"/> are the reads across tenants, and they answer only ids and
/// period ends; every other method names its tenant. A bad request, a missing row and a conflict come back as failed results with a <see cref="VoiceFailureCodes"/>
/// value; a database fault that is none of those is raised, never turned into a default.
/// </summary>
public interface IVoiceBillingStore
{
    Task<Result<IReadOnlyList<VoiceSubscriptionDue>>> ListDueAsync(ListVoiceSubscriptionsDueCommand command, CancellationToken ct = default);
    Task<Result<VoiceBillingPeriodClosed>> CloseBillingPeriodAsync(Guid tenantId, CloseVoiceBillingPeriodCommand command, CancellationToken ct = default);
    Task<Result<VoiceOverageCharge>> SettleOverageChargeAsync(Guid tenantId, Guid chargeId, SettleVoiceOverageChargeCommand command, CancellationToken ct = default);
    Task<Result<VoiceBillingPeriod>> AdvanceBillingPeriodAsync(Guid tenantId, AdvanceVoiceBillingPeriodCommand command, CancellationToken ct = default);
    Task<Result<bool>> SetBillingStatusAsync(Guid tenantId, SetVoiceBillingStatusCommand command, CancellationToken ct = default);

    Task<Result<IReadOnlyList<VoiceSuspendedTenant>>> ListSuspendedAsync(CancellationToken ct = default);
    Task<Result<VoiceBillingStanding>> GetStandingAsync(Guid tenantId, CancellationToken ct = default);
    /// <summary>An active tenant becomes suspended from now; a suspended one keeps the time it was first suspended. Any other status is a conflict.</summary>
    Task<Result<VoiceBillingStanding>> SuspendTenantAsync(Guid tenantId, CancellationToken ct = default);
    /// <summary>A suspended tenant becomes active again, refused (conflict) while an overage is unpaid or the subscription is not ACTIVE.</summary>
    Task<Result<VoiceBillingStanding>> RestoreTenantAsync(Guid tenantId, CancellationToken ct = default);
    /// <summary>Records that a suspended tenant's number stops answering; GeekAPI's call route reads this. Refused (conflict) unless the tenant is suspended.</summary>
    Task<Result<VoiceBillingStanding>> MarkAnsweringStoppedAsync(Guid tenantId, CancellationToken ct = default);
    /// <summary>The tenant is cancelled. Final: nothing moves a tenant out of cancelled.</summary>
    Task<Result<VoiceBillingStanding>> CancelTenantAsync(Guid tenantId, CancellationToken ct = default);
    /// <summary>Records that a cancelled tenant's Twilio subaccount, and with it the number, was closed.</summary>
    Task<Result<bool>> MarkTelephonyReleasedAsync(Guid tenantId, CancellationToken ct = default);
    /// <summary>A failed charge the owner has now paid. Paying the same charge again with the same order answers it unchanged; with another order it is a conflict.</summary>
    Task<Result<VoiceOverageCharge>> CollectOverageChargeAsync(Guid tenantId, Guid chargeId, CollectVoiceOverageChargeCommand command, CancellationToken ct = default);
}
