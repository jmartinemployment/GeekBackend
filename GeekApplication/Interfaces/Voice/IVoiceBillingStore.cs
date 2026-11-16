using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Voice;

/// <summary>
/// Geek-Receptionist's end-of-cycle billing store (docs/Geek-Receptionist.md section 5, item 2): GeekRepository implements it over
/// Postgres. <see cref="ListDueAsync"/> is the one read across tenants, and it answers only ids and period ends; every other method
/// names its tenant. A bad request, a missing row and a conflict come back as failed results with a <see cref="VoiceFailureCodes"/>
/// value; a database fault that is none of those is raised, never turned into a default.
/// </summary>
public interface IVoiceBillingStore
{
    Task<Result<IReadOnlyList<VoiceSubscriptionDue>>> ListDueAsync(ListVoiceSubscriptionsDueCommand command, CancellationToken ct = default);
    Task<Result<VoiceBillingPeriodClosed>> CloseBillingPeriodAsync(Guid tenantId, CloseVoiceBillingPeriodCommand command, CancellationToken ct = default);
    Task<Result<VoiceOverageCharge>> SettleOverageChargeAsync(Guid tenantId, Guid chargeId, SettleVoiceOverageChargeCommand command, CancellationToken ct = default);
    Task<Result<VoiceBillingPeriod>> AdvanceBillingPeriodAsync(Guid tenantId, AdvanceVoiceBillingPeriodCommand command, CancellationToken ct = default);
    Task<Result<bool>> SetBillingStatusAsync(Guid tenantId, SetVoiceBillingStatusCommand command, CancellationToken ct = default);
}
