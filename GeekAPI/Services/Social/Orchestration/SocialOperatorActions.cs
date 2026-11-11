using GeekAPI.Services.Social.Listener;
using GeekAPI.Services.Social.Realtime;
using GeekAPI.Services.Social.Sending;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.Orchestration;

/// <summary>A change the Reviewer checked first: either it was made, or the Reviewer refused the text and nothing changed.</summary>
public sealed class SocialReviewed<T>
{
    public T? Value { get; init; }

    /// <summary>The <see cref="ReviewRules"/> the text broke, or null when it passed and the change was made.</summary>
    public IReadOnlyList<string>? Violations { get; init; }

    public static SocialReviewed<T> Made(T value) => new() { Value = value };

    public static SocialReviewed<T> Refused(IReadOnlyList<string> violations) => new() { Violations = violations };
}

/// <summary>An approval and the send that followed it.</summary>
public sealed class SocialApproval
{
    public required ReplyDraft Draft { get; init; }
    public required SocialSendOutcome Send { get; init; }
}

/// <summary>
/// What an operator does from the screen: edit, approve, reject, publish, like and resync. Every route
/// in api/social/* reaches the agents and the store through here, never directly. Text a person wrote
/// or approved passes the Reviewer before it is stored or sent; a send goes through the send step and
/// the Sender, once. Nothing here calls a platform or a language model itself.
/// </summary>
public sealed class SocialOperatorActions
{
    private readonly ISocialStore _store;
    private readonly IOutboundTextReviewer _reviewer;
    private readonly ISocialSendStep _sendStep;
    private readonly ISocialTokenProvider _tokens;
    private readonly SocialSender _sender;
    private readonly SocialResync _resync;
    private readonly IPostWriter _postWriter;
    private readonly ISocialRealtime _realtime;
    private readonly TimeProvider _clock;
    private readonly ILogger<SocialOperatorActions> _logger;

    public SocialOperatorActions(
        ISocialStore store,
        IOutboundTextReviewer reviewer,
        ISocialSendStep sendStep,
        ISocialTokenProvider tokens,
        SocialSender sender,
        SocialResync resync,
        IPostWriter postWriter,
        ISocialRealtime realtime,
        TimeProvider clock,
        ILogger<SocialOperatorActions> logger)
    {
        _store = store;
        _reviewer = reviewer;
        _sendStep = sendStep;
        _tokens = tokens;
        _sender = sender;
        _resync = resync;
        _postWriter = postWriter;
        _realtime = realtime;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>A person's edit of a pending draft. The Reviewer checks the new text first; a refusal changes nothing.</summary>
    public async Task<Result<SocialReviewed<ReplyDraft>>> EditAsync(Guid draftId, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result<SocialReviewed<ReplyDraft>>.Failure("The reply needs text.", SocialFailureCodes.InvalidRequest);

        var draft = await _store.GetDraftAsync(draftId, ct).ConfigureAwait(false);
        if (!draft.IsSuccess)
            return Pass<SocialReviewed<ReplyDraft>>(draft.Error, draft.ErrorCode, "load the draft");
        if (draft.Value!.Status != ReplyDraftStatuses.Pending)
            return Result<SocialReviewed<ReplyDraft>>.Failure($"The draft is {draft.Value.Status}; only a pending draft is edited.", SocialFailureCodes.Conflict);

        var violations = await ReviewReplyAsync(draft.Value, text, ct).ConfigureAwait(false);
        if (!violations.IsSuccess)
            return Result<SocialReviewed<ReplyDraft>>.Failure(violations.Error!, violations.ErrorCode!);
        if (violations.Value is { } broken)
            return Result<SocialReviewed<ReplyDraft>>.Success(SocialReviewed<ReplyDraft>.Refused(broken));

        var edited = await _store.EditDraftAsync(draftId, new EditReplyDraftCommand { DraftText = text.Trim() }, ct).ConfigureAwait(false);
        if (!edited.IsSuccess)
            return Pass<SocialReviewed<ReplyDraft>>(edited.Error, edited.ErrorCode, "edit the draft");

        await _realtime.DraftChangedAsync(draftId, edited.Value!.AccountId, edited.Value.Status, ct).ConfigureAwait(false);
        return Result<SocialReviewed<ReplyDraft>>.Success(SocialReviewed<ReplyDraft>.Made(edited.Value));
    }

    /// <summary>
    /// Approves a pending draft whose text passes the Reviewer, then sends it at once. A draft a person
    /// must write is approved only after its text is written. A refusal approves nothing.
    /// </summary>
    public async Task<Result<SocialReviewed<SocialApproval>>> ApproveAsync(Guid draftId, string actorUserId, CancellationToken ct)
    {
        var draft = await _store.GetDraftAsync(draftId, ct).ConfigureAwait(false);
        if (!draft.IsSuccess)
            return Pass<SocialReviewed<SocialApproval>>(draft.Error, draft.ErrorCode, "load the draft");
        if (draft.Value!.Status != ReplyDraftStatuses.Pending)
            return Result<SocialReviewed<SocialApproval>>.Failure($"The draft is {draft.Value.Status}; only a pending draft is approved.", SocialFailureCodes.Conflict);
        if (string.IsNullOrWhiteSpace(draft.Value.DraftText))
            return Result<SocialReviewed<SocialApproval>>.Failure("Write the reply and save it before approving.", SocialFailureCodes.InvalidRequest);

        var violations = await ReviewReplyAsync(draft.Value, draft.Value.DraftText, ct).ConfigureAwait(false);
        if (!violations.IsSuccess)
            return Result<SocialReviewed<SocialApproval>>.Failure(violations.Error!, violations.ErrorCode!);
        if (violations.Value is { } broken)
            return Result<SocialReviewed<SocialApproval>>.Success(SocialReviewed<SocialApproval>.Refused(broken));

        var approved = await _store.TransitionDraftAsync(draftId, new TransitionReplyDraftCommand
        {
            From = ReplyDraftStatuses.Pending,
            To = ReplyDraftStatuses.Approved,
            At = Now(),
        }, ct).ConfigureAwait(false);
        if (!approved.IsSuccess)
            return Pass<SocialReviewed<SocialApproval>>(approved.Error, approved.ErrorCode, "approve the draft");

        _logger.LogInformation("Draft {DraftId} was approved by {UserId}.", draftId, actorUserId);
        await _realtime.DraftChangedAsync(draftId, approved.Value!.AccountId, ReplyDraftStatuses.Approved, ct).ConfigureAwait(false);

        var sent = await _sendStep.SendAsync(draftId, actorUserId, ct).ConfigureAwait(false);
        var outcome = sent.IsSuccess
            ? sent.Value!
            : new SocialSendOutcome { Code = sent.ErrorCode, Error = $"Approved, and the send could not start: {sent.Error}", Draft = approved.Value };
        return Result<SocialReviewed<SocialApproval>>.Success(SocialReviewed<SocialApproval>.Made(new SocialApproval
        {
            Draft = outcome.Draft ?? approved.Value,
            Send = outcome,
        }));
    }

    /// <summary>Sends an approved draft a limit or a pause held back.</summary>
    public Task<Result<SocialSendOutcome>> SendAsync(Guid draftId, string actorUserId, CancellationToken ct) =>
        _sendStep.SendAsync(draftId, actorUserId, ct);

    /// <summary>Rejects a pending or approved draft. Nothing is sent and it cannot be sent later.</summary>
    public async Task<Result<ReplyDraft>> RejectAsync(Guid draftId, string actorUserId, CancellationToken ct)
    {
        var draft = await _store.GetDraftAsync(draftId, ct).ConfigureAwait(false);
        if (!draft.IsSuccess)
            return Pass<ReplyDraft>(draft.Error, draft.ErrorCode, "load the draft");
        if (draft.Value!.Status is not (ReplyDraftStatuses.Pending or ReplyDraftStatuses.Approved))
            return Result<ReplyDraft>.Failure($"The draft is {draft.Value.Status} and can no longer be rejected.", SocialFailureCodes.Conflict);

        var rejected = await _store.TransitionDraftAsync(draftId, new TransitionReplyDraftCommand
        {
            From = draft.Value.Status,
            To = ReplyDraftStatuses.Rejected,
            At = Now(),
        }, ct).ConfigureAwait(false);
        if (!rejected.IsSuccess)
            return Pass<ReplyDraft>(rejected.Error, rejected.ErrorCode, "reject the draft");

        _logger.LogInformation("Draft {DraftId} was rejected by {UserId}.", draftId, actorUserId);
        await _realtime.DraftChangedAsync(draftId, rejected.Value!.AccountId, ReplyDraftStatuses.Rejected, ct).ConfigureAwait(false);
        return rejected;
    }

    /// <summary>The Post Writer's suggestion for an article. Nothing is stored or posted.</summary>
    public Task<Result<PostCopySuggestion>> SuggestPostCopyAsync(PostCopyRequest request, CancellationToken ct) =>
        _postWriter.WriteAsync(request, ct);

    /// <summary>
    /// Publishes a post a person wrote or edited. The Reviewer checks its text (the post's own link is the
    /// only one it may carry); the Sender publishes it once; then it is stored. A post that went live and
    /// could not be stored is reported as such, with the platform's id, and is never published again.
    /// </summary>
    public async Task<Result<SocialReviewed<SocialPost>>> PublishAsync(Guid accountId, SocialPostRequest post, string actorUserId, CancellationToken ct)
    {
        var account = await _store.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        if (!account.IsSuccess)
            return Pass<SocialReviewed<SocialPost>>(account.Error, account.ErrorCode, "load the account");
        if (account.Value!.Status != SocialAccountStatuses.Connected)
            return Result<SocialReviewed<SocialPost>>.Failure("The account must be reconnected before it can post.", SocialFailureCodes.TokenInvalid);
        if (account.Value.SendsDisabled)
            return Result<SocialReviewed<SocialPost>>.Failure("Sends are paused for this account.", SocialFailureCodes.Conflict);
        if (string.IsNullOrWhiteSpace(post.Text))
            return Result<SocialReviewed<SocialPost>>.Failure("A post needs text.", SocialFailureCodes.InvalidRequest);

        var review = _reviewer.Review(new OutboundText
        {
            Kind = OutboundTextKinds.Post,
            Platform = account.Value.Platform,
            Text = post.Text,
            ApprovedLinks = string.IsNullOrWhiteSpace(post.LinkUrl) ? new List<string>() : new List<string> { post.LinkUrl },
        });
        if (!review.IsSuccess)
            return Result<SocialReviewed<SocialPost>>.Failure(review.Error!, review.ErrorCode ?? SocialFailureCodes.InvalidRequest);
        if (!review.Value!.Passed)
            return Result<SocialReviewed<SocialPost>>.Success(SocialReviewed<SocialPost>.Refused(review.Value.Violations));

        var token = await _tokens.GetTokenAsync(accountId, ct).ConfigureAwait(false);
        if (!token.IsSuccess)
            return Pass<SocialReviewed<SocialPost>>(token.Error, token.ErrorCode, "get the token");

        var published = await _sender.PublishAsync(account.Value, token.Value!, post, actorUserId, ct).ConfigureAwait(false);
        if (!published.IsSuccess)
            return Result<SocialReviewed<SocialPost>>.Failure(published.Error!, published.ErrorCode!);

        var now = Now();
        var stored = await _store.RecordPostAsync(new RecordSocialPostCommand
        {
            AccountId = accountId,
            PlatformPostId = published.Value!.PlatformPostId,
            Kind = post.Kind,
            Text = post.Text,
            LinkUrl = post.LinkUrl,
            ScheduledFor = post.ScheduledFor,
            PublishedAt = published.Value.Scheduled ? null : now,
        }, ct).ConfigureAwait(false);
        if (!stored.IsSuccess)
        {
            _logger.LogCritical("Post {PlatformPostId} is live on account {AccountId} and could not be stored: {Code}.",
                published.Value.PlatformPostId, accountId, stored.ErrorCode);
            return Result<SocialReviewed<SocialPost>>.Failure(
                $"The post is live (id {published.Value.PlatformPostId}) and could not be recorded: {stored.Error} Do not publish it again.",
                stored.ErrorCode ?? SocialFailureCodes.StoreUnavailable);
        }

        _logger.LogInformation("Account {AccountId} published post {PlatformPostId}.", accountId, published.Value.PlatformPostId);
        return Result<SocialReviewed<SocialPost>>.Success(SocialReviewed<SocialPost>.Made(stored.Value!));
    }

    /// <summary>Likes the comment an event is about, as the account. One attempt, audited by the Sender.</summary>
    public async Task<Result<SocialReactionResult>> ReactAsync(Guid eventId, string reactionType, string actorUserId, CancellationToken ct)
    {
        var ev = await _store.GetEventAsync(eventId, ct).ConfigureAwait(false);
        if (!ev.IsSuccess)
            return Pass<SocialReactionResult>(ev.Error, ev.ErrorCode, "load the comment");
        if (string.IsNullOrWhiteSpace(ev.Value!.PlatformCommentId))
            return Result<SocialReactionResult>.Failure("This event names no comment to react to.", SocialFailureCodes.InvalidRequest);

        var account = await _store.GetAccountAsync(ev.Value.AccountId, ct).ConfigureAwait(false);
        if (!account.IsSuccess)
            return Pass<SocialReactionResult>(account.Error, account.ErrorCode, "load the account");
        if (account.Value!.Status != SocialAccountStatuses.Connected)
            return Result<SocialReactionResult>.Failure("The account must be reconnected before it can react.", SocialFailureCodes.TokenInvalid);
        if (account.Value.SendsDisabled)
            return Result<SocialReactionResult>.Failure("Sends are paused for this account.", SocialFailureCodes.Conflict);

        var token = await _tokens.GetTokenAsync(account.Value.Id, ct).ConfigureAwait(false);
        if (!token.IsSuccess)
            return Pass<SocialReactionResult>(token.Error, token.ErrorCode, "get the token");

        return await _sender.ReactAsync(account.Value, token.Value!, new SocialReactionRequest
        {
            TargetKind = SocialTargetKinds.Comment,
            PlatformTargetId = ev.Value.PlatformCommentId,
            ReactionType = reactionType,
        }, actorUserId, ct).ConfigureAwait(false);
    }

    /// <summary>Reads the comments the webhooks may have missed under the account's posts and takes in the new ones.</summary>
    public Task<Result<SocialResyncResult>> ResyncAsync(Guid accountId, CancellationToken ct) =>
        _resync.ResyncAsync(accountId, ct);

    /// <summary>Saves which categories an account sends without a person, and its pause switch. Complaint and sensitive never auto-send.</summary>
    public async Task<Result<SocialAccount>> UpdateSettingsAsync(Guid accountId, UpdateSocialAccountSettingsCommand command, CancellationToken ct)
    {
        var unknown = command.AutoSendCategories.FirstOrDefault(c => !ReplyCategories.IsKnown(c));
        if (unknown is not null)
            return Result<SocialAccount>.Failure($"'{unknown}' is not a reply category.", SocialFailureCodes.InvalidRequest);

        var never = command.AutoSendCategories.FirstOrDefault(c => ReplyCategories.NeverAutoSend.Contains(c));
        if (never is not null)
            return Result<SocialAccount>.Failure($"A {never} reply always waits for a person; it cannot auto-send.", SocialFailureCodes.InvalidRequest);

        var updated = await _store.UpdateSettingsAsync(accountId, new UpdateSocialAccountSettingsCommand
        {
            AutoSendCategories = command.AutoSendCategories.Distinct(StringComparer.Ordinal).ToList(),
            SendsDisabled = command.SendsDisabled,
        }, ct).ConfigureAwait(false);
        if (!updated.IsSuccess)
            return Pass<SocialAccount>(updated.Error, updated.ErrorCode, "save the account settings");

        _logger.LogInformation("Account {AccountId} settings saved: auto-send {Categories}, sends paused {Paused}.",
            accountId, string.Join(", ", updated.Value!.AutoSendCategories), updated.Value.SendsDisabled);
        await _realtime.AccountChangedAsync(accountId, updated.Value.Status, ct).ConfigureAwait(false);
        return updated;
    }

    /// <summary>Null when the reply text passes, the rules it breaks when it does not, or a failed Result when the Reviewer could not run.</summary>
    private async Task<Result<IReadOnlyList<string>?>> ReviewReplyAsync(ReplyDraft draft, string text, CancellationToken ct)
    {
        var ev = await _store.GetEventAsync(draft.EngagementEventId, ct).ConfigureAwait(false);
        if (!ev.IsSuccess)
            return Pass<IReadOnlyList<string>?>(ev.Error, ev.ErrorCode, "load the comment");

        var links = await _sendStep.ApprovedLinksAsync(ev.Value!, ct).ConfigureAwait(false);
        if (!links.IsSuccess)
            return Pass<IReadOnlyList<string>?>(links.Error, links.ErrorCode, "load the post");

        var review = _reviewer.Review(new OutboundText
        {
            Kind = OutboundTextKinds.Reply,
            Platform = draft.Platform,
            Text = text,
            ApprovedLinks = links.Value!,
        });
        if (!review.IsSuccess)
            return Result<IReadOnlyList<string>?>.Failure(review.Error!, review.ErrorCode ?? SocialFailureCodes.InvalidRequest);

        return Result<IReadOnlyList<string>?>.Success(review.Value!.Passed ? null : review.Value.Violations);
    }

    /// <summary>Carries a failure on with its code, logged at the step that failed.</summary>
    private Result<T> Pass<T>(string? error, string? code, string step)
    {
        _logger.LogError("GeekSocial could not {Step}: {Code}. {Error}", step, code, error);
        return Result<T>.Failure(error ?? $"Could not {step}.", code ?? SocialFailureCodes.StoreUnavailable);
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
}
