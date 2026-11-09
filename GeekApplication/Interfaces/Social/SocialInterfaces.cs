using GeekApplication.Models.Social;

namespace GeekApplication.Interfaces.Social;

// The GeekSocial contracts. Each platform (SocialPlatforms) has its own implementation of the first
// four, registered under the platform string as a keyed service, because every platform is active
// at once. The rules every implementation keeps:
//
//  - A failure is a failed Result with a SocialFailureCodes value. Nothing is swallowed into a
//    default, and a call the platform has no way to do answers not_supported without being made.
//  - A call that changes something on a platform is one attempt. It is never retried here; a
//    timeout after the request left is outcome_unknown, which the caller records and never resends.
//  - A token, a comment body or a person's data never appears in an error message or a log line.
//  - Success means the platform confirmed it, with the id it returned.

/// <summary>Publishes posts as one account.</summary>
public interface ISocialPublisher
{
    /// <summary>The <see cref="SocialPlatforms"/> value this implementation serves.</summary>
    string Platform { get; }

    /// <summary>
    /// Checks the request with <see cref="SocialPostRequestValidator"/>, then publishes it, or schedules it
    /// when the request carries a time. The result carries the platform's own post id.
    /// </summary>
    Task<Result<SocialPostResult>> PublishAsync(
        SocialAccount account, SocialToken token, SocialPostRequest request, CancellationToken ct = default);
}

/// <summary>Reads comments and turns webhook deliveries into <see cref="EngagementEvent"/> rows.</summary>
public interface ISocialEngagementReader
{
    string Platform { get; }

    /// <summary>
    /// Answers the GET a platform sends to prove the endpoint is ours. It is synchronous and uses
    /// in-memory configuration only: LinkedIn allows three seconds and blocks the endpoint after
    /// three failures, so nothing here may touch the database, GeekRepository or the network.
    /// </summary>
    Result<SocialHandshakeResponse> AnswerHandshake(SocialHandshakeRequest request);

    /// <summary>
    /// Verifies the delivery's signature against its raw body, then returns its events normalized.
    /// A body that fails the check is invalid_signature and yields no events.
    /// </summary>
    Result<IReadOnlyList<EngagementEvent>> ParseWebhook(SocialWebhookRequest request);

    /// <summary>Reads one page of the comments under a post, for gap recovery and for the operator's resync.</summary>
    Task<Result<SocialCommentPage>> ListCommentsAsync(
        SocialAccount account, SocialToken token, string platformPostId, string? cursor, CancellationToken ct = default);
}

/// <summary>Replies and reacts as one account.</summary>
public interface ISocialEngagementWriter
{
    string Platform { get; }

    /// <summary>Checks the request with <see cref="SocialReplyRequestValidator"/>, then sends one reply.</summary>
    Task<Result<SocialReplyResult>> ReplyAsync(
        SocialAccount account, SocialToken token, SocialReplyRequest request, CancellationToken ct = default);

    /// <summary>
    /// Checks the request with <see cref="SocialReactionRequestValidator"/>, so a reaction the platform
    /// has no such type for is not_supported before any call, then sends one reaction.
    /// </summary>
    Task<Result<SocialReactionResult>> ReactAsync(
        SocialAccount account, SocialToken token, SocialReactionRequest request, CancellationToken ct = default);
}

/// <summary>Connects an account: the authorization, the token's upkeep and the webhook subscription.</summary>
public interface ISocialAccountConnector
{
    string Platform { get; }

    /// <summary>The address to send the owner to for the platform's own login and consent screen.</summary>
    Result<Uri> BuildAuthorizationUrl(string state, Uri redirectUri);

    /// <summary>Trades the code the platform sent back for the accounts it granted and their tokens.</summary>
    Task<Result<SocialConnection>> ExchangeAuthorizationCodeAsync(
        string code, Uri redirectUri, CancellationToken ct = default);

    /// <summary>Renews a token before it expires, where the platform allows it.</summary>
    Task<Result<SocialToken>> RefreshTokenAsync(SocialToken token, CancellationToken ct = default);

    /// <summary>Asks the platform whether the token is still good and which permissions it carries.</summary>
    Task<Result<SocialTokenStatus>> GetTokenStatusAsync(SocialToken token, CancellationToken ct = default);

    /// <summary>Subscribes the account's events to <paramref name="callbackUrl"/>, or confirms that it already is.</summary>
    Task<Result<SocialWebhookSubscription>> EnsureWebhookSubscriptionAsync(
        SocialAccount account, SocialToken token, Uri callbackUrl, CancellationToken ct = default);
}

// The agents below are each one responsibility, and none calls another: the orchestrator carries one
// agent's result to the next. Triage, the drafter and the post writer are the only three that call
// Claude. A comment's text reaches only Triage and the drafter.

/// <summary>
/// Decides what a comment is, with Claude. It is not keyed by platform: one implementation serves all.
/// It returns whether a reply is needed, the category, and whether a person must write it instead of
/// the drafter. It never writes reply text, never touches the store and never calls a platform.
/// <para>
/// A request carries another person's comment text, which is platform data. Where it may be sent is
/// decided by the platforms' terms and not by this contract: LinkedIn has not agreed in writing that a
/// member's comment may go to Claude (matrix section 1, item 1), and Meta's service-provider terms are
/// open item M4. The implementation arrives with Phase 4a and takes an explicit operator clearance per
/// platform before it sends any comment text; it fails closed without one.
/// </para>
/// </summary>
public interface IEngagementTriage
{
    Task<Result<TriageResult>> TriageAsync(TriageRequest request, CancellationToken ct = default);
}

/// <summary>
/// Writes a reply with Claude, for a comment Triage said needs one and did not reserve for a person.
/// It is not keyed by platform: one drafter serves them all. It returns the reply text and nothing
/// else: it does not decide the category or whether to reply. It never rewrites a draft to pass a
/// check; the Reviewer refuses a draft that fails one and the operator sees it. The request carries
/// the same comment text as <see cref="IEngagementTriage"/> and is held to the same clearance.
/// </summary>
public interface IReplyDrafter
{
    Task<Result<ReplyDraftSuggestion>> DraftAsync(ReplyDraftRequest request, CancellationToken ct = default);
}

/// <summary>
/// Writes post copy for an article or link, one <see cref="PostCopy"/> per platform asked for, as a
/// suggestion for a person to edit in the post form. It never posts and never makes an image.
/// </summary>
public interface IPostWriter
{
    Task<Result<PostCopySuggestion>> WriteAsync(PostCopyRequest request, CancellationToken ct = default);
}

/// <summary>
/// Checks any outbound text (a draft, an edit a person made, post copy) against the voice and safety
/// rules in <see cref="ReviewRules"/>. It never rewrites. It is synchronous plain code and calls
/// nothing: a refusal is a successful review with the rules broken, and a failed Result means it
/// could not run.
/// </summary>
public interface IOutboundTextReviewer
{
    Result<OutboundTextReview> Review(OutboundText text);
}

/// <summary>
/// Hands out a usable token for an account. It is the only component that decrypts a stored token. It
/// renews a token that is within its renewal window when one is asked for, and marks the account
/// reconnect_required when renewal fails, answering the request with that failure. There is no timer.
/// </summary>
public interface ISocialTokenProvider
{
    Task<Result<SocialToken>> GetTokenAsync(Guid accountId, CancellationToken ct = default);
}
