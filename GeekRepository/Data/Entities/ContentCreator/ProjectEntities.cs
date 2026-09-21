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
}

/// <summary>
/// One recorded change to a project. Append-only.
/// </summary>
/// <remarks>
/// A trigger refuses UPDATE and DELETE on this table, so the log cannot be edited into agreement
/// with the row it describes. Every write that carries an event type inserts its entry in the same
/// transaction as the change: never a change without its entry, never an entry without its change.
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
