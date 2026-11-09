using GeekApplication.Models.Social;

namespace GeekApplication.Interfaces.Social;

/// <summary>
/// The geek_social store. GeekRepository implements it over its own Postgres and GeekAPI calls it
/// over HTTP (repo/social/*), so both sides read the same contract. Every method answers a Result
/// carrying a <see cref="SocialFailureCodes"/> value on failure: a store that is down is a
/// failure the caller sees, never an empty list or a null that looks like "nothing there".
/// </summary>
public interface ISocialStore
{
    // Accounts. A token is only ever ciphertext here.
    Task<Result<SocialAccount>> ConnectAccountAsync(ConnectSocialAccountCommand command, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SocialAccount>>> ListAccountsAsync(CancellationToken ct = default);
    Task<Result<SocialAccount>> GetAccountAsync(Guid accountId, CancellationToken ct = default);
    Task<Result<SocialAccountCredential>> GetCredentialAsync(Guid accountId, CancellationToken ct = default);
    Task<Result<SocialAccount>> StoreTokenAsync(Guid accountId, StoreSocialTokenCommand command, CancellationToken ct = default);
    Task<Result<SocialAccount>> MarkReconnectRequiredAsync(Guid accountId, CancellationToken ct = default);
    Task<Result<SocialAccount>> UpdateSettingsAsync(Guid accountId, UpdateSocialAccountSettingsCommand command, CancellationToken ct = default);

    // Posts
    Task<Result<SocialPost>> RecordPostAsync(RecordSocialPostCommand command, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SocialPost>>> ListPostsAsync(Guid accountId, int limit, CancellationToken ct = default);

    // Engagement events. A platform delivers the same event more than once.

    /// <summary>True when this call stored the event, false when it was already stored (a redelivery). Either way the store holds exactly one.</summary>
    Task<Result<bool>> RecordEventAsync(EngagementEvent engagementEvent, CancellationToken ct = default);

    /// <summary>The event with its text and author name, or without them once their retention has run out.</summary>
    Task<Result<EngagementEvent>> GetEventAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Events by where Triage left them: a <see cref="TriageOutcomes"/> filter value, where
    /// <see cref="TriageOutcomes.Untriaged"/> is the comment events with no outcome yet, oldest first, for
    /// recovery after a restart. Any other filter is newest first. Text and names past their retention
    /// are never returned.
    /// </summary>
    Task<Result<IReadOnlyList<EngagementEvent>>> ListEventsAsync(string triageState, Guid? accountId, int limit, CancellationToken ct = default);

    /// <summary>
    /// Records what Triage decided on a comment event. It lands from no outcome, or from one an operator
    /// may re-run (<see cref="TriageOutcomes.Rerunnable"/>); a decided event answers conflict. Only
    /// comment_created events are triaged.
    /// </summary>
    Task<Result<EngagementEvent>> RecordTriageOutcomeAsync(Guid eventId, RecordTriageOutcomeCommand command, CancellationToken ct = default);

    // Reply drafts
    Task<Result<ReplyDraft>> CreateDraftAsync(CreateReplyDraftCommand command, CancellationToken ct = default);
    Task<Result<ReplyDraft>> GetDraftAsync(Guid draftId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ReplyDraft>>> ListDraftsAsync(string? status, Guid? accountId, int limit, CancellationToken ct = default);
    Task<Result<ReplyDraft>> EditDraftAsync(Guid draftId, EditReplyDraftCommand command, CancellationToken ct = default);
    Task<Result<ReplyDraft>> TransitionDraftAsync(Guid draftId, TransitionReplyDraftCommand command, CancellationToken ct = default);

    // Audit
    Task<Result<ActionAudit>> RecordAuditAsync(ActionAudit audit, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ActionAudit>>> ListAuditAsync(Guid? accountId, int limit, CancellationToken ct = default);

    // Retention and recovery. Each is one call; the caller decides when to make it.

    /// <summary>Scrubs the text and author name of every event whose retention has run out as of <paramref name="asOf"/>.</summary>
    Task<Result<SocialPurgeResult>> PurgeExpiredAsync(DateTime asOf, CancellationToken ct = default);

    /// <summary>
    /// Fails every draft still sending, with <see cref="SocialFailureCodes.OutcomeUnknown"/>, and writes an
    /// audit row for each. A restart in the middle of a send cannot say whether the platform got it, so
    /// the draft is never sent again and the operator checks on the platform.
    /// </summary>
    Task<Result<SocialInterruptedSends>> FailInterruptedSendsAsync(DateTime at, CancellationToken ct = default);
}
