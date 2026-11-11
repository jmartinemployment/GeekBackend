using GeekApplication.Models.Crm;
using GeekApplication.Results;
using GeekRepository.Data.Entities.Crm;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeekRepository.Repositories.Crm;

// Phase 2: the catalog, a deal's line items and what a customer holds (docs/GEEK-CRM-DATA-MODEL.md 4.12 to 4.14).
public sealed partial class CrmRepository
{
    // ----- Catalog -----

    public Task<Result<IReadOnlyList<CrmProduct>>> ListProductsAsync(Guid tenantId, string? kind, bool includeRetired, CancellationToken ct = default) =>
        RunAsync("list_products", tenantId, async () =>
        {
            if (kind is not null && !ProductKinds.All.Contains(kind)) return Invalid<IReadOnlyList<CrmProduct>>($"Unknown product kind '{kind}'.");
            var query = db.Products.AsNoTracking().Where(p => p.TenantId == tenantId);
            if (kind is not null) query = query.Where(p => p.Kind == kind);
            if (!includeRetired) query = query.Where(p => p.IsActive);
            var rows = await query.OrderBy(p => p.Name).ToListAsync(ct);
            return Result<IReadOnlyList<CrmProduct>>.Success(rows.Select(ToProduct).ToList());
        }, ct);

    public Task<Result<CrmProduct>> CreateProductAsync(CreateProductCommand command, CancellationToken ct = default) =>
        RunAsync("create_product", command.TenantId, async () =>
        {
            var problem = CrmProductRules.ValidateProduct(command.Name, command.Code, command.Kind, command.Category, command.UseCasePath, command.Billing);
            if (problem is not null) return Invalid<CrmProduct>(problem);
            var partner = await CheckPartnerAsync(command.TenantId, command.PartnerParticipantId, ct);
            if (partner is not null) return Invalid<CrmProduct>(partner);

            var code = command.Code.Trim();
            if (await db.Products.AnyAsync(p => p.TenantId == command.TenantId && p.Code == code, ct))
                return Result<CrmProduct>.Failure($"A product with code '{code}' already exists.", CrmFailureCodes.Conflict);

            var now = Now();
            var row = new CrmProductRow
            {
                Id = Guid.NewGuid(), TenantId = command.TenantId, Name = command.Name.Trim(), Code = code, Kind = command.Kind,
                Category = command.Category, UseCasePath = command.UseCasePath, PartnerParticipantId = command.PartnerParticipantId,
                Billing = command.Billing, IsActive = true, CreatedAt = now, UpdatedAt = now,
            };
            db.Products.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Result<CrmProduct>.Failure($"A product with code '{code}' already exists.", CrmFailureCodes.Conflict);
            }
            return Result<CrmProduct>.Success(ToProduct(row));
        }, ct);

    public Task<Result<CrmProduct>> UpdateProductAsync(Guid productId, UpdateProductCommand command, CancellationToken ct = default) =>
        RunAsync("update_product", command.TenantId, async () =>
        {
            var row = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == command.TenantId, ct);
            if (row is null) return NotFound<CrmProduct>("Product not found.");
            var problem = CrmProductRules.ValidateProduct(command.Name, null, row.Kind, command.Category, command.UseCasePath, command.Billing);
            if (problem is not null) return Invalid<CrmProduct>(problem);
            var partner = await CheckPartnerAsync(command.TenantId, command.PartnerParticipantId, ct);
            if (partner is not null) return Invalid<CrmProduct>(partner);

            row.Name = command.Name.Trim();
            row.Category = command.Category;
            row.UseCasePath = command.UseCasePath;
            row.PartnerParticipantId = command.PartnerParticipantId;
            row.Billing = command.Billing;
            row.IsActive = command.IsActive;
            row.UpdatedAt = Now();
            await db.SaveChangesAsync(ct);
            return Result<CrmProduct>.Success(ToProduct(row));
        }, ct);

    /// <summary>A product's partner is an organization participant of the tenant (Melio, Intuit), or none.</summary>
    private async Task<string?> CheckPartnerAsync(Guid tenantId, Guid? partnerId, CancellationToken ct)
    {
        if (partnerId is not { } id) return null;
        var partner = await db.Participants.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, ct);
        if (partner is null) return "Unknown partner participant.";
        return partner.Kind == ParticipantKinds.Organization ? null : "A product's partner must be an organization.";
    }

    // ----- Deal line items -----

    public Task<Result<CrmDealLineItems>> ListLineItemsAsync(Guid tenantId, Guid dealId, CancellationToken ct = default) =>
        RunAsync("list_line_items", tenantId, async () =>
        {
            var deal = await db.Deals.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dealId && d.TenantId == tenantId, ct);
            if (deal is null) return NotFound<CrmDealLineItems>("Deal not found.");
            return Result<CrmDealLineItems>.Success(await LoadLineItemsAsync(deal, ct));
        }, ct);

    public Task<Result<CrmDealLineItems>> AddLineItemAsync(Guid dealId, AddLineItemCommand command, CancellationToken ct = default) =>
        RunAsync("add_line_item", command.TenantId, async () =>
        {
            var problem = CrmProductRules.ValidateLineItem(command.Quantity, command.UnitPrice, command.DiscountPct);
            if (problem is not null) return Invalid<CrmDealLineItems>(problem);
            var (deal, stage, refusal) = await LockOpenDealAsync<CrmDealLineItems>(command.TenantId, dealId, ct);
            if (refusal is not null) return refusal;
            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == command.ProductId && p.TenantId == command.TenantId, ct);
            if (product is null) return NotFound<CrmDealLineItems>("Product not found.");
            if (!product.IsActive) return Invalid<CrmDealLineItems>("A retired product cannot be added to a deal.");

            db.DealLineItems.Add(new CrmDealLineItemRow
            {
                Id = Guid.NewGuid(), TenantId = command.TenantId, DealId = deal!.Id, ProductId = product.Id, Quantity = command.Quantity,
                UnitPrice = command.UnitPrice, IsRecurring = command.IsRecurring ?? product.Billing == BillingPeriods.Monthly,
                DiscountPct = command.DiscountPct, Currency = Currencies.Usd, CreatedAt = Now(),
            });
            await db.SaveChangesAsync(ct);
            return Result<CrmDealLineItems>.Success(await SumDealAsync(deal, ct));
        }, ct);

    public Task<Result<CrmDealLineItems>> UpdateLineItemAsync(Guid dealId, Guid lineItemId, UpdateLineItemCommand command, CancellationToken ct = default) =>
        RunAsync("update_line_item", command.TenantId, async () =>
        {
            var problem = CrmProductRules.ValidateLineItem(command.Quantity, command.UnitPrice, command.DiscountPct);
            if (problem is not null) return Invalid<CrmDealLineItems>(problem);
            var (deal, _, refusal) = await LockOpenDealAsync<CrmDealLineItems>(command.TenantId, dealId, ct);
            if (refusal is not null) return refusal;
            var item = await db.DealLineItems.FirstOrDefaultAsync(i => i.Id == lineItemId && i.DealId == dealId && i.TenantId == command.TenantId, ct);
            if (item is null) return NotFound<CrmDealLineItems>("Line item not found.");

            item.Quantity = command.Quantity;
            item.UnitPrice = command.UnitPrice;
            item.IsRecurring = command.IsRecurring;
            item.DiscountPct = command.DiscountPct;
            await db.SaveChangesAsync(ct);
            return Result<CrmDealLineItems>.Success(await SumDealAsync(deal!, ct));
        }, ct);

    public Task<Result<CrmDealLineItems>> DeleteLineItemAsync(Guid tenantId, Guid dealId, Guid lineItemId, CancellationToken ct = default) =>
        RunAsync("delete_line_item", tenantId, async () =>
        {
            var (deal, _, refusal) = await LockOpenDealAsync<CrmDealLineItems>(tenantId, dealId, ct);
            if (refusal is not null) return refusal;
            var item = await db.DealLineItems.FirstOrDefaultAsync(i => i.Id == lineItemId && i.DealId == dealId && i.TenantId == tenantId, ct);
            if (item is null) return NotFound<CrmDealLineItems>("Line item not found.");

            db.DealLineItems.Remove(item);
            await db.SaveChangesAsync(ct);
            return Result<CrmDealLineItems>.Success(await SumDealAsync(deal!, ct));
        }, ct);

    /// <summary>The deal row, locked until the transaction ends so a win and a line item change wait for each other.</summary>
    private async Task<CrmDealRow?> LockDealAsync(Guid tenantId, Guid dealId, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            return await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.TenantId == tenantId, ct);
        var rows = await db.Deals
            .FromSqlInterpolated($"SELECT * FROM geek_crm.deals WHERE id = {dealId} AND tenant_id = {tenantId} FOR UPDATE")
            .ToListAsync(ct);
        return rows.FirstOrDefault();
    }

    /// <summary>Locks the deal and refuses when it is missing or already won (its line items are frozen once it is).</summary>
    private async Task<(CrmDealRow? Deal, CrmStageRow? Stage, Result<T>? Refusal)> LockOpenDealAsync<T>(Guid tenantId, Guid dealId, CancellationToken ct)
    {
        var deal = await LockDealAsync(tenantId, dealId, ct);
        if (deal is null) return (null, null, NotFound<T>("Deal not found."));
        var stage = await db.Stages.AsNoTracking().FirstAsync(s => s.Id == deal.StageId, ct);
        if (stage.Outcome == StageOutcomes.Won)
            return (deal, stage, Result<T>.Failure("The deal is won, so its line items are frozen. Change the holdings, or make a new deal.", CrmFailureCodes.Conflict));
        return (deal, stage, null);
    }

    private async Task<CrmDealLineItems> LoadLineItemsAsync(CrmDealRow deal, CancellationToken ct)
    {
        var rows = await (
            from i in db.DealLineItems.AsNoTracking()
            join p in db.Products on i.ProductId equals p.Id
            where i.DealId == deal.Id && i.TenantId == deal.TenantId
            orderby i.CreatedAt, i.Id
            select new { i, p.Name }).ToListAsync(ct);
        return new CrmDealLineItems(deal.Id, rows.Select(r => ToLineItem(r.i, r.Name)).ToList(), deal.EstimatedValue, deal.RecurringValueMonthly);
    }

    /// <summary>When a deal has line items, its estimated value is the sum of the one-time ones and its monthly recurring value the sum of the recurring ones.</summary>
    private async Task<CrmDealLineItems> SumDealAsync(CrmDealRow deal, CancellationToken ct)
    {
        var items = await db.DealLineItems.AsNoTracking().Where(i => i.DealId == deal.Id && i.TenantId == deal.TenantId).ToListAsync(ct);
        if (items.Count > 0)
        {
            deal.EstimatedValue = items.Where(i => !i.IsRecurring).Sum(i => CrmProductRules.LineTotal(i.Quantity, i.UnitPrice, i.DiscountPct));
            deal.RecurringValueMonthly = items.Where(i => i.IsRecurring).Sum(i => CrmProductRules.LineTotal(i.Quantity, i.UnitPrice, i.DiscountPct));
            deal.UpdatedAt = Now();
            await db.SaveChangesAsync(ct);
        }
        return await LoadLineItemsAsync(deal, ct);
    }

    // ----- Win -----

    public Task<Result<WinDealResult>> WinDealAsync(Guid tenantId, Guid dealId, string changedBy, CancellationToken ct = default) =>
        RunAsync("win_deal", tenantId, async () =>
        {
            var deal = await LockDealAsync(tenantId, dealId, ct);
            if (deal is null) return NotFound<WinDealResult>("Deal not found.");
            var stages = await db.Stages.AsNoTracking().Where(s => s.PipelineId == deal.PipelineId).OrderBy(s => s.Position).ToListAsync(ct);
            var won = stages.FirstOrDefault(s => s.Outcome == StageOutcomes.Won);
            if (won is null) return Invalid<WinDealResult>("The deal's pipeline has no Won stage.");

            var items = await db.DealLineItems.AsNoTracking().Where(i => i.DealId == deal.Id && i.TenantId == tenantId).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToListAsync(ct);
            if (items.Count == 0) return Invalid<WinDealResult>("A deal needs at least one line item to be won.");

            var primary = await db.DealParticipants.AsNoTracking()
                .Where(dp => dp.DealId == deal.Id && dp.Role == DealParticipantRoles.PrimaryContact).Select(dp => (Guid?)dp.ParticipantId).FirstOrDefaultAsync(ct);
            var holder = deal.OrganizationParticipantId ?? primary;
            if (holder is null) return Invalid<WinDealResult>("A deal needs an organization or a primary contact to hold what it sold.");

            var now = Now();
            if (deal.StageId != won.Id)
            {
                if (RequiresPrimaryContact(won, stages) && primary is null)
                    return Invalid<WinDealResult>($"A deal needs a primary contact before it can move to '{won.Name}'.");
                db.DealStageHistory.Add(new CrmDealStageHistoryRow
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, DealId = deal.Id, FromStageId = deal.StageId, ToStageId = won.Id,
                    ChangedAt = now, ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? "unknown" : changedBy,
                });
                deal.StageId = won.Id;
                deal.ClosedAt = now;
                deal.LostReason = null;
                deal.UpdatedAt = now;
            }

            var itemIds = items.Select(i => i.Id).ToList();
            var held = (await db.CustomerHoldings.AsNoTracking().Where(h => h.TenantId == tenantId && h.DealLineItemId != null && itemIds.Contains(h.DealLineItemId.Value))
                .Select(h => h.DealLineItemId!.Value).ToListAsync(ct)).ToHashSet();
            var created = 0;
            foreach (var item in items.Where(i => !held.Contains(i.Id)))
            {
                var total = CrmProductRules.LineTotal(item.Quantity, item.UnitPrice, item.DiscountPct);
                db.CustomerHoldings.Add(new CrmCustomerHoldingRow
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ParticipantId = holder.Value, ProductId = item.ProductId, DealId = deal.Id,
                    DealLineItemId = item.Id, Status = HoldingStatuses.Active, StartOn = DateOnly.FromDateTime(now), Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice, DiscountPct = item.DiscountPct, RecurringAmountMonthly = item.IsRecurring ? total : null,
                    OneTimeAmount = item.IsRecurring ? null : total, Currency = item.Currency, CreatedAt = now, UpdatedAt = now,
                });
                created++;
            }
            await db.SaveChangesAsync(ct);

            var holdings = await LoadHoldingsAsync(tenantId, h => h.DealId == deal.Id, ct);
            return Result<WinDealResult>.Success(new WinDealResult(ToDeal(deal, won), holdings, created));
        }, ct);

    // ----- Holdings -----

    public Task<Result<IReadOnlyList<CrmHolding>>> ListHoldingsAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
        RunAsync("list_holdings", tenantId, async () =>
        {
            if (!await db.Participants.AnyAsync(p => p.Id == participantId && p.TenantId == tenantId, ct))
                return NotFound<IReadOnlyList<CrmHolding>>("Participant not found.");
            return Result<IReadOnlyList<CrmHolding>>.Success(await LoadHoldingsAsync(tenantId, h => h.ParticipantId == participantId, ct));
        }, ct);

    public Task<Result<CrmHolding>> CreateHoldingAsync(CreateHoldingCommand command, CancellationToken ct = default) =>
        RunAsync("create_holding", command.TenantId, async () =>
        {
            if (!await db.Participants.AnyAsync(p => p.Id == command.ParticipantId && p.TenantId == command.TenantId, ct))
                return NotFound<CrmHolding>("Participant not found.");
            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == command.ProductId && p.TenantId == command.TenantId, ct);
            if (product is null) return NotFound<CrmHolding>("Product not found.");
            if (!product.IsActive) return Invalid<CrmHolding>("A retired product cannot be held by a new customer.");
            var problem = CrmProductRules.ValidateHolding(command.Details, product.Kind);
            if (problem is not null) return Invalid<CrmHolding>(problem);

            var now = Now();
            var row = new CrmCustomerHoldingRow
            {
                Id = Guid.NewGuid(), TenantId = command.TenantId, ParticipantId = command.ParticipantId, ProductId = product.Id,
                Currency = Currencies.Usd, CreatedAt = now,
            };
            Apply(row, command.Details, now);
            db.CustomerHoldings.Add(row);
            await db.SaveChangesAsync(ct);
            return Result<CrmHolding>.Success(ToHolding(row, product));
        }, ct);

    public Task<Result<CrmHolding>> UpdateHoldingAsync(Guid holdingId, UpdateHoldingCommand command, CancellationToken ct = default) =>
        RunAsync("update_holding", command.TenantId, async () =>
        {
            var row = await db.CustomerHoldings.FirstOrDefaultAsync(h => h.Id == holdingId && h.TenantId == command.TenantId, ct);
            if (row is null) return NotFound<CrmHolding>("Holding not found.");
            var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == row.ProductId, ct);
            var problem = CrmProductRules.ValidateHolding(command.Details, product.Kind);
            if (problem is not null) return Invalid<CrmHolding>(problem);

            Apply(row, command.Details, Now());
            await db.SaveChangesAsync(ct);
            return Result<CrmHolding>.Success(ToHolding(row, product));
        }, ct);

    private async Task<List<CrmHolding>> LoadHoldingsAsync(Guid tenantId, System.Linq.Expressions.Expression<Func<CrmCustomerHoldingRow, bool>> filter, CancellationToken ct)
    {
        var rows = await (
            from h in db.CustomerHoldings.AsNoTracking().Where(filter)
            join p in db.Products on h.ProductId equals p.Id
            where h.TenantId == tenantId
            orderby h.CreatedAt, h.Id
            select new { h, p }).ToListAsync(ct);
        return rows.Select(x => ToHolding(x.h, x.p)).ToList();
    }

    private static void Apply(CrmCustomerHoldingRow row, HoldingDetails d, DateTime now)
    {
        row.Status = d.Status;
        row.StartOn = d.StartOn;
        row.EndOn = d.EndOn;
        row.Quantity = d.Quantity;
        row.UnitPrice = d.UnitPrice;
        row.DiscountPct = d.DiscountPct;
        row.RecurringAmountMonthly = d.RecurringAmountMonthly;
        row.OneTimeAmount = d.OneTimeAmount;
        row.Arrangement = d.Arrangement;
        row.ReferralFee = d.ReferralFee;
        row.ReferralFeeIsRecurring = d.ReferralFeeIsRecurring;
        row.ReferralPaidOn = d.ReferralPaidOn;
        row.PartnerCostMonthly = d.PartnerCostMonthly;
        row.ExternalRef = d.ExternalRef;
        row.UpdatedAt = now;
    }

    // ----- Mapping -----

    private static CrmProduct ToProduct(CrmProductRow p) => new(
        p.Id, p.Name, p.Code, p.Kind, p.Category, p.UseCasePath, p.PartnerParticipantId, p.Billing, p.IsActive, p.CreatedAt, p.UpdatedAt);

    private static CrmDealLineItem ToLineItem(CrmDealLineItemRow i, string productName) => new(
        i.Id, i.DealId, i.ProductId, productName, i.Quantity, i.UnitPrice, i.IsRecurring, i.DiscountPct, i.Currency,
        CrmProductRules.LineTotal(i.Quantity, i.UnitPrice, i.DiscountPct), i.CreatedAt);

    private static CrmHolding ToHolding(CrmCustomerHoldingRow h, CrmProductRow p) => new(
        h.Id, h.ParticipantId, h.ProductId, p.Name, p.Kind, h.DealId, h.DealLineItemId, h.Status, h.StartOn, h.EndOn, h.Quantity,
        h.UnitPrice, h.DiscountPct, h.RecurringAmountMonthly, h.OneTimeAmount, h.Currency, h.Arrangement, h.ReferralFee,
        h.ReferralFeeIsRecurring, h.ReferralPaidOn, h.PartnerCostMonthly, h.ExternalRef, h.TenantSubscriptionId, h.CreatedAt, h.UpdatedAt);
}
