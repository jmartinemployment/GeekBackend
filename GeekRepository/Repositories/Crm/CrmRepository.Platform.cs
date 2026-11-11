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
}
