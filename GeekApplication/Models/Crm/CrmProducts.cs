using GeekApplication.Results;

namespace GeekApplication.Models.Crm;

// ----- Phase 2: the catalog, a deal's line items and what a customer holds (docs/GEEK-CRM-DATA-MODEL.md 4.12 to 4.14) -----

public sealed record CrmProduct(
    Guid Id,
    string Name,
    string Code,
    string Kind,
    string Category,
    string? UseCasePath,
    Guid? PartnerParticipantId,
    string Billing,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>A catalog entry. Code and kind never change after create: holdings and the receptionist refer to them.</summary>
public sealed record CreateProductCommand(
    Guid TenantId,
    string Name,
    string Code,
    string Kind,
    string Category,
    string? UseCasePath,
    Guid? PartnerParticipantId,
    string Billing);

/// <summary>Everything a product may change. A product is retired with IsActive = false, never deleted.</summary>
public sealed record UpdateProductCommand(
    Guid TenantId,
    string Name,
    string Category,
    string? UseCasePath,
    Guid? PartnerParticipantId,
    string Billing,
    bool IsActive);

public sealed record CrmDealLineItem(
    Guid Id,
    Guid DealId,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    bool IsRecurring,
    decimal DiscountPct,
    string Currency,
    decimal LineTotal,
    DateTime CreatedAt);

/// <summary>A line item with the deal's totals after it, so a caller sees the sums the deal now carries.</summary>
public sealed record CrmDealLineItems(Guid DealId, IReadOnlyList<CrmDealLineItem> Items, decimal? EstimatedValue, decimal? RecurringValueMonthly);

/// <summary>
/// The price the deal is sold at. IsRecurring defaults to the product's billing being monthly. When UnitPrice is
/// omitted the catalog price in effect today is used (the monthly price for a recurring line, the one-time price
/// otherwise, for Size when the product is priced by size); if none is set, the line is refused rather than guessed.
/// </summary>
public sealed record AddLineItemCommand(Guid TenantId, Guid ProductId, decimal Quantity, decimal? UnitPrice, bool? IsRecurring, decimal DiscountPct, string? Size = null);

public sealed record UpdateLineItemCommand(Guid TenantId, decimal Quantity, decimal UnitPrice, bool IsRecurring, decimal DiscountPct);

public sealed record CrmHolding(
    Guid Id,
    Guid ParticipantId,
    Guid ProductId,
    string ProductName,
    string ProductKind,
    Guid? DealId,
    Guid? DealLineItemId,
    string Status,
    DateOnly? StartOn,
    DateOnly? EndOn,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPct,
    decimal? RecurringAmountMonthly,
    decimal? OneTimeAmount,
    string Currency,
    string? Arrangement,
    decimal? ReferralFee,
    bool? ReferralFeeIsRecurring,
    DateOnly? ReferralPaidOn,
    decimal? PartnerCostMonthly,
    string? ExternalRef,
    Guid? TenantSubscriptionId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>The fields a person or the service sets on a holding; the same shape creates one and replaces one.</summary>
public sealed record HoldingDetails(
    string Status,
    DateOnly? StartOn,
    DateOnly? EndOn,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPct,
    decimal? RecurringAmountMonthly,
    decimal? OneTimeAmount,
    string? Arrangement,
    decimal? ReferralFee,
    bool? ReferralFeeIsRecurring,
    DateOnly? ReferralPaidOn,
    decimal? PartnerCostMonthly,
    string? ExternalRef);

public sealed record CreateHoldingCommand(Guid TenantId, Guid ParticipantId, Guid ProductId, HoldingDetails Details);

public sealed record UpdateHoldingCommand(Guid TenantId, HoldingDetails Details);

// ----- Prices, pricing rules and uplift tiers -----

public sealed record CrmProductPrice(
    Guid Id,
    Guid ProductId,
    string? Size,
    decimal? Hours,
    decimal? UnitPrice,
    decimal? RecurringPriceMonthly,
    string Currency,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    DateTime CreatedAt);

/// <summary>
/// Sets a product's price from a date (a price may be left blank until it is set). The price in effect for the same
/// size is ended the day before; a date on or before the start of an existing price is refused, because history is kept.
/// </summary>
public sealed record SetProductPriceCommand(Guid TenantId, string? Size, decimal? Hours, decimal? UnitPrice, decimal? RecurringPriceMonthly, DateOnly EffectiveFrom);

public sealed record CrmPricingRules(Guid Id, decimal? HourlyRate, decimal RelatedUseCasePct, string Currency, DateTime UpdatedAt);

public sealed record SetPricingRulesCommand(Guid TenantId, decimal? HourlyRate, decimal RelatedUseCasePct);

public sealed record CrmUpliftTier(Guid Id, decimal BookedPctFrom, decimal UpliftPct);

public sealed record UpliftTierInput(decimal BookedPctFrom, decimal UpliftPct);

/// <summary>Replaces the tenant's whole tier list in one transaction; a booked level appears once.</summary>
public sealed record ReplaceUpliftTiersCommand(Guid TenantId, IReadOnlyList<UpliftTierInput> Tiers);

// ----- Tenant subscriptions (platform-scoped) -----

public sealed record CrmTenantSubscription(Guid Id, Guid SubscriberTenantId, string ProductCode, string? Plan, string Status, DateOnly StartOn, DateOnly? EndOn, DateTime CreatedAt);

/// <summary>
/// A tenant may use a multi-tenant Geek product. ProductCode must be an active geek_saas product of the selling tenant.
/// HoldingId, when given, links the selling tenant's own holding of that product to the subscription.
/// </summary>
public sealed record CreateTenantSubscriptionCommand(Guid SubscriberTenantId, string ProductCode, string? Plan, string Status, DateOnly StartOn, DateOnly? EndOn, Guid? HoldingId);

public sealed record UpdateTenantSubscriptionCommand(string? Plan, string Status, DateOnly? EndOn);

/// <summary>What winning a deal did. Created is false on a repeat call, which makes nothing new.</summary>
public sealed record WinDealResult(CrmDeal Deal, IReadOnlyList<CrmHolding> Holdings, int HoldingsCreated);

/// <summary>The rules every writer of these records applies, so the repository, the controllers and the tests share one definition.</summary>
public static class CrmProductRules
{
    public const int NameLimit = 200;
    public const int CodeLimit = 80;
    public const int PathLimit = 300;
    public const int RefLimit = 200;
    public const decimal MaxMoney = 9_999_999_999.99m;

    public static string? ValidateProduct(string name, string? code, string kind, string category, string? useCasePath, string billing)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameLimit) return $"A product needs a name of at most {NameLimit} characters.";
        if (code is not null && (string.IsNullOrWhiteSpace(code) || code.Trim().Length > CodeLimit || !CodeIsLowerKebab(code.Trim())))
            return $"A product code is lower-case letters, digits and hyphens, at most {CodeLimit} characters.";
        if (!ProductKinds.All.Contains(kind)) return $"Unknown product kind '{kind}'. Use one of: {string.Join(", ", ProductKinds.All)}.";
        if (!ProductCategories.All.Contains(category)) return $"Unknown category '{category}'. Use one of: {string.Join(", ", ProductCategories.All)}.";
        if (!BillingPeriods.All.Contains(billing)) return $"Unknown billing '{billing}'. Use one of: {string.Join(", ", BillingPeriods.All)}.";
        if (useCasePath is not null && (useCasePath.Length > PathLimit || !useCasePath.StartsWith('/')))
            return $"A use case path starts with '/' and is at most {PathLimit} characters.";
        return null;
    }

    private static bool CodeIsLowerKebab(string code) =>
        code.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') && !code.StartsWith('-') && !code.EndsWith('-');

    /// <summary>quantity x unit price x (1 - discount / 100), rounded half away from zero to cents.</summary>
    public static decimal LineTotal(decimal quantity, decimal unitPrice, decimal discountPct) =>
        Math.Round(quantity * unitPrice * (1m - discountPct / 100m), 2, MidpointRounding.AwayFromZero);

    public static string? ValidateLineItem(decimal quantity, decimal unitPrice, decimal discountPct)
    {
        if (quantity <= 0 || quantity > 99_999_999.99m || decimal.Round(quantity, 2) != quantity) return "Quantity is more than zero, with at most two decimals.";
        if (!MoneyOk(unitPrice)) return "Unit price is zero or more, with at most two decimals.";
        if (discountPct < 0 || discountPct > 100 || decimal.Round(discountPct, 2) != discountPct) return "Discount is from 0 to 100, with at most two decimals.";
        return null;
    }

    public static string? ValidatePrice(string? size, decimal? hours, decimal? unitPrice, decimal? recurring)
    {
        if (size is not null && !EstimateSizes.All.Contains(size)) return $"Unknown size '{size}'. Use one of: {string.Join(", ", EstimateSizes.All)}, or leave it empty for a flat price.";
        if (hours is { } h && (h < 0 || h > 99_999.99m || decimal.Round(h, 2) != h)) return "Hours is zero or more, with at most two decimals.";
        if (!MoneyOk(unitPrice) || !MoneyOk(recurring)) return "Prices are zero or more, with at most two decimals.";
        return null;
    }

    public static string? ValidatePricingRules(decimal? hourlyRate, decimal relatedPct)
    {
        if (!MoneyOk(hourlyRate)) return "The hourly rate is zero or more, with at most two decimals.";
        if (relatedPct < 0 || relatedPct > 100 || decimal.Round(relatedPct, 2) != relatedPct) return "The related use case percentage is from 0 to 100, with at most two decimals.";
        return null;
    }

    public static string? ValidateUpliftTiers(IReadOnlyList<UpliftTierInput>? tiers)
    {
        if (tiers is null) return "A tier list is required (it may be empty).";
        foreach (var t in tiers)
            if (t.BookedPctFrom is < 0 or > 100 || t.UpliftPct is < 0 or > 100 || decimal.Round(t.BookedPctFrom, 2) != t.BookedPctFrom || decimal.Round(t.UpliftPct, 2) != t.UpliftPct)
                return "Booked and uplift percentages are from 0 to 100, with at most two decimals.";
        return tiers.Select(t => t.BookedPctFrom).Distinct().Count() == tiers.Count ? null : "A booked level can appear only once.";
    }

    public static string? ValidateSubscription(string productCode, string status, DateOnly startOn, DateOnly? endOn, string? plan)
    {
        if (string.IsNullOrWhiteSpace(productCode) || !CodeIsLowerKebab(productCode.Trim())) return "A subscription needs a product code.";
        if (!HoldingStatuses.All.Contains(status)) return $"Unknown status '{status}'. Use one of: {string.Join(", ", HoldingStatuses.All)}.";
        if (endOn is { } end && end < startOn) return "A subscription cannot end before it starts.";
        if (plan is not null && (string.IsNullOrWhiteSpace(plan) || plan.Length > NameLimit)) return $"A plan is at most {NameLimit} characters.";
        return null;
    }

    public static bool MoneyOk(decimal? amount) => amount is null || (amount >= 0 && amount <= MaxMoney && decimal.Round(amount.Value, 2) == amount);

    /// <summary>A holding's own rules; productKind is the kind of the product it holds.</summary>
    public static string? ValidateHolding(HoldingDetails d, string productKind)
    {
        if (!HoldingStatuses.All.Contains(d.Status)) return $"Unknown status '{d.Status}'. Use one of: {string.Join(", ", HoldingStatuses.All)}.";
        var line = ValidateLineItem(d.Quantity, d.UnitPrice, d.DiscountPct);
        if (line is not null) return line;
        foreach (var amount in new[] { d.RecurringAmountMonthly, d.OneTimeAmount, d.ReferralFee, d.PartnerCostMonthly })
            if (!MoneyOk(amount)) return "Amounts are zero or more, with at most two decimals.";
        if (d.StartOn is { } start && d.EndOn is { } end && end < start) return "A holding cannot end before it starts.";
        if (d.ExternalRef is not null && (string.IsNullOrWhiteSpace(d.ExternalRef) || d.ExternalRef.Length > RefLimit)) return $"An external reference is at most {RefLimit} characters.";

        if (d.Arrangement is not null)
        {
            if (productKind != ProductKinds.PartnerSubscription) return "Only a partner subscription has an arrangement.";
            if (!PartnerArrangements.All.Contains(d.Arrangement)) return $"Unknown arrangement '{d.Arrangement}'. Use one of: {string.Join(", ", PartnerArrangements.All)}.";
        }
        var referral = d.ReferralFee is not null || d.ReferralFeeIsRecurring is not null || d.ReferralPaidOn is not null;
        if (referral && d.Arrangement != PartnerArrangements.Referral) return "Referral fee fields belong to a referral arrangement.";
        if (d.PartnerCostMonthly is not null && d.Arrangement != PartnerArrangements.Resold) return "A partner cost belongs to a resold arrangement.";
        return null;
    }
}
