using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Crm;

/// <summary>
/// repo/crm/* — the geek_crm store for GeekAPI only. Every route names its tenant; the store never
/// reads across tenants. Status codes come from the failure code, never its wording.
/// </summary>
[ApiController]
[Route("repo/crm")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class CrmController(ICrmStore crm) : ControllerBase
{
    [HttpPost("participants/ensure-by-phone")]
    public async Task<IActionResult> EnsureByPhone([FromBody] EnsureParticipantByPhoneCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A command is required.") : ToAction(await crm.EnsureParticipantByPhoneAsync(command, ct));

    [HttpPost("activities")]
    public async Task<IActionResult> RecordActivity([FromBody] RecordActivityCommand command, CancellationToken ct) =>
        command is null ? BadRequest("An activity is required.") : ToAction(await crm.RecordActivityAsync(command, ct));

    [HttpPost("contact-points/sms-opt-out")]
    public async Task<IActionResult> SetSmsOptOut([FromBody] SetSmsOptOutCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A command is required.") : ToAction(await crm.SetSmsOptOutAsync(command, ct));

    [HttpPost("imports")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Import([FromBody] ImportProspectsCommand command, CancellationToken ct) =>
        command is null ? BadRequest("An import is required.") : ToAction(await crm.ImportProspectsAsync(command, ct));

    [HttpGet("tenants/{tenantId:guid}/participants")]
    public async Task<IActionResult> ListParticipants(Guid tenantId, [FromQuery] string? stage, [FromQuery] string? kind, [FromQuery] int limit, CancellationToken ct) =>
        ToAction(await crm.ListParticipantsAsync(tenantId, stage, kind, limit, ct));

    [HttpGet("tenants/{tenantId:guid}/participants/{participantId:guid}")]
    public async Task<IActionResult> GetParticipant(Guid tenantId, Guid participantId, CancellationToken ct) =>
        ToAction(await crm.GetParticipantAsync(tenantId, participantId, ct));

    [HttpPost("deals")]
    public async Task<IActionResult> CreateDeal([FromBody] CreateDealCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A deal is required.") : ToAction(await crm.CreateDealAsync(command, ct));

    [HttpPost("deals/{dealId:guid}/stage")]
    public async Task<IActionResult> MoveDealStage(Guid dealId, [FromBody] MoveDealStageCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A stage is required.") : ToAction(await crm.MoveDealStageAsync(dealId, command, ct));

    [HttpGet("tenants/{tenantId:guid}/deals")]
    public async Task<IActionResult> ListDeals(Guid tenantId, [FromQuery] bool openOnly, [FromQuery] int limit, CancellationToken ct) =>
        ToAction(await crm.ListDealsAsync(tenantId, openOnly, limit, ct));

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
