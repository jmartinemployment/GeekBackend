using System.Text;
using System.Text.Json;
using GeekAPI.Services.Crm;
using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Crm;

/// <summary>
/// Geek-CRM over GeekAPI: import a prospect list, read participants and deals. Authenticated by
/// ApiKeyMiddleware (X-API-Key) like every /api route. The tenant defaults to Geek @ Your Spot.
/// </summary>
[ApiController]
[Route("api/crm")]
public sealed class CrmController(ICrmStore crm) : ControllerBase
{
    /// <summary>
    /// POST an Apollo "Export contacts" CSV as the raw request body (Content-Type: text/csv).
    /// Re-importing the same file matches existing people (by email or Apollo id) and companies
    /// (by name or exact website domain) instead of duplicating them. The whole batch is one
    /// transaction, and every row it creates carries the batch id the answer returns.
    /// <c>?dryRun=true</c> runs the import and rolls it back: the counts are what a real run would
    /// give and nothing is written.
    /// </summary>
    [HttpPost("imports/apollo")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> ImportApollo([FromQuery] string? source, [FromQuery] Guid? tenantId, [FromQuery] bool dryRun, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var csv = await reader.ReadToEndAsync(ct);
        var prospects = ApolloCsv.Parse(csv);
        if (prospects.Count == 0) return BadRequest("The CSV had no rows (send the file as the request body).");

        var command = new ImportProspectsCommand(
            tenantId ?? CrmSeed.GeekAtYourSpotTenantId,
            string.IsNullOrWhiteSpace(source) ? $"Apollo import {DateTime.UtcNow:yyyy-MM-dd}" : source.Trim(),
            prospects,
            dryRun);
        return ToAction(await crm.ImportProspectsAsync(command, ct));
    }

    [HttpGet("participants")]
    public async Task<IActionResult> ListParticipants([FromQuery] string? stage, [FromQuery] string? kind, [FromQuery] int limit, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListParticipantsAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, stage, kind, limit, ct));

    [HttpGet("participants/{participantId:guid}")]
    public async Task<IActionResult> GetParticipant(Guid participantId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.GetParticipantAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, participantId, ct));

    [HttpGet("deals")]
    public async Task<IActionResult> ListDeals([FromQuery] bool openOnly, [FromQuery] int limit, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListDealsAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, openOnly, limit, ct));

    // ----- Industry lookup -----

    /// <summary>Search the Census Bureau's 2022 NAICS list. Digits search by code prefix; words search titles.</summary>
    [HttpGet("lookups/naics")]
    public async Task<IActionResult> SearchNaics([FromQuery] string? q, [FromQuery] int? level, [FromQuery] int limit, CancellationToken ct) =>
        ToAction(await crm.SearchNaicsAsync(q, level, limit, ct));

    // ----- Custom properties -----
    // A sensitive property is hidden unless the caller asks for it with includeSensitive=true. Today every
    // caller holds the X-API-Key (a trusted server); when signed-in users arrive, their role decides it.

    // ----- Catalog, deal line items and holdings (Phase 2) -----

    [HttpGet("products")]
    public async Task<IActionResult> ListProducts([FromQuery] string? kind, [FromQuery] bool includeRetired, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListProductsAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, kind, includeRetired, ct));

    [HttpPost("products")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A product is required.")
            : ToAction(await crm.CreateProductAsync(new CreateProductCommand(
                tenantId ?? CrmSeed.GeekAtYourSpotTenantId, body.Name ?? string.Empty, body.Code ?? string.Empty, body.Kind ?? string.Empty,
                body.Category ?? string.Empty, body.UseCasePath, body.PartnerParticipantId, body.Billing ?? string.Empty), ct));

    /// <summary>Everything but the code and the kind can change; a product is retired with isActive = false, never deleted.</summary>
    [HttpPut("products/{productId:guid}")]
    public async Task<IActionResult> UpdateProduct(Guid productId, [FromBody] UpdateProductRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A product is required.")
            : ToAction(await crm.UpdateProductAsync(productId, new UpdateProductCommand(
                tenantId ?? CrmSeed.GeekAtYourSpotTenantId, body.Name ?? string.Empty, body.Category ?? string.Empty, body.UseCasePath,
                body.PartnerParticipantId, body.Billing ?? string.Empty, body.IsActive), ct));

    [HttpGet("deals/{dealId:guid}/line-items")]
    public async Task<IActionResult> ListLineItems(Guid dealId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListLineItemsAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, dealId, ct));

    [HttpPost("deals/{dealId:guid}/line-items")]
    public async Task<IActionResult> AddLineItem(Guid dealId, [FromBody] LineItemRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || body.ProductId is null
            ? BadRequest("A line item needs a product.")
            : ToAction(await crm.AddLineItemAsync(dealId, new AddLineItemCommand(
                tenantId ?? CrmSeed.GeekAtYourSpotTenantId, body.ProductId.Value, body.Quantity ?? 0m, body.UnitPrice, body.IsRecurring, body.DiscountPct ?? 0m, body.Size), ct));

    [HttpPut("deals/{dealId:guid}/line-items/{lineItemId:guid}")]
    public async Task<IActionResult> UpdateLineItem(Guid dealId, Guid lineItemId, [FromBody] LineItemRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || body.IsRecurring is null
            ? BadRequest("A line item needs quantity, unitPrice and isRecurring.")
            : ToAction(await crm.UpdateLineItemAsync(dealId, lineItemId, new UpdateLineItemCommand(
                tenantId ?? CrmSeed.GeekAtYourSpotTenantId, body.Quantity ?? 0m, body.UnitPrice ?? -1m, body.IsRecurring.Value, body.DiscountPct ?? 0m), ct));

    [HttpDelete("deals/{dealId:guid}/line-items/{lineItemId:guid}")]
    public async Task<IActionResult> DeleteLineItem(Guid dealId, Guid lineItemId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.DeleteLineItemAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, dealId, lineItemId, ct));

    /// <summary>Moves the deal to Won and makes one active holding per line item, in one transaction. A repeat call makes nothing new.</summary>
    [HttpPost("deals/{dealId:guid}/win")]
    public async Task<IActionResult> WinDeal(Guid dealId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.WinDealAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, dealId, "api", ct));

    [HttpGet("participants/{participantId:guid}/holdings")]
    public async Task<IActionResult> ListHoldings(Guid participantId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListHoldingsAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, participantId, ct));

    [HttpPost("participants/{participantId:guid}/holdings")]
    public async Task<IActionResult> CreateHolding(Guid participantId, [FromBody] HoldingRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || body.ProductId is null
            ? BadRequest("A holding needs a product.")
            : ToAction(await crm.CreateHoldingAsync(new CreateHoldingCommand(
                tenantId ?? CrmSeed.GeekAtYourSpotTenantId, participantId, body.ProductId.Value, body.ToDetails()), ct));

    [HttpPut("participants/{participantId:guid}/holdings/{holdingId:guid}")]
    public async Task<IActionResult> UpdateHolding(Guid participantId, Guid holdingId, [FromBody] HoldingRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A holding is required.")
            : ToAction(await crm.UpdateHoldingAsync(holdingId, new UpdateHoldingCommand(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, body.ToDetails()), ct));

    // ----- Prices, pricing rules and uplift tiers (Phase 2) -----

    /// <summary>A product's prices, newest first; <c>includeHistory=true</c> adds the ended ones.</summary>
    [HttpGet("products/{productId:guid}/prices")]
    public async Task<IActionResult> ListPrices(Guid productId, [FromQuery] bool includeHistory, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListPricesAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, productId, includeHistory, ct));

    /// <summary>The price in effect on <c>on</c> (today when omitted) for <c>size</c> (a flat price when omitted); 404 when none is set.</summary>
    [HttpGet("products/{productId:guid}/price")]
    public async Task<IActionResult> GetPriceInEffect(Guid productId, [FromQuery] string? size, [FromQuery] DateOnly? on, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.GetPriceInEffectAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, productId, size, on, ct));

    /// <summary>Sets a price from <c>effectiveFrom</c>; the price in effect for that size ends the day before. History is kept.</summary>
    [HttpPost("products/{productId:guid}/prices")]
    public async Task<IActionResult> SetPrice(Guid productId, [FromBody] SetPriceRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || body.EffectiveFrom is null
            ? BadRequest("A price needs effectiveFrom.")
            : ToAction(await crm.SetPriceAsync(productId, new SetProductPriceCommand(
                tenantId ?? CrmSeed.GeekAtYourSpotTenantId, body.Size, body.Hours, body.UnitPrice, body.RecurringPriceMonthly, body.EffectiveFrom.Value), ct));

    [HttpGet("pricing-rules")]
    public async Task<IActionResult> GetPricingRules([FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.GetPricingRulesAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, ct));

    [HttpPut("pricing-rules")]
    public async Task<IActionResult> SetPricingRules([FromBody] SetPricingRulesRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || body.RelatedUseCasePct is null
            ? BadRequest("Pricing rules need relatedUseCasePct.")
            : ToAction(await crm.SetPricingRulesAsync(new SetPricingRulesCommand(
                tenantId ?? CrmSeed.GeekAtYourSpotTenantId, body.HourlyRate, body.RelatedUseCasePct.Value), ct));

    [HttpGet("pricing-uplift-tiers")]
    public async Task<IActionResult> ListUpliftTiers([FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListUpliftTiersAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, ct));

    /// <summary>Replaces the whole tier list (an empty list clears it) in one transaction.</summary>
    [HttpPut("pricing-uplift-tiers")]
    public async Task<IActionResult> ReplaceUpliftTiers([FromBody] ReplaceUpliftTiersRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body?.Tiers is null
            ? BadRequest("A tier list is required.")
            : ToAction(await crm.ReplaceUpliftTiersAsync(new ReplaceUpliftTiersCommand(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, body.Tiers), ct));

    [HttpGet("properties/definitions")]
    public async Task<IActionResult> ListPropertyDefinitions([FromQuery] string? objectType, [FromQuery] bool includeRetired, [FromQuery] bool includeSensitive, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListPropertyDefinitionsAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, objectType, includeRetired, includeSensitive, ct));

    [HttpPost("properties/definitions")]
    public async Task<IActionResult> CreatePropertyDefinition([FromBody] CreatePropertyDefinitionRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A property is required.")
            : ToAction(await crm.CreatePropertyDefinitionAsync(new CreatePropertyDefinitionCommand(
                tenantId ?? CrmSeed.GeekAtYourSpotTenantId, body.ObjectType ?? string.Empty, body.Key ?? string.Empty,
                body.Label ?? string.Empty, body.DataType ?? string.Empty, body.Options, body.IsSensitive), ct));

    [HttpPut("properties/definitions/{definitionId:guid}")]
    public async Task<IActionResult> UpdatePropertyDefinition(Guid definitionId, [FromBody] UpdatePropertyDefinitionRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A property is required.")
            : ToAction(await crm.UpdatePropertyDefinitionAsync(definitionId, new UpdatePropertyDefinitionCommand(
                tenantId ?? CrmSeed.GeekAtYourSpotTenantId, body.Label ?? string.Empty, body.Options, body.IsSensitive), ct));

    /// <summary>Retires a property: it disappears from reads and refuses new values; stored values are kept for history.</summary>
    [HttpDelete("properties/definitions/{definitionId:guid}")]
    public async Task<IActionResult> RetirePropertyDefinition(Guid definitionId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.RetirePropertyDefinitionAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, definitionId, ct));

    /// <summary>objectType is person, organization or deal.</summary>
    [HttpGet("{objectType}/{recordId:guid}/properties")]
    public async Task<IActionResult> GetPropertyValues(string objectType, Guid recordId, [FromQuery] bool includeSensitive, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.GetPropertyValuesAsync(tenantId ?? CrmSeed.GeekAtYourSpotTenantId, objectType, recordId, includeSensitive, ct));

    /// <summary>Body: <c>{"values": {"key": value, "other_key": null}}</c>. A null clears. Every value is checked first and all are saved or none.</summary>
    [HttpPut("{objectType}/{recordId:guid}/properties")]
    public async Task<IActionResult> SetPropertyValues(string objectType, Guid recordId, [FromBody] SetPropertyValuesRequest? body, [FromQuery] bool includeSensitive, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body?.Values is null
            ? BadRequest("A values object is required.")
            : ToAction(await crm.SetPropertyValuesAsync(new SetPropertyValuesCommand(
                tenantId ?? CrmSeed.GeekAtYourSpotTenantId, objectType, recordId, body.Values, includeSensitive), ct));

    private IActionResult ToAction<T>(Result<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        var message = string.IsNullOrWhiteSpace(result.Error) ? "The CRM store failed." : result.Error;
        return result.ErrorCode switch
        {
            CrmFailureCodes.NotFound => NotFound(message),
            CrmFailureCodes.InvalidRequest => BadRequest(message),
            CrmFailureCodes.Conflict => Conflict(message),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, message),
        };
    }
}

public sealed record CreatePropertyDefinitionRequest(string? ObjectType, string? Key, string? Label, string? DataType, IReadOnlyList<string>? Options, bool IsSensitive);

public sealed record UpdatePropertyDefinitionRequest(string? Label, IReadOnlyList<string>? Options, bool IsSensitive);

public sealed record SetPropertyValuesRequest(IReadOnlyDictionary<string, JsonElement>? Values);

public sealed record CreateProductRequest(string? Name, string? Code, string? Kind, string? Category, string? UseCasePath, Guid? PartnerParticipantId, string? Billing);

public sealed record UpdateProductRequest(string? Name, string? Category, string? UseCasePath, Guid? PartnerParticipantId, string? Billing, bool IsActive);

/// <summary>A missing quantity or unit price is refused by the store's rules, not defaulted.</summary>
/// <summary>On a new line item, an omitted unitPrice means the catalog price in effect (for size, when the product is priced by size).</summary>
public sealed record LineItemRequest(Guid? ProductId, decimal? Quantity, decimal? UnitPrice, bool? IsRecurring, decimal? DiscountPct, string? Size = null);

public sealed record SetPriceRequest(string? Size, decimal? Hours, decimal? UnitPrice, decimal? RecurringPriceMonthly, DateOnly? EffectiveFrom);

public sealed record SetPricingRulesRequest(decimal? HourlyRate, decimal? RelatedUseCasePct);

public sealed record ReplaceUpliftTiersRequest(IReadOnlyList<UpliftTierInput>? Tiers);

/// <summary>A holding as a caller sends it; quantity, unit price and status are required, so a missing one is refused rather than defaulted.</summary>
public sealed record HoldingRequest(
    Guid? ProductId,
    string? Status,
    DateOnly? StartOn,
    DateOnly? EndOn,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? DiscountPct,
    decimal? RecurringAmountMonthly,
    decimal? OneTimeAmount,
    string? Arrangement,
    decimal? ReferralFee,
    bool? ReferralFeeIsRecurring,
    DateOnly? ReferralPaidOn,
    decimal? PartnerCostMonthly,
    string? ExternalRef)
{
    public HoldingDetails ToDetails() => new(
        Status ?? string.Empty, StartOn, EndOn, Quantity ?? 0m, UnitPrice ?? -1m, DiscountPct ?? 0m, RecurringAmountMonthly, OneTimeAmount,
        Arrangement, ReferralFee, ReferralFeeIsRecurring, ReferralPaidOn, PartnerCostMonthly, ExternalRef);
}
