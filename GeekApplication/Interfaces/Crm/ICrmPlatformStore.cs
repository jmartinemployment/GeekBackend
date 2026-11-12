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

    /// <summary>The tenant and role of a GeekOAuth user, or not_found when they belong to none.</summary>
    Task<Result<CrmMembership>> GetMembershipAsync(Guid userId, CancellationToken ct = default);

    Task<Result<IReadOnlyList<CrmTenantUser>>> ListTenantUsersAsync(Guid? tenantId, CancellationToken ct = default);

    /// <summary>A user belongs to one tenant: adding a second membership is a conflict.</summary>
    Task<Result<CrmTenantUser>> AddTenantUserAsync(AddTenantUserCommand command, CancellationToken ct = default);

    /// <summary>Refused when it would leave the tenant without an owner.</summary>
    Task<Result<CrmTenantUser>> SetTenantUserRoleAsync(Guid id, SetTenantUserRoleCommand command, CancellationToken ct = default);

    /// <summary>Refused when it would leave the tenant without an owner.</summary>
    Task<Result<bool>> RemoveTenantUserAsync(Guid id, CancellationToken ct = default);
}

