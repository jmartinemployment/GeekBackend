using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Crm;

/// <summary>
/// repo/crm/* for the work a person does at the keyboard (<see cref="ICrmWorkStore"/>), beside <see cref="CrmController"/>.
/// Every route names its tenant; status codes come from the failure code, never its wording.
/// </summary>
[ApiController]
[Route("repo/crm")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class CrmWorkController(ICrmWorkStore work) : ControllerBase
{
    [HttpGet("tenants/{tenantId:guid}/stages")]
    public async Task<IActionResult> ListStages(Guid tenantId, CancellationToken ct) =>
        ToAction(await work.ListStagesAsync(tenantId, ct));

    [HttpGet("tenants/{tenantId:guid}/dashboard")]
    public async Task<IActionResult> GetDashboard(Guid tenantId, CancellationToken ct) =>
        ToAction(await work.GetDashboardAsync(tenantId, ct));

    [HttpGet("tenants/{tenantId:guid}/participants/search")]
    public async Task<IActionResult> SearchParticipants(Guid tenantId, [FromQuery] string? q, [FromQuery] string? stage, [FromQuery] string? kind, [FromQuery] int limit, CancellationToken ct) =>
        ToAction(await work.SearchParticipantsAsync(tenantId, q, stage, kind, limit, ct));

    [HttpPost("participants")]
    public async Task<IActionResult> CreateParticipant([FromBody] CreateParticipantCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A participant is required.") : ToAction(await work.CreateParticipantAsync(command, ct));

    [HttpPut("participants/{participantId:guid}")]
    public async Task<IActionResult> UpdateParticipant(Guid participantId, [FromBody] UpdateParticipantCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A participant is required.") : ToAction(await work.UpdateParticipantAsync(participantId, command, ct));

    [HttpPost("tenants/{tenantId:guid}/participants/{participantId:guid}/archive")]
    public async Task<IActionResult> ArchiveParticipant(Guid tenantId, Guid participantId, CancellationToken ct) =>
        ToAction(await work.ArchiveParticipantAsync(tenantId, participantId, ct));

    [HttpDelete("tenants/{tenantId:guid}/participants/{participantId:guid}")]
    public async Task<IActionResult> DeleteParticipant(Guid tenantId, Guid participantId, CancellationToken ct) =>
        ToAction(await work.DeleteParticipantAsync(tenantId, participantId, ct));

    [HttpDelete("tenants/{tenantId:guid}/deals/{dealId:guid}")]
    public async Task<IActionResult> DeleteDeal(Guid tenantId, Guid dealId, CancellationToken ct) =>
        ToAction(await work.DeleteDealAsync(tenantId, dealId, ct));

    [HttpDelete("tenants/{tenantId:guid}/activities/{activityId:guid}")]
    public async Task<IActionResult> DeleteActivity(Guid tenantId, Guid activityId, CancellationToken ct) =>
        ToAction(await work.DeleteActivityAsync(tenantId, activityId, ct));

    [HttpGet("tenants/{tenantId:guid}/deals/views")]
    public async Task<IActionResult> ListDealViews(Guid tenantId, [FromQuery] bool openOnly, [FromQuery] string? q, [FromQuery] int limit, CancellationToken ct) =>
        ToAction(await work.ListDealViewsAsync(tenantId, openOnly, q, limit, ct));

    [HttpGet("tenants/{tenantId:guid}/deals/{dealId:guid}/view")]
    public async Task<IActionResult> GetDealView(Guid tenantId, Guid dealId, CancellationToken ct) =>
        ToAction(await work.GetDealViewAsync(tenantId, dealId, ct));

    [HttpPut("deals/{dealId:guid}")]
    public async Task<IActionResult> UpdateDeal(Guid dealId, [FromBody] UpdateDealCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A deal is required.") : ToAction(await work.UpdateDealAsync(dealId, command, ct));

    [HttpGet("tenants/{tenantId:guid}/activities/views")]
    public async Task<IActionResult> ListActivityViews(Guid tenantId, [FromQuery] Guid? participantId, [FromQuery] Guid? dealId, [FromQuery] string? channel, [FromQuery] bool openTasks, [FromQuery] int limit, CancellationToken ct) =>
        ToAction(await work.ListActivityViewsAsync(tenantId, participantId, dealId, channel, openTasks, limit, ct));

    [HttpPost("activities/create")]
    public async Task<IActionResult> CreateActivity([FromBody] CreateActivityCommand command, CancellationToken ct) =>
        command is null ? BadRequest("An activity is required.") : ToAction(await work.CreateActivityAsync(command, ct));

    [HttpPost("tenants/{tenantId:guid}/activities/{activityId:guid}/complete")]
    public async Task<IActionResult> CompleteTask(Guid tenantId, Guid activityId, CancellationToken ct) =>
        ToAction(await work.CompleteTaskAsync(tenantId, activityId, ct));

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
