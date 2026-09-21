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

/// <summary>The event types a project log entry may carry at this stage.</summary>
public static class GccProjectLogEventTypes
{
    public const string ProjectCreated = "project_created";
    public const string ProjectUpdated = "project_updated";
    public const string ProjectStatusChanged = "project_status_changed";

    public static readonly IReadOnlyList<string> All =
    [
        ProjectCreated,
        ProjectUpdated,
        ProjectStatusChanged,
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
