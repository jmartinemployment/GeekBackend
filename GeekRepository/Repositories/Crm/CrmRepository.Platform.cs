using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using GeekRepository.Data.Entities.Crm;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Crm;

// Phase 2: tenant subscriptions, platform-scoped (docs/GEEK-CRM-DATA-MODEL.md 4.15). Every operation runs as a
// platform operation: the platform flag and the selling tenant (Geek @ Your Spot) are both set, so it may read
// the platform table and link the seller's own holding in one transaction.
public sealed partial class CrmRepository
{
    private static readonly Guid SellerTenantId = CrmSeed.GeekAtYourSpotTenantId;

    private static readonly string[] LiveStatuses = [HoldingStatuses.Proposed, HoldingStatuses.Active, HoldingStatuses.Paused];

    public Task<Result<IReadOnlyList<CrmTenantSubscription>>> ListSubscriptionsAsync(Guid? subscriberTenantId, CancellationToken ct = default) =>
        RunAsync("list_subscriptions", SellerTenantId, async () =>
        {
            var query = db.TenantSubscriptions.AsNoTracking();
            if (subscriberTenantId is { } id) query = query.Where(s => s.SubscriberTenantId == id);
            var rows = await query.OrderByDescending(s => s.StartOn).ThenBy(s => s.ProductCode).ToListAsync(ct);
            return Result<IReadOnlyList<CrmTenantSubscription>>.Success(rows.Select(ToSubscription).ToList());
        }, ct, platform: true);

    public Task<Result<CrmTenantSubscription>> CreateSubscriptionAsync(CreateTenantSubscriptionCommand command, CancellationToken ct = default) =>
        RunAsync("create_subscription", SellerTenantId, async () =>
        {
            var problem = CrmProductRules.ValidateSubscription(command.ProductCode, command.Status, command.StartOn, command.EndOn, command.Plan);
            if (problem is not null) return Invalid<CrmTenantSubscription>(problem);
            if (!await db.Tenants.AnyAsync(t => t.Id == command.SubscriberTenantId, ct)) return NotFound<CrmTenantSubscription>("Subscriber tenant not found.");

            var code = command.ProductCode.Trim();
            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.TenantId == SellerTenantId && p.Code == code && p.Kind == ProductKinds.GeekSaas && p.IsActive, ct);
            if (product is null) return Invalid<CrmTenantSubscription>($"'{code}' is not an active geek_saas product of the selling tenant.");

            if (await db.TenantSubscriptions.AnyAsync(s => s.SubscriberTenantId == command.SubscriberTenantId && s.ProductCode == code && LiveStatuses.Contains(s.Status), ct))
                return Result<CrmTenantSubscription>.Failure("That tenant already has a live subscription to this product. Update it instead.", CrmFailureCodes.Conflict);

            CrmCustomerHoldingRow? holding = null;
            if (command.HoldingId is { } holdingId)
            {
                holding = await db.CustomerHoldings.FirstOrDefaultAsync(h => h.Id == holdingId && h.TenantId == SellerTenantId, ct);
                if (holding is null) return NotFound<CrmTenantSubscription>("Holding not found.");
                if (holding.ProductId != product.Id) return Invalid<CrmTenantSubscription>("The holding is not a holding of that product.");
                if (holding.TenantSubscriptionId is not null) return Result<CrmTenantSubscription>.Failure("The holding is already linked to a subscription.", CrmFailureCodes.Conflict);
            }

            var now = Now();
            var row = new CrmTenantSubscriptionRow
            {
                Id = Guid.NewGuid(), SubscriberTenantId = command.SubscriberTenantId, ProductCode = code, Plan = command.Plan,
                Status = command.Status, StartOn = command.StartOn, EndOn = command.EndOn, CreatedAt = now,
            };
            db.TenantSubscriptions.Add(row);
            if (holding is not null)
            {
                holding.TenantSubscriptionId = row.Id;
                holding.UpdatedAt = now;
            }
            await db.SaveChangesAsync(ct);
            return Result<CrmTenantSubscription>.Success(ToSubscription(row));
        }, ct, platform: true);

    public Task<Result<CrmTenantSubscription>> UpdateSubscriptionAsync(Guid subscriptionId, UpdateTenantSubscriptionCommand command, CancellationToken ct = default) =>
        RunAsync("update_subscription", SellerTenantId, async () =>
        {
            var row = await db.TenantSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
            if (row is null) return NotFound<CrmTenantSubscription>("Subscription not found.");
            var problem = CrmProductRules.ValidateSubscription(row.ProductCode, command.Status, row.StartOn, command.EndOn, command.Plan);
            if (problem is not null) return Invalid<CrmTenantSubscription>(problem);

            row.Plan = command.Plan;
            row.Status = command.Status;
            row.EndOn = command.EndOn;
            await db.SaveChangesAsync(ct);
            return Result<CrmTenantSubscription>.Success(ToSubscription(row));
        }, ct, platform: true);

    private static CrmTenantSubscription ToSubscription(CrmTenantSubscriptionRow s) => new(s.Id, s.SubscriberTenantId, s.ProductCode, s.Plan, s.Status, s.StartOn, s.EndOn, s.CreatedAt);

    // ----- Tenant membership (Phase 4) -----

    public Task<Result<CrmMembership>> GetMembershipAsync(Guid userId, CancellationToken ct = default) =>
        RunAsync("get_membership", SellerTenantId, async () =>
        {
            var row = await db.TenantUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, ct);
            return row is null ? NotFound<CrmMembership>("The user belongs to no tenant.") : Result<CrmMembership>.Success(new CrmMembership(row.TenantId, row.UserId, row.Role));
        }, ct, platform: true);

    public Task<Result<IReadOnlyList<CrmTenantUser>>> ListTenantUsersAsync(Guid? tenantId, CancellationToken ct = default) =>
        RunAsync("list_tenant_users", SellerTenantId, async () =>
        {
            var query = db.TenantUsers.AsNoTracking();
            if (tenantId is { } id) query = query.Where(u => u.TenantId == id);
            var rows = await query.OrderBy(u => u.TenantId).ThenBy(u => u.CreatedAt).ToListAsync(ct);
            return Result<IReadOnlyList<CrmTenantUser>>.Success(rows.Select(ToTenantUser).ToList());
        }, ct, platform: true);

    public Task<Result<CrmTenantUser>> AddTenantUserAsync(AddTenantUserCommand command, CancellationToken ct = default) =>
        RunAsync("add_tenant_user", SellerTenantId, async () =>
        {
            if (!TenantUserRoles.All.Contains(command.Role)) return Invalid<CrmTenantUser>($"Unknown role '{command.Role}'. Use one of: {string.Join(", ", TenantUserRoles.All)}.");
            if (command.UserId == Guid.Empty) return Invalid<CrmTenantUser>("A user id is required.");
            if (!await db.Tenants.AnyAsync(t => t.Id == command.TenantId, ct)) return NotFound<CrmTenantUser>("Tenant not found.");
            if (await db.TenantUsers.AnyAsync(u => u.UserId == command.UserId, ct))
                return Result<CrmTenantUser>.Failure("That user already belongs to a tenant.", CrmFailureCodes.Conflict);

            var row = new CrmTenantUserRow { Id = Guid.NewGuid(), TenantId = command.TenantId, UserId = command.UserId, Role = command.Role, CreatedAt = Now() };
            db.TenantUsers.Add(row);
            await db.SaveChangesAsync(ct);
            return Result<CrmTenantUser>.Success(ToTenantUser(row));
        }, ct, platform: true);

    public Task<Result<CrmTenantUser>> SetTenantUserRoleAsync(Guid id, SetTenantUserRoleCommand command, CancellationToken ct = default) =>
        RunAsync("set_tenant_user_role", SellerTenantId, async () =>
        {
            if (!TenantUserRoles.All.Contains(command.Role)) return Invalid<CrmTenantUser>($"Unknown role '{command.Role}'. Use one of: {string.Join(", ", TenantUserRoles.All)}.");
            var row = await db.TenantUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (row is null) return NotFound<CrmTenantUser>("Membership not found.");
            if (row.Role == TenantUserRoles.Owner && command.Role != TenantUserRoles.Owner && !await HasAnotherOwnerAsync(row, ct))
                return Result<CrmTenantUser>.Failure("The tenant's last owner cannot be demoted.", CrmFailureCodes.Conflict);
            row.Role = command.Role;
            await db.SaveChangesAsync(ct);
            return Result<CrmTenantUser>.Success(ToTenantUser(row));
        }, ct, platform: true);

    public Task<Result<bool>> RemoveTenantUserAsync(Guid id, CancellationToken ct = default) =>
        RunAsync("remove_tenant_user", SellerTenantId, async () =>
        {
            var row = await db.TenantUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (row is null) return NotFound<bool>("Membership not found.");
            if (row.Role == TenantUserRoles.Owner && !await HasAnotherOwnerAsync(row, ct))
                return Result<bool>.Failure("The tenant's last owner cannot be removed.", CrmFailureCodes.Conflict);
            db.TenantUsers.Remove(row);
            await db.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }, ct, platform: true);

    private Task<bool> HasAnotherOwnerAsync(CrmTenantUserRow row, CancellationToken ct) =>
        db.TenantUsers.AnyAsync(u => u.TenantId == row.TenantId && u.Role == TenantUserRoles.Owner && u.Id != row.Id, ct);

    private static CrmTenantUser ToTenantUser(CrmTenantUserRow u) => new(u.Id, u.TenantId, u.UserId, u.Role, u.CreatedAt);
}

