namespace GeekApplication.Models.Social;

// The records the GeekSocial contracts pass around. They are plain classes and not records on
// purpose: a record prints every member from its generated ToString, and these carry other people's
// comment text, which is never logged. Timestamps are DateTime in UTC. A token is never a member of
// any of them; it travels beside them as a SocialToken.

public static class SocialAccountStatuses
{
    public const string Connected = "connected";

    /// <summary>The platform refused the token or it expired. The operator reconnects the account.</summary>
    public const string ReconnectRequired = "reconnect_required";

    public static readonly IReadOnlyList<string> All = new[] { Connected, ReconnectRequired };
}

/// <summary>One connected Page, Instagram account, LinkedIn organization or LinkedIn member.</summary>
public sealed class SocialAccount
{
    public Guid Id { get; set; }
    public string Platform { get; set; } = string.Empty;

    /// <summary>The platform's own id: a Page id, an Instagram account id, or an organization or person URN.</summary>
    public string PlatformAccountId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = SocialAccountStatuses.Connected;

    /// <summary>The reply categories this account sends without approval. Complaint and sensitive are never listed.</summary>
    public List<string> AutoSendCategories { get; set; } = new();

    /// <summary>The per-account kill switch: while true, nothing is sent from this account.</summary>
    public bool SendsDisabled { get; set; }

    public DateTime? TokenExpiresAt { get; set; }
    public DateTime ConnectedAt { get; set; }
}

/// <summary>What to publish. Which fields apply depends on <see cref="Kind"/> and the platform; see <see cref="SocialPostRequestValidator"/>.</summary>
public sealed class SocialPostRequest
{
    public string Kind { get; set; } = SocialPostKinds.Link;

    /// <summary>The post's commentary or caption.</summary>
    public string Text { get; set; } = string.Empty;

    public string? LinkUrl { get; set; }
    public string? LinkTitle { get; set; }
    public string? LinkDescription { get; set; }

    /// <summary>A publicly reachable image the platform fetches. A link post's thumbnail, or an Instagram post's picture.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>When to publish, in UTC, or null to publish now.</summary>
    public DateTime? ScheduledFor { get; set; }
}

/// <summary>What the platform confirmed about a publish.</summary>
public sealed class SocialPostResult
{
    public string PlatformPostId { get; set; } = string.Empty;
    public string? Permalink { get; set; }

    /// <summary>True when the platform accepted the post for a later time and it is not live yet.</summary>
    public bool Scheduled { get; set; }
}

/// <summary>A post the agent published, as stored.</summary>
public sealed class SocialPost
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string PlatformPostId { get; set; } = string.Empty;
    public string Kind { get; set; } = SocialPostKinds.Link;
    public string Text { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>The normalized kinds of engagement, whichever platform's spelling they arrived in.</summary>
public static class EngagementKinds
{
    public const string CommentCreated = "comment_created";
    public const string CommentEdited = "comment_edited";
    public const string CommentDeleted = "comment_deleted";
    public const string CommentHidden = "comment_hidden";
    public const string CommentUnhidden = "comment_unhidden";
    public const string ReactionAdded = "reaction_added";
    public const string ReactionRemoved = "reaction_removed";
    public const string Share = "share";
    public const string Mention = "mention";

    public static readonly IReadOnlyList<string> All = new[]
    {
        CommentCreated, CommentEdited, CommentDeleted, CommentHidden, CommentUnhidden,
        ReactionAdded, ReactionRemoved, Share, Mention,
    };

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}

/// <summary>
/// One thing that happened on a post, normalized from a webhook or a pull. Stored once per
/// (<see cref="Platform"/>, <see cref="PlatformEventId"/>): a platform redelivers, and the second
/// delivery must change nothing.
/// </summary>
public sealed class EngagementEvent
{
    public Guid Id { get; set; }

    /// <summary>The connected account the event belongs to. The store sets it from <see cref="PlatformAccountId"/>; a caller leaves it empty.</summary>
    public Guid AccountId { get; set; }

    public string Platform { get; set; } = string.Empty;

    /// <summary>The id the platform gave this event, or a value derived from its content when it gives none. The de-duplication key.</summary>
    public string PlatformEventId { get; set; } = string.Empty;

    public string PlatformAccountId { get; set; } = string.Empty;
    public string Kind { get; set; } = EngagementKinds.CommentCreated;
    public string? PlatformPostId { get; set; }
    public string? PlatformCommentId { get; set; }
    public string? ParentPlatformCommentId { get; set; }
    public string? AuthorPlatformId { get; set; }
    public string? AuthorDisplayName { get; set; }

    /// <summary>Another person's comment text. Never logged; kept no longer than the platform's retention limit.</summary>
    public string? Text { get; set; }

    public string? ReactionType { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }

    /// <summary>When <see cref="Text"/> must be gone, or null when the platform sets no limit. Set by the store.</summary>
    public DateTime? TextExpiresAt { get; set; }

    /// <summary>When <see cref="AuthorDisplayName"/> must be gone, or null when the platform sets no limit. Set by the store.</summary>
    public DateTime? AuthorExpiresAt { get; set; }

    /// <summary>A <see cref="TriageOutcomes"/> value, or null before Triage has run. Set only by RecordTriageOutcome; a value a caller sends with a new event is ignored.</summary>
    public string? TriageOutcome { get; set; }

    /// <summary>The <see cref="SocialFailureCodes"/> value when <see cref="TriageOutcome"/> is failed.</summary>
    public string? TriageFailureCode { get; set; }

    public DateTime? TriagedAt { get; set; }
}

/// <summary>One comment as a read call returns it.</summary>
public sealed class SocialComment
{
    public string PlatformCommentId { get; set; } = string.Empty;
    public string? ParentPlatformCommentId { get; set; }
    public string? AuthorPlatformId { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>A page of comments. <see cref="NextCursor"/> is null on the last page.</summary>
public sealed class SocialCommentPage
{
    public List<SocialComment> Comments { get; set; } = new();
    public string? NextCursor { get; set; }
}

/// <summary>A reply to a comment, sent as the account.</summary>
public sealed class SocialReplyRequest
{
    public string PlatformPostId { get; set; } = string.Empty;

    /// <summary>The comment being answered.</summary>
    public string ParentPlatformCommentId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class SocialReplyResult
{
    public string PlatformCommentId { get; set; } = string.Empty;
}

/// <summary>A reaction the account gives to a post or a comment.</summary>
public sealed class SocialReactionRequest
{
    public string TargetKind { get; set; } = SocialTargetKinds.Comment;
    public string PlatformTargetId { get; set; } = string.Empty;
    public string ReactionType { get; set; } = SocialReactionTypes.Like;
}

public sealed class SocialReactionResult
{
    /// <summary>The id the platform returned, or null when it confirms with a bare success.</summary>
    public string? PlatformReactionId { get; set; }
}

/// <summary>A webhook delivery as the endpoint received it: the untouched body bytes and the headers.</summary>
public sealed class SocialWebhookRequest
{
    /// <summary>The body exactly as received. A signature is checked against these bytes and never against a re-serialized copy.</summary>
    public byte[] RawBody { get; set; } = Array.Empty<byte>();

    /// <summary>Request headers, looked up case-insensitively: build it with <see cref="StringComparer.OrdinalIgnoreCase"/>.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The query string of the GET a platform sends to prove the endpoint is ours.</summary>
public sealed class SocialHandshakeRequest
{
    public IReadOnlyDictionary<string, string> Query { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>The answer to a handshake: what to write back and as which content type.</summary>
public sealed class SocialHandshakeResponse
{
    public string ContentType { get; set; } = "text/plain";
    public string Body { get; set; } = string.Empty;
}

/// <summary>An account the platform returned after a successful authorization, with its token.</summary>
public sealed class SocialConnectedAccount
{
    public string Platform { get; set; } = string.Empty;
    public string PlatformAccountId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public required SocialToken Token { get; set; }
}

/// <summary>Every account one authorization granted. One login can yield more than one.</summary>
public sealed class SocialConnection
{
    public List<SocialConnectedAccount> Accounts { get; set; } = new();
}

/// <summary>What the platform says about a token right now.</summary>
public sealed class SocialTokenStatus
{
    public bool IsValid { get; set; }
    public DateTime? ExpiresAt { get; set; }

    /// <summary>The permissions or scopes the token actually carries, as the platform names them.</summary>
    public List<string> GrantedScopes { get; set; } = new();
}

/// <summary>Whether the platform will deliver events for an account to the callback address.</summary>
public sealed class SocialWebhookSubscription
{
    public bool Active { get; set; }
    public List<string> Fields { get; set; } = new();
}

public static class ReplyCategories
{
    public const string Question = "question";
    public const string Praise = "praise";
    public const string Complaint = "complaint";
    public const string Spam = "spam";
    public const string Sensitive = "sensitive";

    public static readonly IReadOnlyList<string> All = new[] { Question, Praise, Complaint, Spam, Sensitive };

    /// <summary>Categories no setting can turn into an automatic send: a person always reads these first.</summary>
    public static readonly IReadOnlyList<string> NeverAutoSend = new[] { Complaint, Sensitive };

    public static bool IsKnown(string? category) => category is not null && All.Contains(category);
}

/// <summary>
/// A draft's life: pending, then approved or rejected; an approved draft is sending, then sent or
/// failed. A failed draft never becomes sending again, because a send is one attempt and never
/// automatically retried. A draft the Reviewer refused is created rejected, with the rules it broke,
/// so it is never pending and never sent.
/// </summary>
public static class ReplyDraftStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Failed = "failed";

    public static readonly IReadOnlyList<string> All = new[] { Pending, Approved, Rejected, Sending, Sent, Failed };

    private static readonly Dictionary<string, string[]> Next = new()
    {
        [Pending] = new[] { Approved, Rejected },
        [Approved] = new[] { Sending, Rejected },
        [Sending] = new[] { Sent, Failed },
        [Rejected] = Array.Empty<string>(),
        [Sent] = Array.Empty<string>(),
        [Failed] = Array.Empty<string>(),
    };

    public static bool IsKnown(string? status) => status is not null && All.Contains(status);

    public static bool CanTransition(string from, string to) =>
        Next.TryGetValue(from, out var allowed) && allowed.Contains(to);
}

/// <summary>A reply Claude drafted for one comment, waiting for a person.</summary>
public sealed class ReplyDraft
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid EngagementEventId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string Category { get; set; } = ReplyCategories.Question;

    /// <summary>The drafted reply. Never edited by code after Claude wrote it; a person edits it or rejects it.</summary>
    public string DraftText { get; set; } = string.Empty;

    /// <summary>Why a person must write this, when Triage said so. The draft text is then empty.</summary>
    public string? NeedsHumanReason { get; set; }

    /// <summary>The <see cref="ReviewRules"/> the Reviewer refused this draft for, or null when it passed. A refused draft is rejected.</summary>
    public List<string>? ReviewRefusal { get; set; }

    public string Status { get; set; } = ReplyDraftStatuses.Pending;
    public string? SentPlatformCommentId { get; set; }

    /// <summary>The <see cref="SocialFailureCodes"/> value when the status is failed.</summary>
    public string? FailureCode { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }

    /// <summary>When the send began. A draft left sending across a restart has an unknown outcome.</summary>
    public DateTime? SendingStartedAt { get; set; }
}

/// <summary>What the drafter is given for one comment: only what the reply needs.</summary>
public sealed class ReplyDraftRequest
{
    public string Platform { get; set; } = string.Empty;
    public string AccountDisplayName { get; set; } = string.Empty;

    /// <summary>What Triage decided the comment is. The drafter takes it as given and does not decide it again.</summary>
    public string Category { get; set; } = ReplyCategories.Question;

    /// <summary>Another person's comment text. See <see cref="Interfaces.Social.IReplyDrafter"/> for where it may go.</summary>
    public string CommentText { get; set; } = string.Empty;

    /// <summary>The text of the post the comment is under, for context.</summary>
    public string? PostText { get; set; }

    public string? AuthorDisplayName { get; set; }

    /// <summary>The only links the reply may carry; the Reviewer refuses any other. Empty means none.</summary>
    public List<string> ApprovedLinks { get; set; } = new();
}

/// <summary>The drafter's answer for one comment: the reply text and nothing else.</summary>
public sealed class ReplyDraftSuggestion
{
    public string DraftText { get; set; } = string.Empty;
}

public static class ActionKinds
{
    public const string Connect = "connect";
    public const string RefreshToken = "refresh_token";
    public const string SubscribeWebhook = "subscribe_webhook";
    public const string Publish = "publish";
    public const string Reply = "reply";
    public const string React = "react";

    public static readonly IReadOnlyList<string> All =
        new[] { Connect, RefreshToken, SubscribeWebhook, Publish, Reply, React };
}

public static class ActionOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    /// <summary>The call may have been applied and nothing confirmed it. Verify on the platform; never resend.</summary>
    public const string OutcomeUnknown = "outcome_unknown";

    public static readonly IReadOnlyList<string> All = new[] { Succeeded, Failed, OutcomeUnknown };
}

/// <summary>One row for every call the agent made to a platform that changed something there, or tried to.</summary>
public sealed class ActionAudit
{
    public Guid Id { get; set; }
    public Guid? AccountId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string Action { get; set; } = ActionKinds.Publish;
    public string Outcome { get; set; } = ActionOutcomes.Succeeded;

    /// <summary>The id the platform returned for what was created: a post, a comment or a reaction.</summary>
    public string? PlatformObjectId { get; set; }

    public Guid? ReplyDraftId { get; set; }

    /// <summary>A <see cref="SocialFailureCodes"/> value when the outcome is not succeeded.</summary>
    public string? FailureCode { get; set; }

    /// <summary>The operator-facing reason. Never a token, a comment body or a person's data.</summary>
    public string? FailureDetail { get; set; }

    /// <summary>The signed-in user who approved or started it, or null when the agent acted on its own.</summary>
    public string? ActorUserId { get; set; }

    public DateTime OccurredAt { get; set; }
}
