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

/// <summary>The price is the one the deal is sold at; IsRecurring defaults to the product's billing being monthly.</summary>
public sealed record AddLineItemCommand(Guid TenantId, Guid ProductId, decimal Quantity, decimal UnitPrice, bool? IsRecurring, decimal DiscountPct);

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
