using GeekApplication.Models.Crm;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Crm;

/// <summary>
/// Platform-scoped Geek-CRM data: which tenant may use which multi-tenant Geek product. Reached only through
/// repo/platform/* behind the platform-admin key; a tenant-scoped store can never read it.
/// </summary>
public interface ICrmPlatformStore
{
    Task<Result<IReadOnlyList<CrmTenantSubscription>>> ListSubscriptionsAsync(Guid? subscriberTenantId, CancellationToken ct = default);

    Task<Result<CrmTenantSubscription>> CreateSubscriptionAsync(CreateTenantSubscriptionCommand command, CancellationToken ct = default);

    Task<Result<CrmTenantSubscription>> UpdateSubscriptionAsync(Guid subscriptionId, UpdateTenantSubscriptionCommand command, CancellationToken ct = default);
}
