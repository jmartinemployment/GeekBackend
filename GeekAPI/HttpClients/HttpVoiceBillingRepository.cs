using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

/// <summary>
/// /internal/v1/billing/* over the GeekRepository client that sends X-Internal-Api-Key, the <see cref="IVoiceBillingStore"/> GeekAPI's
/// end-of-cycle billing calls. A status is read by <see cref="VoiceRepositoryTransport"/> as every Geek-Receptionist client reads it;
/// nothing here turns a failure into an empty list, so a store that is down is never read as "nothing is due".
/// </summary>
public sealed class HttpVoiceBillingRepository : IVoiceBillingStore
{
    private readonly VoiceRepositoryTransport _transport;

    public HttpVoiceBillingRepository(HttpClient http, ILogger<HttpVoiceBillingRepository> logger)
    {
        _transport = new VoiceRepositoryTransport(http, logger);
    }

    public async Task<Result<IReadOnlyList<VoiceSubscriptionDue>>> ListDueAsync(ListVoiceSubscriptionsDueCommand command, CancellationToken ct = default)
    {
        var result = await _transport.SendAsync<List<VoiceSubscriptionDue>>(HttpMethod.Post, "internal/v1/billing/subscriptions/due", command, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Result<IReadOnlyList<VoiceSubscriptionDue>>.Success(result.Value!)
            : Result<IReadOnlyList<VoiceSubscriptionDue>>.Failure(result.Error ?? "The due subscriptions could not be read.", result.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
    }

    public Task<Result<VoiceBillingPeriodClosed>> CloseBillingPeriodAsync(Guid tenantId, CloseVoiceBillingPeriodCommand command, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceBillingPeriodClosed>(HttpMethod.Post, $"internal/v1/billing/tenants/{tenantId}/periods/close", command, ct);

    public Task<Result<VoiceOverageCharge>> SettleOverageChargeAsync(Guid tenantId, Guid chargeId, SettleVoiceOverageChargeCommand command, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceOverageCharge>(HttpMethod.Post, $"internal/v1/billing/tenants/{tenantId}/overage-charges/{chargeId}/settle", command, ct);

    public Task<Result<VoiceBillingPeriod>> AdvanceBillingPeriodAsync(Guid tenantId, AdvanceVoiceBillingPeriodCommand command, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceBillingPeriod>(HttpMethod.Post, $"internal/v1/billing/tenants/{tenantId}/periods/advance", command, ct);

    public Task<Result<bool>> SetBillingStatusAsync(Guid tenantId, SetVoiceBillingStatusCommand command, CancellationToken ct = default) =>
        _transport.SendAsync<bool>(HttpMethod.Put, $"internal/v1/billing/tenants/{tenantId}/billing-status", command, ct);

    public async Task<Result<IReadOnlyList<VoiceSuspendedTenant>>> ListSuspendedAsync(CancellationToken ct = default)
    {
        var result = await _transport.SendAsync<List<VoiceSuspendedTenant>>(HttpMethod.Post, "internal/v1/billing/tenants/suspended", null, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Result<IReadOnlyList<VoiceSuspendedTenant>>.Success(result.Value!)
            : Result<IReadOnlyList<VoiceSuspendedTenant>>.Failure(result.Error ?? "The suspended tenants could not be read.", result.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
    }

    public Task<Result<VoiceBillingStanding>> GetStandingAsync(Guid tenantId, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceBillingStanding>(HttpMethod.Get, $"internal/v1/billing/tenants/{tenantId}/standing", null, ct);

    public Task<Result<VoiceBillingStanding>> SuspendTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceBillingStanding>(HttpMethod.Post, $"internal/v1/billing/tenants/{tenantId}/suspend", null, ct);

    public Task<Result<VoiceBillingStanding>> RestoreTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceBillingStanding>(HttpMethod.Post, $"internal/v1/billing/tenants/{tenantId}/restore", null, ct);

    public Task<Result<VoiceBillingStanding>> MarkAnsweringStoppedAsync(Guid tenantId, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceBillingStanding>(HttpMethod.Post, $"internal/v1/billing/tenants/{tenantId}/answering-stopped", null, ct);

    public Task<Result<VoiceBillingStanding>> CancelTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceBillingStanding>(HttpMethod.Post, $"internal/v1/billing/tenants/{tenantId}/cancel", null, ct);

    public Task<Result<bool>> MarkTelephonyReleasedAsync(Guid tenantId, CancellationToken ct = default) =>
        _transport.SendAsync<bool>(HttpMethod.Post, $"internal/v1/billing/tenants/{tenantId}/telephony-released", null, ct);

    public Task<Result<VoiceOverageCharge>> CollectOverageChargeAsync(Guid tenantId, Guid chargeId, CollectVoiceOverageChargeCommand command, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceOverageCharge>(HttpMethod.Post, $"internal/v1/billing/tenants/{tenantId}/overage-charges/{chargeId}/collect", command, ct);
}
