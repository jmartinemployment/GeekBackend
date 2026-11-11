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
