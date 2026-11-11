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

    // ----- Industry lookup (platform data; not tenant-scoped) -----

    /// <summary>NAICS codes whose code starts with, or whose title contains, the query; optionally one level (2 to 6).</summary>
    Task<Result<IReadOnlyList<CrmNaicsCode>>> SearchNaicsAsync(string? query, int? level, int limit, CancellationToken ct = default);

    // ----- Custom properties -----

    Task<Result<IReadOnlyList<CrmPropertyDefinition>>> ListPropertyDefinitionsAsync(Guid tenantId, string? objectType, bool includeRetired, bool includeSensitive, CancellationToken ct = default);

    Task<Result<CrmPropertyDefinition>> CreatePropertyDefinitionAsync(CreatePropertyDefinitionCommand command, CancellationToken ct = default);

    Task<Result<CrmPropertyDefinition>> UpdatePropertyDefinitionAsync(Guid definitionId, UpdatePropertyDefinitionCommand command, CancellationToken ct = default);

    /// <summary>Hides the property and its values from reads and refuses new values; the stored values are kept for history.</summary>
    Task<Result<CrmPropertyDefinition>> RetirePropertyDefinitionAsync(Guid tenantId, Guid definitionId, CancellationToken ct = default);

    Task<Result<IReadOnlyList<CrmPropertyValue>>> GetPropertyValuesAsync(Guid tenantId, string objectType, Guid recordId, bool includeSensitive, CancellationToken ct = default);

    Task<Result<IReadOnlyList<CrmPropertyValue>>> SetPropertyValuesAsync(SetPropertyValuesCommand command, CancellationToken ct = default);

    // ----- Catalog, deal line items, holdings (Phase 2) -----

    Task<Result<IReadOnlyList<CrmProduct>>> ListProductsAsync(Guid tenantId, string? kind, bool includeRetired, CancellationToken ct = default);

    Task<Result<CrmProduct>> CreateProductAsync(CreateProductCommand command, CancellationToken ct = default);

    Task<Result<CrmProduct>> UpdateProductAsync(Guid productId, UpdateProductCommand command, CancellationToken ct = default);

    Task<Result<CrmDealLineItems>> ListLineItemsAsync(Guid tenantId, Guid dealId, CancellationToken ct = default);

    /// <summary>Adds a product to a deal; the deal's estimated value and monthly recurring value become the sums of its line items. Refused once the deal is won.</summary>
    Task<Result<CrmDealLineItems>> AddLineItemAsync(Guid dealId, AddLineItemCommand command, CancellationToken ct = default);

    Task<Result<CrmDealLineItems>> UpdateLineItemAsync(Guid dealId, Guid lineItemId, UpdateLineItemCommand command, CancellationToken ct = default);

    Task<Result<CrmDealLineItems>> DeleteLineItemAsync(Guid tenantId, Guid dealId, Guid lineItemId, CancellationToken ct = default);

    /// <summary>
    /// Moves the deal to its Won stage and creates one active holding per line item, in one transaction. A repeat
    /// call creates nothing. Refused when the deal has no line items or no organization or primary contact to hold them.
    /// </summary>
    Task<Result<WinDealResult>> WinDealAsync(Guid tenantId, Guid dealId, string changedBy, CancellationToken ct = default);

    Task<Result<IReadOnlyList<CrmHolding>>> ListHoldingsAsync(Guid tenantId, Guid participantId, CancellationToken ct = default);

    Task<Result<CrmHolding>> CreateHoldingAsync(CreateHoldingCommand command, CancellationToken ct = default);

    Task<Result<CrmHolding>> UpdateHoldingAsync(Guid holdingId, UpdateHoldingCommand command, CancellationToken ct = default);

    // ----- Prices, pricing rules, uplift tiers (Phase 2) -----

    /// <summary>A product's prices, newest first; history (ended prices) only when asked.</summary>
    Task<Result<IReadOnlyList<CrmProductPrice>>> ListPricesAsync(Guid tenantId, Guid productId, bool includeHistory, CancellationToken ct = default);

    /// <summary>The price in effect on a date (today when null) for a size, or not_found when none is set.</summary>
    Task<Result<CrmProductPrice>> GetPriceInEffectAsync(Guid tenantId, Guid productId, string? size, DateOnly? on, CancellationToken ct = default);

    Task<Result<CrmProductPrice>> SetPriceAsync(Guid productId, SetProductPriceCommand command, CancellationToken ct = default);

    /// <summary>The tenant's pricing rules, or not_found until they are set; nothing is defaulted.</summary>
    Task<Result<CrmPricingRules>> GetPricingRulesAsync(Guid tenantId, CancellationToken ct = default);

    Task<Result<CrmPricingRules>> SetPricingRulesAsync(SetPricingRulesCommand command, CancellationToken ct = default);

    Task<Result<IReadOnlyList<CrmUpliftTier>>> ListUpliftTiersAsync(Guid tenantId, CancellationToken ct = default);

    Task<Result<IReadOnlyList<CrmUpliftTier>>> ReplaceUpliftTiersAsync(ReplaceUpliftTiersCommand command, CancellationToken ct = default);
}
