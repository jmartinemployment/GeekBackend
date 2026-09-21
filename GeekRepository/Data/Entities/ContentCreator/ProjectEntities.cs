namespace GeekRepository.Data.Entities.ContentCreator;

/// <summary>
/// One client's engagement: its profile, its schedule, and the root everything else hangs from.
/// </summary>
/// <remarks>
/// Storage type. It exists only in GeekRepository — GeekAPI carries request/response contracts and
/// never this class, so a new field is one table and one contract rather than two stores that can
/// disagree.
///
/// <see cref="IdempotencyKey"/> is unique across the table, not per client. A key that already
/// exists under another client is a conflict the repository refuses: reusing it would hand the
/// caller a project belonging to someone else, which is the defect that made the old
/// keyword-plus-URL dedupe dangerous once projects carry billing.
/// </remarks>
public class GccProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public Guid IdempotencyKey { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Operator's short reference. Unique within the client when present.</summary>
    public string? Code { get; set; }
    public string? Description { get; set; }

    /// <summary>planned | active | on_hold | finished | cancelled. The database carries the same list.</summary>
    public string Status { get; set; } = "planned";

    /// <summary>The site this engagement targets.</summary>
    public string? SiteUrl { get; set; }

    /// <summary>The Geek-Crawler-v2 crawl run for <see cref="SiteUrl"/> — what content is grounded on.</summary>
    public Guid? ProjectSiteRunId { get; set; }

    public string? Department { get; set; }

    /// <summary>Sites this client sells or recommends, as the operator declared them.</summary>
    public List<string> PartnerUrls { get; set; } = [];

    /// <summary>Rivals writing on the same topics, as the operator declared them.</summary>
    public List<string> CompetitorUrls { get; set; } = [];

    public DateOnly StartDate { get; set; }
    public DateOnly? DueDate { get; set; }

    /// <summary>Set exactly when <see cref="Status"/> is "finished". A CHECK enforces the pair.</summary>
    public DateOnly? FinishedDate { get; set; }

    public decimal? EstimatedHours { get; set; }
    public decimal? Budget { get; set; }

    /// <summary>ISO-4217. Set with <see cref="Budget"/> and null without it; a CHECK enforces the pair.</summary>
    public string? BudgetCurrency { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Soft-delete marker. Null means live. A project always has a project_created log row, and
    /// that row can never be removed (the log is append-only, and its FK back to this table is
    /// RESTRICT) — so this column, not a real DELETE, is what "delete a project" means here.
    /// </summary>
    public DateTime? DeletedAtUtc { get; set; }
}

/// <summary>
/// One recorded change to a project. Append-only, and — since 2026-09-21 — deletable one row at a
/// time.
/// </summary>
/// <remarks>
/// A trigger refuses UPDATE on this table, so a row can never be edited into agreeing with a story
/// told after the fact. DELETE is no longer refused by that same trigger — Jeff chose a true,
/// permanent delete for a single entry over a hide-only alternative — but every deletion is itself
/// logged (log_entry_deleted), so the log still shows that something was removed and by whom, even
/// though the removed entry's own content is genuinely gone. Every write that carries an event type
/// inserts its entry in the same transaction as the change: never a change without its entry, never
/// an entry without its change.
///
/// The key is a bigint identity rather than a Guid. Entries are read in the order they happened and
/// never addressed from elsewhere, so a monotonic key is both cheaper and the natural sort.
/// </remarks>
public class GccProjectLogEntry
{
    public long Id { get; set; }
    public Guid ProjectId { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>The token subject of whoever acted. Text, with no FK — that table is another database.</summary>
    public string ActorUserId { get; set; } = string.Empty;

    /// <summary>project_created | project_updated | project_status_changed.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>What changed, as jsonb.</summary>
    public string Payload { get; set; } = "{}";
}

/// <summary>
/// A unit of work under a project.
/// </summary>
/// <remarks>
/// The unique (Id, ProjectId) pair exists for the composite foreign key on
/// <see cref="GccTimeEntry"/>: it is what lets the database refuse an entry whose task belongs to a
/// different project. Without it that check would have to live in application code, where a missed
/// call means hours quietly billed against the wrong engagement.
/// </remarks>
public class GccTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>todo | in_progress | done. The database carries the same list.</summary>
    public string Status { get; set; } = "todo";

    /// <summary>The token subject of whoever it is assigned to. Text, no FK — another database.</summary>
    public string? AssigneeUserId { get; set; }

    public DateOnly? DueDate { get; set; }
    public decimal? EstimatedHours { get; set; }

    /// <summary>Operator's ordering within the project. Not a priority, just an order.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Which of the twenty v2 content types this task relates to, if any. Declared, not enforced —
    /// no CHECK constraint ties this to the frontend's list, on purpose: that list is still
    /// settling, and pinning it in the database now would mean a migration every time it changes.
    /// Always empty rather than not required in the sense that matters here: a task with none
    /// checked is a completely ordinary task, not an incomplete one.
    /// </summary>
    public List<string> ContentTypes { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Effort logged against a project, and optionally against one of its tasks.
/// </summary>
/// <remarks>
/// <see cref="RateSnapshot"/> and <see cref="Currency"/> are copied from the client row when the
/// entry is written, never accepted from the caller and never resolved at read time. A rate change
/// next month must not silently restate what last month cost; an invoice sent against these hours
/// has to keep meaning what it meant.
///
/// Once <see cref="InvoicedAtUtc"/> is set the row is frozen — a trigger refuses any update or
/// delete. Money that has left the building is not editable.
/// </remarks>
public class GccTimeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }

    /// <summary>Optional, but when set the database requires it to belong to the same project.</summary>
    public Guid? TaskId { get; set; }

    /// <summary>Who logged it: the JWT subject, set by GeekAPI and never from a request body.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>The day worked, not the instant recorded — a calendar date, as a timesheet has.</summary>
    public DateOnly WorkDate { get; set; }

    /// <summary>Minutes, not fractional hours: 0.1h is a rounding argument waiting to happen.</summary>
    public int Minutes { get; set; }

    public string? Description { get; set; }
    public bool Billable { get; set; }

    /// <summary>The client's rate at the moment of logging. Required when billable.</summary>
    public decimal? RateSnapshot { get; set; }

    /// <summary>The client's currency at the moment of logging. Required when billable.</summary>
    public string? Currency { get; set; }

    public DateTime? InvoicedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Something the client receives, backed by the create that produces it.
/// </summary>
/// <remarks>
/// The project-side record of a piece of content, not a second copy of it. The content pipeline is
/// untouched: the create, its artifacts, its versions and its approvals stay exactly where they
/// are, and this row says which project promised the thing and when it was delivered.
///
/// <see cref="CreateId"/> is unique across the table. One create is one deliverable — listing the
/// same piece of content under two projects would make both schedules and both invoices wrong, and
/// there would be no way to tell which.
/// </remarks>
public class GccDeliverable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }

    /// <summary>The GccCreate this deliverable is. Unique: one create, one deliverable.</summary>
    public Guid CreateId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>What kind of thing it is — long-form, social, and so on. Free text by design.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>planned | in_progress | delivered. The database carries the same list.</summary>
    public string Status { get; set; } = "planned";

    public DateOnly? DueDate { get; set; }

    /// <summary>Set exactly when <see cref="Status"/> is "delivered". A CHECK enforces the pair.</summary>
    public DateTime? DeliveredAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
