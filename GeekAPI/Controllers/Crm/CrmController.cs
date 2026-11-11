using System.Text;
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
    /// (by name or website) instead of duplicating them.
    /// </summary>
    [HttpPost("imports/apollo")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> ImportApollo([FromQuery] string? source, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var csv = await reader.ReadToEndAsync(ct);
        var prospects = ApolloCsv.Parse(csv);
        if (prospects.Count == 0) return BadRequest("The CSV had no rows (send the file as the request body).");

        var command = new ImportProspectsCommand(
            tenantId ?? CrmSeed.GeekAtYourSpotTenantId,
            string.IsNullOrWhiteSpace(source) ? $"Apollo import {DateTime.UtcNow:yyyy-MM-dd}" : source.Trim(),
            prospects);
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
