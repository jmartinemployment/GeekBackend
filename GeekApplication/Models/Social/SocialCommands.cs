namespace GeekApplication.Models.Social;

// What GeekAPI sends to GeekRepository's repo/social/* routes and what comes back. The store keeps
// a token only as AES-256-GCM ciphertext that GeekAPI produced: GeekRepository never holds the key
// and never sees a token in the clear. Timestamps are UTC.

/// <summary>A token as stored: the ciphertext, its nonce and its authentication tag.</summary>
public sealed class SocialEncryptedToken
{
    public byte[] Cipher { get; set; } = Array.Empty<byte>();
    public byte[] Iv { get; set; } = Array.Empty<byte>();
    public byte[] Tag { get; set; } = Array.Empty<byte>();
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Records an account after its owner authorized it, or replaces the token of one already connected.</summary>
public sealed class ConnectSocialAccountCommand
{
    public string Platform { get; set; } = string.Empty;
    public string PlatformAccountId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public SocialEncryptedToken Token { get; set; } = new();
    public DateTime ConnectedAt { get; set; }
}

/// <summary>An account's stored token, for GeekAPI to decrypt and use.</summary>
public sealed class SocialAccountCredential
{
    public Guid AccountId { get; set; }
    public SocialEncryptedToken Token { get; set; } = new();
}

/// <summary>A renewed token for an account that stays connected.</summary>
public sealed class StoreSocialTokenCommand
{
    public SocialEncryptedToken Token { get; set; } = new();
}

/// <summary>The two settings an owner changes: which categories send without approval, and the kill switch.</summary>
public sealed class UpdateSocialAccountSettingsCommand
{
    public List<string> AutoSendCategories { get; set; } = new();
    public bool SendsDisabled { get; set; }
}

public sealed class RecordSocialPostCommand
{
    public Guid AccountId { get; set; }
    public string PlatformPostId { get; set; } = string.Empty;
    public string Kind { get; set; } = SocialPostKinds.Link;
    public string Text { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public DateTime? PublishedAt { get; set; }
}

/// <summary>
/// A draft a person will read. The event it answers must already be stored. When the Reviewer refused
/// the text, <see cref="ReviewRefusal"/> names the rules it broke and the store records the draft as
/// rejected, so the operator sees what was refused and nothing can send it.
/// </summary>
public sealed class CreateReplyDraftCommand
{
    public Guid EngagementEventId { get; set; }
    public string Category { get; set; } = ReplyCategories.Question;
    public string DraftText { get; set; } = string.Empty;
    public string? NeedsHumanReason { get; set; }

    /// <summary>The <see cref="ReviewRules"/> the draft broke, or null when it passed.</summary>
    public List<string>? ReviewRefusal { get; set; }
}

/// <summary>A person's edit of a draft that has not been decided.</summary>
public sealed class EditReplyDraftCommand
{
    public string DraftText { get; set; } = string.Empty;
}

/// <summary>
/// One step of a draft's life. It lands only if the draft is still in <see cref="From"/>, so two
/// callers racing for the same draft have exactly one winner and the loser is told it lost.
/// </summary>
public sealed class TransitionReplyDraftCommand
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public DateTime At { get; set; }

    /// <summary>Required when <see cref="To"/> is sent.</summary>
    public string? SentPlatformCommentId { get; set; }

    /// <summary>Required when <see cref="To"/> is failed: a <see cref="SocialFailureCodes"/> value.</summary>
    public string? FailureCode { get; set; }
}

/// <summary>What a retention purge removed. Counts only; no text.</summary>
public sealed class SocialPurgeResult
{
    public int EventsScrubbed { get; set; }
}

/// <summary>The drafts a restart found mid-send, now failed with an unknown outcome and audited.</summary>
public sealed class SocialInterruptedSends
{
    public List<Guid> DraftIds { get; set; } = new();
}

/// <summary>Whether a recorded event was new. False is a redelivery the store already held.</summary>
public sealed class SocialEventStored
{
    public bool Inserted { get; set; }
}

/// <summary>Asks the store to scrub every event whose text or author name is past its retention deadline as of <see cref="AsOf"/>.</summary>
public sealed class SocialPurgeCommand
{
    public DateTime AsOf { get; set; }
}

/// <summary>Asks the store to fail every draft still sending, as of <see cref="At"/>.</summary>
public sealed class SocialFailInterruptedSendsCommand
{
    public DateTime At { get; set; }
}
