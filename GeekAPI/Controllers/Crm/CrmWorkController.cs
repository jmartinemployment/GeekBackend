using GeekAPI.Auth;
using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Crm;

/// <summary>
/// The work a person does in Geek-CRM's screens: find and edit people and companies, work deals through the pipeline, keep notes
/// and tasks, and the numbers for the home screen. Owners and members both do all of it (the catalog and prices stay owner-only in
/// <see cref="CrmController"/>). A signed-in user's tenant comes from tenant_users, whatever the address says.
/// </summary>
[ApiController]
[Route("api/crm")]
[CrmAccess]
public sealed class CrmWorkController(ICrmWorkStore work, ICrmStore crm) : CrmApiController
{
    /// <summary>Who did it, as written on a stage change or an activity: the signed-in user's id, or "api" for a server.</summary>
    private string Actor => Caller.UserId?.ToString() ?? "api";

    // ----- Pipeline and home screen -----

    [HttpGet("stages")]
    public async Task<IActionResult> ListStages([FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await work.ListStagesAsync(Tenant(tenantId), ct));

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard([FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await work.GetDashboardAsync(Tenant(tenantId), ct));

    // ----- People and companies -----

    /// <summary>Newest activity first. <c>q</c> matches a name, an email address or a phone number.</summary>
    [HttpGet("participants/search")]
    public async Task<IActionResult> SearchParticipants([FromQuery] string? q, [FromQuery] string? stage, [FromQuery] string? kind, [FromQuery] int limit, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await work.SearchParticipantsAsync(Tenant(tenantId), q, stage, kind, limit, ct));

    [HttpPost("participants")]
    public async Task<IActionResult> CreateParticipant([FromBody] CreateParticipantRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A person or company is required.")
            : ToAction(await work.CreateParticipantAsync(new CreateParticipantCommand(
                Tenant(tenantId), body.Kind ?? string.Empty, body.FirstName, body.LastName, body.JobTitle, body.OrganizationName, body.Website,
                body.Email, body.Phone, body.LifecycleStage ?? string.Empty, body.Source ?? string.Empty, body.Notes,
                body.LinkedOrganizationId, body.LinkRole), ct));

    [HttpPut("participants/{participantId:guid}")]
    public async Task<IActionResult> UpdateParticipant(Guid participantId, [FromBody] UpdateParticipantRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A person or company is required.")
            : ToAction(await work.UpdateParticipantAsync(participantId, new UpdateParticipantCommand(
                Tenant(tenantId), body.FirstName, body.LastName, body.JobTitle, body.OrganizationName, body.Website,
                body.LifecycleStage ?? string.Empty, body.Notes, body.Email, body.Phone), ct));

    /// <summary>Takes the record out of every list. Nothing is deleted.</summary>
    [HttpPost("participants/{participantId:guid}/archive")]
    public async Task<IActionResult> ArchiveParticipant(Guid participantId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await work.ArchiveParticipantAsync(Tenant(tenantId), participantId, ct));

    /// <summary>
    /// Permanently removes a person or company. Owner only. Refused while it is on a deal, in a customer holding or on a project;
    /// archive takes it out of the lists without deleting anything.
    /// </summary>
    [CrmOwnerOnly]
    [HttpDelete("participants/{participantId:guid}")]
    public async Task<IActionResult> DeleteParticipant(Guid participantId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await work.DeleteParticipantAsync(Tenant(tenantId), participantId, ct));

    // ----- Deals -----

    /// <summary>Deals with their company and primary contact named. <c>q</c> matches the deal's or the company's name.</summary>
    [HttpGet("deals/views")]
    public async Task<IActionResult> ListDealViews([FromQuery] bool openOnly, [FromQuery] string? q, [FromQuery] int limit, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await work.ListDealViewsAsync(Tenant(tenantId), openOnly, q, limit, ct));

    [HttpGet("deals/{dealId:guid}/view")]
    public async Task<IActionResult> GetDealView(Guid dealId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await work.GetDealViewAsync(Tenant(tenantId), dealId, ct));

    /// <summary>A new deal starts in <c>stageName</c> (the first stage when omitted).</summary>
    [HttpPost("deals")]
    public async Task<IActionResult> CreateDeal([FromBody] CreateDealRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A deal is required.")
            : ToAction(await crm.CreateDealAsync(new CreateDealCommand(
                Tenant(tenantId), body.Name ?? string.Empty, body.ProductCategory ?? string.Empty, body.OrganizationParticipantId,
                body.PrimaryContactParticipantId, null, body.EstimatedValue, null, body.StageName), ct));

    [HttpPut("deals/{dealId:guid}")]
    public async Task<IActionResult> UpdateDeal(Guid dealId, [FromBody] UpdateDealRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("A deal is required.")
            : ToAction(await work.UpdateDealAsync(dealId, new UpdateDealCommand(
                Tenant(tenantId), body.Name ?? string.Empty, body.ProductCategory ?? string.Empty, body.EstimatedValue, body.ExpectedCloseOn,
                body.OrganizationParticipantId, body.PrimaryContactParticipantId), ct));

    /// <summary>Moves the deal to the named stage. Winning a deal with line items is <c>POST deals/{id}/win</c>, which also makes its holdings.</summary>
    [HttpPost("deals/{dealId:guid}/stage")]
    public async Task<IActionResult> MoveDealStage(Guid dealId, [FromBody] MoveDealStageRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null || string.IsNullOrWhiteSpace(body.StageName)
            ? BadRequest("A stage is required.")
            : ToAction(await crm.MoveDealStageAsync(dealId, new MoveDealStageCommand(Tenant(tenantId), body.StageName, body.LostReason, Actor), ct));

    /// <summary>
    /// Permanently removes a deal with its stage history, line items and participants. Owner only. Its activities and any holdings made
    /// from it are kept, with the deal cleared.
    /// </summary>
    [CrmOwnerOnly]
    [HttpDelete("deals/{dealId:guid}")]
    public async Task<IActionResult> DeleteDeal(Guid dealId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await work.DeleteDealAsync(Tenant(tenantId), dealId, ct));

    // ----- Notes and tasks -----

    /// <summary>
    /// One person's or one deal's history, or everything. <c>openTasks=true</c> lists the tasks not yet completed, soonest due first.
    /// </summary>
    [HttpGet("activities/views")]
    public async Task<IActionResult> ListActivityViews([FromQuery] Guid? participantId, [FromQuery] Guid? dealId, [FromQuery] string? channel, [FromQuery] bool openTasks, [FromQuery] int limit, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await work.ListActivityViewsAsync(Tenant(tenantId), participantId, dealId, channel, openTasks, limit, ct));

    /// <summary>A note, a logged call, email, text or meeting, or a task (channel task, with an optional dueAt). It happened now unless occurredAt says otherwise.</summary>
    [HttpPost("activities")]
    public async Task<IActionResult> CreateActivity([FromBody] CreateActivityRequest? body, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        body is null
            ? BadRequest("An activity is required.")
            : ToAction(await work.CreateActivityAsync(new CreateActivityCommand(
                Tenant(tenantId), body.ParticipantId, body.DealId, body.Channel ?? string.Empty, body.Direction,
                body.OccurredAt ?? DateTime.UtcNow, body.Subject, body.Body, body.Outcome, body.DueAt, Actor), ct));

    [HttpPost("activities/{activityId:guid}/complete")]
    public async Task<IActionResult> CompleteTask(Guid activityId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await work.CompleteTaskAsync(Tenant(tenantId), activityId, ct));

    /// <summary>Permanently removes one note, call, email, text, meeting or task. Owner only.</summary>
    [CrmOwnerOnly]
    [HttpDelete("activities/{activityId:guid}")]
    public async Task<IActionResult> DeleteActivity(Guid activityId, [FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await work.DeleteActivityAsync(Tenant(tenantId), activityId, ct));
}

public sealed record CreateParticipantRequest(
    string? Kind,
    string? FirstName,
    string? LastName,
    string? JobTitle,
    string? OrganizationName,
    string? Website,
    string? Email,
    string? Phone,
    string? LifecycleStage,
    string? Source,
    string? Notes,
    Guid? LinkedOrganizationId,
    string? LinkRole);

public sealed record UpdateParticipantRequest(
    string? FirstName,
    string? LastName,
    string? JobTitle,
    string? OrganizationName,
    string? Website,
    string? LifecycleStage,
    string? Notes,
    string? Email,
    string? Phone);

public sealed record CreateDealRequest(string? Name, string? ProductCategory, Guid? OrganizationParticipantId, Guid? PrimaryContactParticipantId, decimal? EstimatedValue, string? StageName);

public sealed record UpdateDealRequest(string? Name, string? ProductCategory, decimal? EstimatedValue, DateOnly? ExpectedCloseOn, Guid? OrganizationParticipantId, Guid? PrimaryContactParticipantId);

public sealed record MoveDealStageRequest(string? StageName, string? LostReason);

public sealed record CreateActivityRequest(Guid? ParticipantId, Guid? DealId, string? Channel, string? Direction, DateTime? OccurredAt, string? Subject, string? Body, string? Outcome, DateTime? DueAt);
