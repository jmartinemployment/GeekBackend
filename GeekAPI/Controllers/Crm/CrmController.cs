using System.Text;
using System.Text.Json;
using GeekAPI.Auth;
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
[CrmAccess]
public sealed class CrmController(ICrmStore crm) : ControllerBase
{
    private CrmCaller Caller => HttpContext.Items[CrmAuthConstants.CallerItem] as CrmCaller
        ?? throw new InvalidOperationException("CrmAccess did not run for this request.");

    /// <summary>A server names its tenant (default Geek @ Your Spot); a signed-in user works in the tenant tenant_users gives them, whatever the address says.</summary>
    private Guid Tenant(Guid? requested) => Caller.IsServer ? requested ?? CrmSeed.GeekAtYourSpotTenantId : Caller.TenantId!.Value;

    /// <summary>A sensitive property is shown only to an owner (or a server) that asks for it.</summary>
    private bool Sensitive(bool requested) => requested && Caller.IsOwner;

    /// <summary>
    /// Who GeekAPI says is calling: for a signed-in user their tenant and role from tenant_users, for a server (the
    /// X-API-Key) no tenant and the owner role. The screen reads this once to know what to show; it is not a permission
    /// check, GeekAPI refuses a forbidden action whatever the screen shows.
    /// </summary>
    [HttpGet("me")]
    public IActionResult Me() => Ok(new CrmMe(Caller.IsServer, Caller.TenantId, Caller.Role, Caller.UserId));

    /// <summary>
    /// POST an Apollo "Export contacts" CSV as the raw request body (Content-Type: text/csv).
    /// Re-importing the same file matches existing people (by email or Apollo id) and companies
    /// (by name or exact website domain) instead of duplicating them. The whole batch is one
    /// transaction, and every row it creates carries the batch id the answer returns.
    /// <c>?dryRun=true</c> runs the import and rolls it back: the counts are what a real run would
    /// give and nothing is written.
    /// </summary>
    [HttpPost("imports/apollo")]
    [CrmOwnerOnly]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> ImportApollo([FromQuery] string? source, [FromQuery] Guid? tenantId, [FromQuery] bool dryRun, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var csv = await reader.ReadToEndAsync(ct);
        var prospects = ApolloCsv.Parse(csv);
        if (prospects.Count == 0) return BadRequest("The CSV had no rows (send the file as the request body).");

        var command = new ImportProspectsCommand(
            Tenant(tenantId),
            string.IsNullOrWhiteSpace(source) ? $"Apollo import {DateTime.UtcNow:yyyy-MM-dd}" : source.Trim(),
            prospects,
            dryRun);
        return ToAction(await crm.ImportProspectsAsync(command, ct));
    }

    [HttpGet("participants")]
    public async Task<IActionResult> ListParticipants([FromQuery] string? stage, [FromQuery] string? kind, [FromQuery] int limit, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListParticipantsAsync(Tenant(tenantId), stage, kind, limit, ct));

    [HttpGet("participants/{participantId:guid}")]
    public async Task<IActionResult> GetParticipant(Guid participantId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.GetParticipantAsync(Tenant(tenantId), participantId, ct));

    [HttpGet("deals")]
    public async Task<IActionResult> ListDeals([FromQuery] bool openOnly, [FromQuery] int limit, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListDealsAsync(Tenant(tenantId), openOnly, limit, ct));

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
        ToAction(await crm.ListProductsAsync(Tenant(tenantId), kind, includeRetired, ct));

    [HttpPost("products")]
    [CrmOwnerOnly]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A product is required.")
            : ToAction(await crm.CreateProductAsync(new CreateProductCommand(
                Tenant(tenantId), body.Name ?? string.Empty, body.Code ?? string.Empty, body.Kind ?? string.Empty,
                body.Category ?? string.Empty, body.UseCasePath, body.PartnerParticipantId, body.Billing ?? string.Empty), ct));

    /// <summary>Everything but the code and the kind can change; a product is retired with isActive = false, never deleted.</summary>
    [HttpPut("products/{productId:guid}")]
    [CrmOwnerOnly]
    public async Task<IActionResult> UpdateProduct(Guid productId, [FromBody] UpdateProductRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A product is required.")
            : ToAction(await crm.UpdateProductAsync(productId, new UpdateProductCommand(
                Tenant(tenantId), body.Name ?? string.Empty, body.Category ?? string.Empty, body.UseCasePath,
                body.PartnerParticipantId, body.Billing ?? string.Empty, body.IsActive), ct));

    [HttpGet("deals/{dealId:guid}/line-items")]
    public async Task<IActionResult> ListLineItems(Guid dealId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListLineItemsAsync(Tenant(tenantId), dealId, ct));

    [HttpPost("deals/{dealId:guid}/line-items")]
    public async Task<IActionResult> AddLineItem(Guid dealId, [FromBody] LineItemRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || body.ProductId is null
            ? BadRequest("A line item needs a product.")
            : ToAction(await crm.AddLineItemAsync(dealId, new AddLineItemCommand(
                Tenant(tenantId), body.ProductId.Value, body.Quantity ?? 0m, body.UnitPrice, body.IsRecurring, body.DiscountPct ?? 0m, body.Size), ct));

    [HttpPut("deals/{dealId:guid}/line-items/{lineItemId:guid}")]
    public async Task<IActionResult> UpdateLineItem(Guid dealId, Guid lineItemId, [FromBody] LineItemRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || body.IsRecurring is null
            ? BadRequest("A line item needs quantity, unitPrice and isRecurring.")
            : ToAction(await crm.UpdateLineItemAsync(dealId, lineItemId, new UpdateLineItemCommand(
                Tenant(tenantId), body.Quantity ?? 0m, body.UnitPrice ?? -1m, body.IsRecurring.Value, body.DiscountPct ?? 0m), ct));

    [HttpDelete("deals/{dealId:guid}/line-items/{lineItemId:guid}")]
    public async Task<IActionResult> DeleteLineItem(Guid dealId, Guid lineItemId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.DeleteLineItemAsync(Tenant(tenantId), dealId, lineItemId, ct));

    /// <summary>Moves the deal to Won and makes one active holding per line item, in one transaction. A repeat call makes nothing new.</summary>
    [HttpPost("deals/{dealId:guid}/win")]
    public async Task<IActionResult> WinDeal(Guid dealId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.WinDealAsync(Tenant(tenantId), dealId, "api", ct));

    [HttpGet("participants/{participantId:guid}/holdings")]
    public async Task<IActionResult> ListHoldings(Guid participantId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListHoldingsAsync(Tenant(tenantId), participantId, ct));

    [HttpPost("participants/{participantId:guid}/holdings")]
    public async Task<IActionResult> CreateHolding(Guid participantId, [FromBody] HoldingRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || body.ProductId is null
            ? BadRequest("A holding needs a product.")
            : ToAction(await crm.CreateHoldingAsync(new CreateHoldingCommand(
                Tenant(tenantId), participantId, body.ProductId.Value, body.ToDetails()), ct));

    [HttpPut("participants/{participantId:guid}/holdings/{holdingId:guid}")]
    public async Task<IActionResult> UpdateHolding(Guid participantId, Guid holdingId, [FromBody] HoldingRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A holding is required.")
            : ToAction(await crm.UpdateHoldingAsync(holdingId, new UpdateHoldingCommand(Tenant(tenantId), body.ToDetails()), ct));

    // ----- Prices, pricing rules and uplift tiers (Phase 2) -----

    /// <summary>A product's prices, newest first; <c>includeHistory=true</c> adds the ended ones.</summary>
    [HttpGet("products/{productId:guid}/prices")]
    public async Task<IActionResult> ListPrices(Guid productId, [FromQuery] bool includeHistory, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListPricesAsync(Tenant(tenantId), productId, includeHistory, ct));

    /// <summary>The price in effect on <c>on</c> (today when omitted) for <c>size</c> (a flat price when omitted); 404 when none is set.</summary>
    [HttpGet("products/{productId:guid}/price")]
    public async Task<IActionResult> GetPriceInEffect(Guid productId, [FromQuery] string? size, [FromQuery] DateOnly? on, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.GetPriceInEffectAsync(Tenant(tenantId), productId, size, on, ct));

    /// <summary>Sets a price from <c>effectiveFrom</c>; the price in effect for that size ends the day before. History is kept.</summary>
    [HttpPost("products/{productId:guid}/prices")]
    [CrmOwnerOnly]
    public async Task<IActionResult> SetPrice(Guid productId, [FromBody] SetPriceRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || body.EffectiveFrom is null
            ? BadRequest("A price needs effectiveFrom.")
            : ToAction(await crm.SetPriceAsync(productId, new SetProductPriceCommand(
                Tenant(tenantId), body.Size, body.Hours, body.UnitPrice, body.RecurringPriceMonthly, body.EffectiveFrom.Value), ct));

    [HttpGet("pricing-rules")]
    public async Task<IActionResult> GetPricingRules([FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.GetPricingRulesAsync(Tenant(tenantId), ct));

    [HttpPut("pricing-rules")]
    [CrmOwnerOnly]
    public async Task<IActionResult> SetPricingRules([FromBody] SetPricingRulesRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || body.RelatedUseCasePct is null
            ? BadRequest("Pricing rules need relatedUseCasePct.")
            : ToAction(await crm.SetPricingRulesAsync(new SetPricingRulesCommand(
                Tenant(tenantId), body.HourlyRate, body.RelatedUseCasePct.Value), ct));

    [HttpGet("pricing-uplift-tiers")]
    public async Task<IActionResult> ListUpliftTiers([FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListUpliftTiersAsync(Tenant(tenantId), ct));

    /// <summary>Replaces the whole tier list (an empty list clears it) in one transaction.</summary>
    [HttpPut("pricing-uplift-tiers")]
    [CrmOwnerOnly]
    public async Task<IActionResult> ReplaceUpliftTiers([FromBody] ReplaceUpliftTiersRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body?.Tiers is null
            ? BadRequest("A tier list is required.")
            : ToAction(await crm.ReplaceUpliftTiersAsync(new ReplaceUpliftTiersCommand(Tenant(tenantId), body.Tiers), ct));

    // ----- The AI voice receptionist: projects, capacity, activities, intake (Phase 3) -----

    /// <summary>
    /// One call writes the caller, their contact points, a deal with estimate line items, the call and the booked meeting, in one
    /// transaction. Repeating a call SID writes nothing and answers what the first run wrote (created = false).
    /// </summary>
    [HttpPost("receptionist/intake")]
    public async Task<IActionResult> ReceptionistIntake([FromBody] ReceptionistIntakeRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || body.OccurredAt is null
            ? BadRequest("An intake needs callSid, phone, productCategory, priority and occurredAt.")
            : ToAction(await crm.ReceptionistIntakeAsync(new ReceptionistIntakeCommand(
                Tenant(tenantId), body.CallSid ?? string.Empty, body.Phone ?? string.Empty, body.FirstName, body.LastName, body.Email,
                body.ProductCategory ?? string.Empty, body.Qualification, body.Products ?? [], body.Summary, body.Transcript, body.RecordingUrl,
                body.DurationSeconds, body.OccurredAt.Value, body.Priority ?? string.Empty, body.Meeting), ct));

    [HttpGet("projects")]
    public async Task<IActionResult> ListProjects([FromQuery] string? status, [FromQuery] Guid? participantId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListProjectsAsync(Tenant(tenantId), status, participantId, ct));

    [HttpPost("projects")]
    public async Task<IActionResult> CreateProject([FromBody] ProjectRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || body.ParticipantId is null
            ? BadRequest("A project needs a participantId.")
            : ToAction(await crm.CreateProjectAsync(new CreateProjectCommand(Tenant(tenantId), body.ParticipantId.Value, body.ToDetails()), ct));

    [HttpPut("projects/{projectId:guid}")]
    public async Task<IActionResult> UpdateProject(Guid projectId, [FromBody] ProjectRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A project is required.")
            : ToAction(await crm.UpdateProjectAsync(projectId, new UpdateProjectCommand(Tenant(tenantId), body.ToDetails()), ct));

    /// <summary>Booked hours per week against 40 base + 20 surge, the uplift each week carries, and the earliest week with base capacity free. An estimate, never a reservation.</summary>
    [HttpGet("capacity")]
    public async Task<IActionResult> GetCapacity([FromQuery] int weeks, [FromQuery] DateOnly? asOf, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.GetCapacityAsync(Tenant(tenantId), weeks <= 0 ? CrmCapacityRules.DefaultWeeks : weeks, asOf, ct));

    /// <summary><c>?priority=urgent</c> lists the messages that need attention first.</summary>
    [HttpGet("activities")]
    public async Task<IActionResult> ListActivities([FromQuery] string? priority, [FromQuery] string? channel, [FromQuery] int limit, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListActivitiesAsync(Tenant(tenantId), priority, channel, limit, ct));

    [HttpGet("properties/definitions")]
    public async Task<IActionResult> ListPropertyDefinitions([FromQuery] string? objectType, [FromQuery] bool includeRetired, [FromQuery] bool includeSensitive, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.ListPropertyDefinitionsAsync(Tenant(tenantId), objectType, includeRetired, Sensitive(includeSensitive), ct));

    [HttpPost("properties/definitions")]
    [CrmOwnerOnly]
    public async Task<IActionResult> CreatePropertyDefinition([FromBody] CreatePropertyDefinitionRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A property is required.")
            : ToAction(await crm.CreatePropertyDefinitionAsync(new CreatePropertyDefinitionCommand(
                Tenant(tenantId), body.ObjectType ?? string.Empty, body.Key ?? string.Empty,
                body.Label ?? string.Empty, body.DataType ?? string.Empty, body.Options, body.IsSensitive), ct));

    [HttpPut("properties/definitions/{definitionId:guid}")]
    [CrmOwnerOnly]
    public async Task<IActionResult> UpdatePropertyDefinition(Guid definitionId, [FromBody] UpdatePropertyDefinitionRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A property is required.")
            : ToAction(await crm.UpdatePropertyDefinitionAsync(definitionId, new UpdatePropertyDefinitionCommand(
                Tenant(tenantId), body.Label ?? string.Empty, body.Options, body.IsSensitive), ct));

    /// <summary>Retires a property: it disappears from reads and refuses new values; stored values are kept for history.</summary>
    [HttpDelete("properties/definitions/{definitionId:guid}")]
    [CrmOwnerOnly]
    public async Task<IActionResult> RetirePropertyDefinition(Guid definitionId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.RetirePropertyDefinitionAsync(Tenant(tenantId), definitionId, ct));

    /// <summary>objectType is person, organization or deal.</summary>
    [HttpGet("{objectType}/{recordId:guid}/properties")]
    public async Task<IActionResult> GetPropertyValues(string objectType, Guid recordId, [FromQuery] bool includeSensitive, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await crm.GetPropertyValuesAsync(Tenant(tenantId), objectType, recordId, Sensitive(includeSensitive), ct));

    /// <summary>Body: <c>{"values": {"key": value, "other_key": null}}</c>. A null clears. Every value is checked first and all are saved or none.</summary>
    [HttpPut("{objectType}/{recordId:guid}/properties")]
    public async Task<IActionResult> SetPropertyValues(string objectType, Guid recordId, [FromBody] SetPropertyValuesRequest? body, [FromQuery] bool includeSensitive, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body?.Values is null
            ? BadRequest("A values object is required.")
            : ToAction(await crm.SetPropertyValuesAsync(new SetPropertyValuesCommand(
                Tenant(tenantId), objectType, recordId, body.Values, Sensitive(includeSensitive)), ct));

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

public sealed record ReceptionistIntakeRequest(
    string? CallSid,
    string? Phone,
    string? FirstName,
    string? LastName,
    string? Email,
    string? ProductCategory,
    JsonElement? Qualification,
    IReadOnlyList<IntakeProduct>? Products,
    string? Summary,
    string? Transcript,
    string? RecordingUrl,
    int? DurationSeconds,
    DateTime? OccurredAt,
    string? Priority,
    IntakeMeeting? Meeting);

/// <summary>A project as a caller sends it; status, estimated hours and hours per week are required, so a missing one is refused rather than defaulted.</summary>
public sealed record ProjectRequest(Guid? ParticipantId, string? Status, DateOnly? StartOn, DateOnly? TargetEndOn, decimal? EstimatedHours, decimal? HoursPerWeek, Guid? DealId, Guid? CustomerHoldingId)
{
    public ProjectDetails ToDetails() => new(Status ?? string.Empty, StartOn, TargetEndOn, EstimatedHours ?? -1m, HoursPerWeek ?? -1m, DealId, CustomerHoldingId);
}

public sealed record CrmMe(bool IsServer, Guid? TenantId, string Role, Guid? UserId);
