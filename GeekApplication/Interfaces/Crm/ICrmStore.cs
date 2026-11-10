using GeekApplication.Models.Crm;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Crm;

/// <summary>
/// The geek_crm store (docs/GEEK-CRM-DATA-MODEL.md). GeekRepository implements it over its own
/// Postgres and GeekAPI calls it over HTTP (repo/crm/*), so both sides read one contract. Every
/// call is scoped to a tenant; nothing reads or writes across tenants. Failures carry a
/// <see cref="CrmFailureCodes"/> value: a store that is down is never answered as "nothing there".
/// </summary>
public interface ICrmStore
{
    Task<Result<EnsureParticipantResult>> EnsureParticipantByPhoneAsync(EnsureParticipantByPhoneCommand command, CancellationToken ct = default);

    Task<Result<RecordActivityResult>> RecordActivityAsync(RecordActivityCommand command, CancellationToken ct = default);

    /// <summary>Sets or clears SMS opt-out on every phone contact point with this number in the tenant. Success with false when the number is unknown.</summary>
    Task<Result<bool>> SetSmsOptOutAsync(SetSmsOptOutCommand command, CancellationToken ct = default);

    Task<Result<ImportProspectsResult>> ImportProspectsAsync(ImportProspectsCommand command, CancellationToken ct = default);

    Task<Result<IReadOnlyList<CrmParticipantSummary>>> ListParticipantsAsync(Guid tenantId, string? lifecycleStage, string? kind, int limit, CancellationToken ct = default);

    Task<Result<CrmParticipant>> GetParticipantAsync(Guid tenantId, Guid participantId, CancellationToken ct = default);

    Task<Result<CrmDeal>> CreateDealAsync(CreateDealCommand command, CancellationToken ct = default);

    Task<Result<CrmDeal>> MoveDealStageAsync(Guid dealId, MoveDealStageCommand command, CancellationToken ct = default);

    Task<Result<IReadOnlyList<CrmDeal>>> ListDealsAsync(Guid tenantId, bool openOnly, int limit, CancellationToken ct = default);
}
