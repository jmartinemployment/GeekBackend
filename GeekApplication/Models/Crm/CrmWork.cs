namespace GeekApplication.Models.Crm;

// What a person at the keyboard does in Geek-CRM: find people and companies, add and edit them, work deals through the
// pipeline, and keep notes and tasks. JSON camelCase on the wire, like every other Crm model.

/// <summary>One column of the pipeline, in order.</summary>
public sealed record CrmStage(Guid Id, string Name, int Position, string Outcome, int? ProbabilityPct);

/// <summary>A deal with the names a screen shows beside it: the company, and the person who is the primary contact.</summary>
public sealed record CrmDealView(CrmDeal Deal, string? OrganizationName, Guid? PrimaryContactId, string? PrimaryContactName);

/// <summary>An activity with the names of who and what it belongs to, for lists that are not already inside one person or deal.</summary>
public sealed record CrmActivityView(CrmActivity Activity, string? ParticipantName, string? DealName);

/// <summary>How many deals sit in a stage and what they are worth (estimated value; monthly recurring value separately).</summary>
public sealed record CrmStageTotal(Guid StageId, string Name, int Position, string Outcome, int DealCount, decimal EstimatedValue, decimal RecurringValueMonthly);

/// <summary>The numbers the home screen shows. Counted by the database, so a long list never makes them wrong.</summary>
public sealed record CrmDashboard(
    IReadOnlyList<CrmStageTotal> Stages,
    int People,
    int Organizations,
    int OpenTasks,
    int OverdueTasks,
    int UrgentMessages);

// ----- Commands -----

/// <summary>
/// A person (FirstName/LastName, optionally JobTitle) or an organization (OrganizationName, optionally Website). Email and
/// PhoneE164 become the primary contact points. A person can be linked to an existing company at the same time.
/// </summary>
public sealed record CreateParticipantCommand(
    Guid TenantId,
    string Kind,
    string? FirstName,
    string? LastName,
    string? JobTitle,
    string? OrganizationName,
    string? Website,
    string? Email,
    string? PhoneE164,
    string LifecycleStage,
    string Source,
    string? Notes,
    Guid? LinkedOrganizationId,
    string? LinkRole);

/// <summary>
/// Replaces the editable fields of a person or company. A non-empty Email or PhoneE164 becomes the primary contact point of its
/// kind (the old primary's value is replaced); an empty one leaves the contact points alone, so a form that does not show
/// them cannot wipe them.
/// </summary>
public sealed record UpdateParticipantCommand(
    Guid TenantId,
    string? FirstName,
    string? LastName,
    string? JobTitle,
    string? OrganizationName,
    string? Website,
    string LifecycleStage,
    string? Notes,
    string? Email,
    string? PhoneE164);

/// <summary>
/// Name, category, expected close date and the company are always replaced. EstimatedValue can be changed only while the deal
/// has no line items (with line items the value is their sum). PrimaryContactParticipantId replaces the deal's primary contact.
/// </summary>
public sealed record UpdateDealCommand(
    Guid TenantId,
    string Name,
    string ProductCategory,
    decimal? EstimatedValue,
    DateOnly? ExpectedCloseOn,
    Guid? OrganizationParticipantId,
    Guid? PrimaryContactParticipantId);

/// <summary>
/// A note, a logged call, email, text or meeting, or a task (channel task; DueAt is when it is due). It belongs to a person, a
/// deal, or both. OccurredAt is when it happened (a task's creation time).
/// </summary>
public sealed record CreateActivityCommand(
    Guid TenantId,
    Guid? ParticipantId,
    Guid? DealId,
    string Channel,
    string? Direction,
    DateTime OccurredAt,
    string? Subject,
    string? Body,
    string? Outcome,
    DateTime? DueAt,
    string CreatedBy);
