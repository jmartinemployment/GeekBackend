namespace GeekRepository.Data.Entities.Social;

// One class per table of the geek_social schema (Migrations/Sql/0043_geek_social_schema.sql).
// SocialDbContext names every column as the snake_case of its property, so a property here and a
// column there are the same word. Instants are UTC.

public sealed class SocialAccountRow
{
    public Guid Id { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string PlatformAccountId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> AutoSendCategories { get; set; } = new();
    public bool SendsDisabled { get; set; }
    public byte[] TokenCipher { get; set; } = Array.Empty<byte>();
    public byte[] TokenIv { get; set; } = Array.Empty<byte>();
    public byte[] TokenTag { get; set; } = Array.Empty<byte>();
    public DateTime? TokenExpiresAt { get; set; }
    public DateTime ConnectedAt { get; set; }
    public int Version { get; set; }
}

public sealed class SocialPostRow
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string PlatformPostId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class EngagementEventRow
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string PlatformEventId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? PlatformPostId { get; set; }
    public string? PlatformCommentId { get; set; }
    public string? ParentPlatformCommentId { get; set; }
    public string? AuthorPlatformId { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string? Text { get; set; }
    public string? ReactionType { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? TextExpiresAt { get; set; }
    public DateTime? AuthorExpiresAt { get; set; }
    public string? TriageOutcome { get; set; }
    public string? TriageFailureCode { get; set; }
    public DateTime? TriagedAt { get; set; }
}

public sealed class ReplyDraftRow
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid EngagementEventId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DraftText { get; set; } = string.Empty;
    public string? NeedsHumanReason { get; set; }
    public List<string>? ReviewRefusal { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? SentPlatformCommentId { get; set; }
    public string? FailureCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime? SendingStartedAt { get; set; }
}

public sealed class ActionAuditRow
{
    public Guid Id { get; set; }
    public Guid? AccountId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? PlatformObjectId { get; set; }
    public Guid? ReplyDraftId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureDetail { get; set; }
    public string? ActorUserId { get; set; }
    public DateTime OccurredAt { get; set; }
}
