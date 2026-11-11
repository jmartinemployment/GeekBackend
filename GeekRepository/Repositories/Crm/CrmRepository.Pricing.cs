using GeekApplication.Models.Crm;
using GeekApplication.Results;
using GeekRepository.Data.Entities.Crm;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Crm;

// Phase 2: prices with history, the tenant's pricing rules and uplift tiers (docs/GEEK-CRM-DATA-MODEL.md 4.12).
public sealed partial class CrmRepository
{
    public Task<Result<IReadOnlyList<CrmProductPrice>>> ListPricesAsync(Guid tenantId, Guid productId, bool includeHistory, CancellationToken ct = default) =>
        RunAsync("list_prices", tenantId, async () =>
        {
            if (!await db.Products.AnyAsync(p => p.Id == productId && p.TenantId == tenantId, ct)) return NotFound<IReadOnlyList<CrmProductPrice>>("Product not found.");
            var today = DateOnly.FromDateTime(Now());
            var query = db.ProductPrices.AsNoTracking().Where(p => p.ProductId == productId && p.TenantId == tenantId);
            if (!includeHistory) query = query.Where(p => p.EffectiveTo == null || p.EffectiveTo >= today);
            var rows = await query.OrderByDescending(p => p.EffectiveFrom).ThenBy(p => p.Size).ToListAsync(ct);
            return Result<IReadOnlyList<CrmProductPrice>>.Success(rows.Select(ToPrice).ToList());
        }, ct);

    public Task<Result<CrmProductPrice>> GetPriceInEffectAsync(Guid tenantId, Guid productId, string? size, DateOnly? on, CancellationToken ct = default) =>
        RunAsync("get_price_in_effect", tenantId, async () =>
        {
            if (size is not null && !EstimateSizes.All.Contains(size)) return Invalid<CrmProductPrice>($"Unknown size '{size}'.");
            if (!await db.Products.AnyAsync(p => p.Id == productId && p.TenantId == tenantId, ct)) return NotFound<CrmProductPrice>("Product not found.");
            var price = await FindPriceInEffectAsync(tenantId, productId, size, on ?? DateOnly.FromDateTime(Now()), ct);
            return price is null ? NotFound<CrmProductPrice>("No price is set for that product, size and date.") : Result<CrmProductPrice>.Success(ToPrice(price));
        }, ct);

    private Task<CrmProductPriceRow?> FindPriceInEffectAsync(Guid tenantId, Guid productId, string? size, DateOnly on, CancellationToken ct) =>
        db.ProductPrices.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.ProductId == productId && p.Size == size && p.EffectiveFrom <= on && (p.EffectiveTo == null || p.EffectiveTo >= on))
            .OrderByDescending(p => p.EffectiveFrom).FirstOrDefaultAsync(ct);

    public Task<Result<CrmProductPrice>> SetPriceAsync(Guid productId, SetProductPriceCommand command, CancellationToken ct = default) =>
        RunAsync("set_price", command.TenantId, async () =>
        {
            var problem = CrmProductRules.ValidatePrice(command.Size, command.Hours, command.UnitPrice, command.RecurringPriceMonthly);
            if (problem is not null) return Invalid<CrmProductPrice>(problem);

            // The product row is locked so two price changes for one product wait for each other and cannot overlap.
            var product = db.Database.IsNpgsql()
                ? (await db.Products.FromSqlInterpolated($"SELECT * FROM geek_crm.products WHERE id = {productId} AND tenant_id = {command.TenantId} FOR UPDATE").ToListAsync(ct)).FirstOrDefault()
                : await db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == command.TenantId, ct);
            if (product is null) return NotFound<CrmProductPrice>("Product not found.");

            var existing = await db.ProductPrices.Where(p => p.ProductId == productId && p.TenantId == command.TenantId && p.Size == command.Size).ToListAsync(ct);
            if (existing.Any(p => p.EffectiveFrom >= command.EffectiveFrom))
                return Invalid<CrmProductPrice>("A price for this size already starts on or after that date. History is kept, so set a later date.");
            foreach (var overlapping in existing.Where(p => p.EffectiveTo is null || p.EffectiveTo >= command.EffectiveFrom))
                overlapping.EffectiveTo = command.EffectiveFrom.AddDays(-1);

            var row = new CrmProductPriceRow
            {
                Id = Guid.NewGuid(), TenantId = command.TenantId, ProductId = productId, Size = command.Size, Hours = command.Hours,
                UnitPrice = command.UnitPrice, RecurringPriceMonthly = command.RecurringPriceMonthly, Currency = Currencies.Usd,
                EffectiveFrom = command.EffectiveFrom, CreatedAt = Now(),
            };
            db.ProductPrices.Add(row);
            await db.SaveChangesAsync(ct);
            return Result<CrmProductPrice>.Success(ToPrice(row));
        }, ct);

    public Task<Result<CrmPricingRules>> GetPricingRulesAsync(Guid tenantId, CancellationToken ct = default) =>
        RunAsync("get_pricing_rules", tenantId, async () =>
        {
            var row = await db.PricingRules.AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenantId, ct);
            return row is null ? NotFound<CrmPricingRules>("The tenant has no pricing rules yet.") : Result<CrmPricingRules>.Success(ToRules(row));
        }, ct);

    public Task<Result<CrmPricingRules>> SetPricingRulesAsync(SetPricingRulesCommand command, CancellationToken ct = default) =>
        RunAsync("set_pricing_rules", command.TenantId, async () =>
        {
            var problem = CrmProductRules.ValidatePricingRules(command.HourlyRate, command.RelatedUseCasePct);
            if (problem is not null) return Invalid<CrmPricingRules>(problem);
            var row = await db.PricingRules.FirstOrDefaultAsync(r => r.TenantId == command.TenantId, ct);
            if (row is null)
            {
                row = new CrmPricingRulesRow { Id = Guid.NewGuid(), TenantId = command.TenantId, Currency = Currencies.Usd };
                db.PricingRules.Add(row);
            }
            row.HourlyRate = command.HourlyRate;
            row.RelatedUseCasePct = command.RelatedUseCasePct;
            row.UpdatedAt = Now();
            await db.SaveChangesAsync(ct);
            return Result<CrmPricingRules>.Success(ToRules(row));
        }, ct);

    public Task<Result<IReadOnlyList<CrmUpliftTier>>> ListUpliftTiersAsync(Guid tenantId, CancellationToken ct = default) =>
        RunAsync("list_uplift_tiers", tenantId, async () =>
        {
            var rows = await db.PricingUpliftTiers.AsNoTracking().Where(t => t.TenantId == tenantId).OrderBy(t => t.BookedPctFrom).ToListAsync(ct);
            return Result<IReadOnlyList<CrmUpliftTier>>.Success(rows.Select(t => new CrmUpliftTier(t.Id, t.BookedPctFrom, t.UpliftPct)).ToList());
        }, ct);

    public Task<Result<IReadOnlyList<CrmUpliftTier>>> ReplaceUpliftTiersAsync(ReplaceUpliftTiersCommand command, CancellationToken ct = default) =>
        RunAsync("replace_uplift_tiers", command.TenantId, async () =>
        {
            var problem = CrmProductRules.ValidateUpliftTiers(command.Tiers);
            if (problem is not null) return Invalid<IReadOnlyList<CrmUpliftTier>>(problem);

            // Deleted first and saved on its own: the unique key on (tenant, booked level) would otherwise see the new rows before the old are gone.
            db.PricingUpliftTiers.RemoveRange(await db.PricingUpliftTiers.Where(t => t.TenantId == command.TenantId).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            var now = Now();
            var added = command.Tiers.OrderBy(t => t.BookedPctFrom).Select(t => new CrmPricingUpliftTierRow
            {
                Id = Guid.NewGuid(), TenantId = command.TenantId, BookedPctFrom = t.BookedPctFrom, UpliftPct = t.UpliftPct, CreatedAt = now,
            }).ToList();
            db.PricingUpliftTiers.AddRange(added);
            await db.SaveChangesAsync(ct);
            return Result<IReadOnlyList<CrmUpliftTier>>.Success(added.Select(t => new CrmUpliftTier(t.Id, t.BookedPctFrom, t.UpliftPct)).ToList());
        }, ct);

    private static CrmProductPrice ToPrice(CrmProductPriceRow p) => new(
        p.Id, p.ProductId, p.Size, p.Hours, p.UnitPrice, p.RecurringPriceMonthly, p.Currency, p.EffectiveFrom, p.EffectiveTo, p.CreatedAt);

    private static CrmPricingRules ToRules(CrmPricingRulesRow r) => new(r.Id, r.HourlyRate, r.RelatedUseCasePct, r.Currency, r.UpdatedAt);
}
