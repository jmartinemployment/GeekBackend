namespace GeekApplication.Models.ContentCreator;

/// <summary>
/// A project: one client's engagement, with its own schedule and its own work beneath it.
/// </summary>
/// <remarks>
/// Not an article. The record this replaces was keyed on target keyword plus site URL, which made
/// two pieces of content about one keyword the same row and gave a project nowhere to keep a due
/// date, an hour or a deliverable. A project belongs to exactly one client, and the client is not
/// derivable from anything here — <c>ClientId</c> is required and never inferred.
/// </remarks>
/// <param name="Code">Operator's short reference, unique within the client when present.</param>
/// <param name="Status">planned | active | on_hold | finished | cancelled.</param>
/// <param name="SiteUrl">The site this engagement targets; its Run ID is what content is grounded on.</param>
/// <param name="ProjectSiteRunId">The Geek-Crawler-v2 crawl run for <paramref name="SiteUrl"/>.</param>
/// <param name="FinishedDate">Set exactly when <paramref name="Status"/> is "finished"; the database enforces the pair.</param>
/// <param name="BudgetCurrency">ISO-4217, set with <paramref name="Budget"/> and null without it.</param>
public sealed record GccProjectDto(
    Guid Id,
    Guid ClientId,
    string Name,
    string? Code,
    string? Description,
    string Status,
    string? SiteUrl,
    Guid? ProjectSiteRunId,
    string? Department,
    IReadOnlyList<string> PartnerUrls,
    IReadOnlyList<string> CompetitorUrls,
    DateOnly StartDate,
    DateOnly? DueDate,
    DateOnly? FinishedDate,
    decimal? EstimatedHours,
    decimal? Budget,
    string? BudgetCurrency,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>
/// Create one project.
/// </summary>
/// <remarks>
/// The idempotency key is minted by the form that submits this, not by the server, so a double
/// submit of one form carries one key and yields one project. The repository resolves a repeat by
/// loading the row the key already names; a key belonging to another client is a conflict rather
/// than a second project.
///
/// The actor is the caller's token subject. GeekAPI sets it from the validated JWT and it is never
/// read from a request body — the log records who acted, so a value the caller can choose is worth
/// nothing.
/// </remarks>
public sealed record CreateGccProjectCommand(
    Guid ClientId,
    Guid IdempotencyKey,
    string Name,
    string ActorUserId,
    DateOnly StartDate,
    string? Code = null,
    string? Description = null,
    string? SiteUrl = null,
    Guid? ProjectSiteRunId = null,
    string? Department = null,
    IReadOnlyList<string>? PartnerUrls = null,
    IReadOnlyList<string>? CompetitorUrls = null,
    DateOnly? DueDate = null,
    decimal? EstimatedHours = null,
    decimal? Budget = null,
    string? BudgetCurrency = null);

/// <summary>
/// Update a project's profile and schedule.
/// </summary>
/// <remarks>
/// Status is not here. Moving a project to "finished" also sets its finish date and writes a
/// different log event, so it is its own operation rather than one more nullable field whose side
/// effects are invisible at the call site.
/// </remarks>
public sealed record UpdateGccProjectCommand(
    Guid Id,
    string ActorUserId,
    string Name,
    DateOnly StartDate,
    string? Code = null,
    string? Description = null,
    string? SiteUrl = null,
    Guid? ProjectSiteRunId = null,
    string? Department = null,
    IReadOnlyList<string>? PartnerUrls = null,
    IReadOnlyList<string>? CompetitorUrls = null,
    DateOnly? DueDate = null,
    decimal? EstimatedHours = null,
    decimal? Budget = null,
    string? BudgetCurrency = null);

/// <summary>
/// The outcome of a create: the project, or the reason there is not one.
/// </summary>
/// <remarks>
/// A result rather than an exception, so the caller decides the status code and nothing unwinds
/// through a stack to decide it for them. <c>Conflict</c> means the idempotency key already names a
/// project belonging to a different client — the one case where returning the row the key points at
/// would hand over someone else's work.
/// </remarks>
public sealed record GccProjectCreateResult(GccProjectDto? Project, bool Conflict)
{
    public static GccProjectCreateResult Created(GccProjectDto project) => new(project, false);

    /// <summary>The key was seen before under this client; this is the project it made.</summary>
    public static GccProjectCreateResult Existing(GccProjectDto project) => new(project, false);

    public static GccProjectCreateResult ConflictingClient() => new(null, true);
}

/// <summary>
/// Move a project to a new status.
/// </summary>
/// <remarks>
/// A finish date is required for "finished" and refused for every other status. The database
/// enforces the same pairing, so a caller cannot record a finished project with no finish date, or
/// a finish date on a project still running.
/// </remarks>
public sealed record ChangeGccProjectStatusCommand(
    Guid Id,
    string ActorUserId,
    string Status,
    DateOnly? FinishedDate = null);

/// <summary>
/// One entry in a project's log. Append-only: the database refuses updates and deletes.
/// </summary>
/// <param name="ActorUserId">The token subject of whoever caused the change.</param>
/// <param name="EventType">project_created | project_updated | project_status_changed.</param>
/// <param name="Payload">What changed, as JSON. Never empty — an event with no detail is not evidence.</param>
public sealed record GccProjectLogEntryDto(
    long Id,
    Guid ProjectId,
    DateTime OccurredAtUtc,
    string ActorUserId,
    string EventType,
    string Payload);

/// <summary>
/// The event types a project log entry may carry.
/// </summary>
/// <remarks>
/// The database carries the same list as a CHECK, and it grows one migration at a time: a new type
/// added here without altering that constraint is a write that fails at insert. That is the
/// intended direction — the log refuses an event it does not recognise rather than recording one
/// nothing can interpret later.
/// </remarks>
public static class GccProjectLogEventTypes
{
    public const string ProjectCreated = "project_created";
    public const string ProjectUpdated = "project_updated";
    public const string ProjectStatusChanged = "project_status_changed";

    public const string TaskCreated = "task_created";
    public const string TaskUpdated = "task_updated";
    public const string TaskCompleted = "task_completed";
    public const string TimeLogged = "time_logged";

    public const string DeliverableCreated = "deliverable_created";
    public const string DeliverableUpdated = "deliverable_updated";
    public const string DeliverableDelivered = "deliverable_delivered";

    public static readonly IReadOnlyList<string> All =
    [
        ProjectCreated,
        ProjectUpdated,
        ProjectStatusChanged,
        TaskCreated,
        TaskUpdated,
        TaskCompleted,
        TimeLogged,
        DeliverableCreated,
        DeliverableUpdated,
        DeliverableDelivered,
    ];
}

/// <summary>The statuses a project may hold. The database carries the same list as a CHECK.</summary>
public static class GccProjectStatuses
{
    public const string Planned = "planned";
    public const string Active = "active";
    public const string OnHold = "on_hold";
    public const string Finished = "finished";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All =
    [
        Planned,
        Active,
        OnHold,
        Finished,
        Cancelled,
    ];

    public static bool IsValid(string? status) =>
        status is not null && All.Contains(status, StringComparer.Ordinal);
}

/// <summary>A unit of work under a project.</summary>
/// <param name="Status">todo | in_progress | done.</param>
/// <param name="AssigneeUserId">The token subject it is assigned to, or null.</param>
/// <param name="SortOrder">The operator's ordering within the project — an order, not a priority.</param>
public sealed record GccTaskDto(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    string Status,
    string? AssigneeUserId,
    DateOnly? DueDate,
    decimal? EstimatedHours,
    int SortOrder,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateGccTaskCommand(
    Guid ProjectId,
    string Name,
    string ActorUserId,
    string? Description = null,
    string? AssigneeUserId = null,
    DateOnly? DueDate = null,
    decimal? EstimatedHours = null,
    int SortOrder = 0);

public sealed record UpdateGccTaskCommand(
    Guid Id,
    string ActorUserId,
    string Name,
    string Status,
    string? Description = null,
    string? AssigneeUserId = null,
    DateOnly? DueDate = null,
    decimal? EstimatedHours = null,
    int SortOrder = 0);

/// <summary>The statuses a task may hold. The database carries the same list as a CHECK.</summary>
public static class GccTaskStatuses
{
    public const string Todo = "todo";
    public const string InProgress = "in_progress";
    public const string Done = "done";

    public static readonly IReadOnlyList<string> All = [Todo, InProgress, Done];

    public static bool IsValid(string? status) =>
        status is not null && All.Contains(status, StringComparer.Ordinal);
}

/// <summary>
/// Effort logged against a project, and optionally one of its tasks.
/// </summary>
/// <param name="Minutes">Minutes, not fractional hours — 0.1h is a rounding argument.</param>
/// <param name="RateSnapshot">The client's rate when this was logged. A later change cannot move it.</param>
/// <param name="InvoicedAtUtc">Once set, the row is frozen: the database refuses updates and deletes.</param>
public sealed record GccTimeEntryDto(
    Guid Id,
    Guid ProjectId,
    Guid? TaskId,
    string UserId,
    DateOnly WorkDate,
    int Minutes,
    string? Description,
    bool Billable,
    decimal? RateSnapshot,
    string? Currency,
    DateTime? InvoicedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>
/// Log time.
/// </summary>
/// <remarks>
/// Neither the rate nor the currency is here. Both are read from the client row inside the insert
/// transaction, because a caller that could name its own rate could bill anything.
/// </remarks>
public sealed record CreateGccTimeEntryCommand(
    Guid ProjectId,
    string UserId,
    DateOnly WorkDate,
    int Minutes,
    bool Billable,
    Guid? TaskId = null,
    string? Description = null);

/// <summary>
/// What a project's logged time adds up to.
/// </summary>
/// <remarks>
/// Billable money is summed per currency, never across them. One number spanning two currencies is
/// not a total, it is a mistake with a decimal point.
/// </remarks>
public sealed record GccProjectTimeTotals(
    int TotalMinutes,
    int BillableMinutes,
    IReadOnlyList<GccBillableTotal> Billable);

public sealed record GccBillableTotal(string Currency, int Minutes, decimal Amount);

/// <summary>Why time could not be logged. Null Reason means it was.</summary>
public sealed record GccTimeEntryResult(GccTimeEntryDto? Entry, string? Reason)
{
    public static GccTimeEntryResult Logged(GccTimeEntryDto entry) => new(entry, null);
    public static GccTimeEntryResult Refused(string reason) => new(null, reason);
}

/// <summary>Something the client receives, backed by the create that produces it.</summary>
/// <param name="CreateId">The GccCreate this deliverable is. One create, one deliverable.</param>
/// <param name="Status">planned | in_progress | delivered.</param>
/// <param name="DeliveredAtUtc">Set exactly when delivered; the database enforces the pair.</param>
public sealed record GccDeliverableDto(
    Guid Id,
    Guid ProjectId,
    Guid CreateId,
    string Name,
    string Type,
    string Status,
    DateOnly? DueDate,
    DateTime? DeliveredAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateGccDeliverableCommand(
    Guid ProjectId,
    Guid CreateId,
    string Name,
    string ActorUserId,
    string Type = "long-form",
    DateOnly? DueDate = null);

/// <summary>
/// Move a deliverable to a new status.
/// </summary>
/// <remarks>
/// Delivering it stamps the moment. Like a project's finish date, the pair is enforced by the
/// database: delivered without a timestamp cannot be reported on, and a timestamp on something
/// still in progress is a claim nothing backs.
/// </remarks>
public sealed record ChangeGccDeliverableStatusCommand(
    Guid Id,
    string ActorUserId,
    string Status);

/// <summary>The statuses a deliverable may hold. The database carries the same list as a CHECK.</summary>
public static class GccDeliverableStatuses
{
    public const string Planned = "planned";
    public const string InProgress = "in_progress";
    public const string Delivered = "delivered";

    public static readonly IReadOnlyList<string> All = [Planned, InProgress, Delivered];

    public static bool IsValid(string? status) =>
        status is not null && All.Contains(status, StringComparer.Ordinal);
}

/// <summary>Why a deliverable could not be recorded. Null Reason means it was.</summary>
public sealed record GccDeliverableResult(GccDeliverableDto? Deliverable, string? Reason)
{
    public static GccDeliverableResult Created(GccDeliverableDto deliverable) => new(deliverable, null);
    public static GccDeliverableResult Refused(string reason) => new(null, reason);
}
