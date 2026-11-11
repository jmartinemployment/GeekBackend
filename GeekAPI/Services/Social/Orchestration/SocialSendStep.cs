using GeekAPI.Services.Social.Realtime;
using GeekAPI.Services.Social.Sending;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.Orchestration;

/// <summary>
/// The house limits on sending. They are not a platform's published numbers (Meta publishes none for
/// a Page's comments): they keep a burst of replies from looking automated and give a throttled
/// platform time to recover. Both are derived from the audit rows, so a restart does not forget them.
/// </summary>
public static class SocialSendLimits
{
    /// <summary>The most replies one account sends in any hour, counting every attempt.</summary>
    public const int RepliesPerAccountPerHour = 30;

    /// <summary>How long an account sends nothing after a send it made was throttled or may not have landed.</summary>
    public static readonly TimeSpan CoolDown = TimeSpan.FromMinutes(15);

    /// <summary>The failures that open a cool-down. A refusal of the request itself (invalid, not supported) does not.</summary>
    public static readonly IReadOnlyList<string> CoolDownCodes = new[]
    {
        SocialFailureCodes.RateLimited, SocialFailureCodes.PlatformUnavailable, SocialFailureCodes.OutcomeUnknown,
    };

    /// <summary>How many recent audit rows are read to apply the limits (the store's own maximum).</summary>
    internal const int AuditLookback = 200;
}

/// <summary>Where one send attempt ended, for the screen.</summary>
public sealed class SocialSendOutcome
{
    /// <summary>The platform confirmed the reply.</summary>
    public bool Sent { get; init; }

    /// <summary>Nothing was sent and the draft is still approved: a limit, a pause or the token kept it back.</summary>
    public bool Held { get; init; }

    /// <summary>The <see cref="SocialFailureCodes"/> value when it was not sent.</summary>
    public string? Code { get; init; }

    public string? Error { get; init; }

    /// <summary>When a held draft can go, when a limit is what holds it.</summary>
    public DateTime? RetryAfter { get; init; }

    public ReplyDraft? Draft { get; init; }
}

/// <summary>The orchestrator's send step, as the orchestrator and the operator routes reach it.</summary>
public interface ISocialSendStep
{
    /// <summary>Approves and sends a pending draft the account sends without a person; null when the draft is not one to auto-send.</summary>
    Task<Result<SocialSendOutcome>?> AutoSendAsync(ReplyDraft draft, SocialAccount account, CancellationToken ct);

    /// <summary>Sends one approved draft.</summary>
    Task<Result<SocialSendOutcome>> SendAsync(Guid draftId, string? actorUserId, CancellationToken ct);

    /// <summary>Sends the approved drafts a limit or a pause held back. Returns how many were sent.</summary>
    Task<int> SendWaitingAsync(CancellationToken ct);

    /// <summary>The links a reply to this comment may carry.</summary>
    Task<Result<List<string>>> ApprovedLinksAsync(EngagementEvent ev, CancellationToken ct);
}

/// <summary>
/// The orchestrator's send step for a reply draft (the plan's steps 7 and 8), and auto-send. It checks
/// the draft is approved, the account connected and not paused, the text still passes the Reviewer,
/// and the limits; it gets the token; then it moves the draft from approved to sending by
/// compare-and-set, hands it to the Sender once, and stores sent or failed. It never calls a platform
/// itself and never calls a language model.
/// </summary>
/// <remarks>
/// The token is asked for before the draft moves to sending, not after: a token that cannot be had
/// (the account must be reconnected, or Meta is down while it is renewed) means nothing was sent, so
/// the draft stays approved and is not failed for good. A send that reached the platform and failed
/// leaves the draft failed and is never sent again.
/// </remarks>
public sealed class SocialSendStep : ISocialSendStep
{
    /// <summary>How many approved drafts one pass over the waiting ones reads.</summary>
    internal const int WaitingBatch = 50;

    private readonly ISocialStore _store;
    private readonly ISocialTokenProvider _tokens;
    private readonly SocialSender _sender;
    private readonly IOutboundTextReviewer _reviewer;
    private readonly SocialOrchestratorStatus _status;
    private readonly ISocialRealtime _realtime;
    private readonly TimeProvider _clock;
    private readonly ILogger<SocialSendStep> _logger;

    public SocialSendStep(
        ISocialStore store,
        ISocialTokenProvider tokens,
        SocialSender sender,
        IOutboundTextReviewer reviewer,
        SocialOrchestratorStatus status,
        ISocialRealtime realtime,
        TimeProvider clock,
        ILogger<SocialSendStep> logger)
    {
        _store = store;
        _tokens = tokens;
        _sender = sender;
        _reviewer = reviewer;
        _status = status;
        _realtime = realtime;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Approves a pending draft the account sends without a person, and sends it. It applies only to a
    /// category the account opted into, never complaint or sensitive, only to text the Reviewer passed
    /// and never to a draft a person must write. Returns null when the draft is not one to auto-send.
    /// </summary>
    public async Task<Result<SocialSendOutcome>?> AutoSendAsync(ReplyDraft draft, SocialAccount account, CancellationToken ct)
    {
        if (!IsAutoSendable(draft, account))
            return null;

        var approved = await _store.TransitionDraftAsync(draft.Id, new TransitionReplyDraftCommand
        {
            From = ReplyDraftStatuses.Pending,
            To = ReplyDraftStatuses.Approved,
            At = Now(),
        }, ct).ConfigureAwait(false);
        if (!approved.IsSuccess)
        {
            Fail("auto-approve a draft", approved.ErrorCode, approved.Error, draft.Id);
            return Result<SocialSendOutcome>.Failure(approved.Error!, approved.ErrorCode ?? SocialFailureCodes.StoreUnavailable);
        }

        _status.RecordAutoApproved();
        _logger.LogInformation("Draft {DraftId} ({Category}) was approved by the account's auto-send setting.", draft.Id, draft.Category);
        await _realtime.DraftChangedAsync(draft.Id, draft.AccountId, ReplyDraftStatuses.Approved, ct).ConfigureAwait(false);
        return await SendAsync(draft.Id, actorUserId: null, ct).ConfigureAwait(false);
    }

    internal static bool IsAutoSendable(ReplyDraft draft, SocialAccount account) =>
        draft.Status == ReplyDraftStatuses.Pending
        && draft.ReviewRefusal is null
        && draft.NeedsHumanReason is null
        && !string.IsNullOrWhiteSpace(draft.DraftText)
        && account.Status == SocialAccountStatuses.Connected
        && !account.SendsDisabled
        && account.AutoSendCategories.Contains(draft.Category)
        && !ReplyCategories.NeverAutoSend.Contains(draft.Category);

    /// <summary>
    /// Sends one approved draft. A failed Result means the step could not run (the store, or the draft is
    /// not approved); a send that was held or failed is a successful Result whose outcome says so.
    /// </summary>
    public async Task<Result<SocialSendOutcome>> SendAsync(Guid draftId, string? actorUserId, CancellationToken ct)
    {
        var loaded = await _store.GetDraftAsync(draftId, ct).ConfigureAwait(false);
        if (!loaded.IsSuccess)
            return StepFailed<SocialSendOutcome>("load the draft", loaded.ErrorCode, loaded.Error, draftId);

        var draft = loaded.Value!;
        if (draft.Status != ReplyDraftStatuses.Approved)
            return Result<SocialSendOutcome>.Failure(
                $"The draft is {draft.Status}; only an approved draft is sent.", SocialFailureCodes.Conflict);

        var account = await _store.GetAccountAsync(draft.AccountId, ct).ConfigureAwait(false);
        if (!account.IsSuccess)
            return StepFailed<SocialSendOutcome>("load the account", account.ErrorCode, account.Error, draftId);

        if (account.Value!.Status != SocialAccountStatuses.Connected)
            return Held(draft, SocialFailureCodes.TokenInvalid, "The account must be reconnected before it can send.", null);
        if (account.Value.SendsDisabled)
            return Held(draft, SocialFailureCodes.Conflict, "Sends are paused for this account.", null);

        var ev = await _store.GetEventAsync(draft.EngagementEventId, ct).ConfigureAwait(false);
        if (!ev.IsSuccess)
            return StepFailed<SocialSendOutcome>("load the comment", ev.ErrorCode, ev.Error, draftId);

        // The text is checked again here, whoever approved it: nothing leaves without passing the Reviewer.
        var links = await ApprovedLinksAsync(ev.Value!, ct).ConfigureAwait(false);
        if (!links.IsSuccess)
            return StepFailed<SocialSendOutcome>("load the post", links.ErrorCode, links.Error, draftId);

        var refusal = Review(draft, links.Value!);
        if (!refusal.IsSuccess)
            return StepFailed<SocialSendOutcome>("review the draft", refusal.ErrorCode, refusal.Error, draftId);
        if (refusal.Value is { } violations)
            return await RejectUnsendableAsync(draft, violations, ct).ConfigureAwait(false);

        var limit = await CheckLimitsAsync(draft.AccountId, ct).ConfigureAwait(false);
        if (!limit.IsSuccess)
            return StepFailed<SocialSendOutcome>("read the audit for the limits", limit.ErrorCode, limit.Error, draftId);
        if (limit.Value is { } retryAfter)
            return Held(draft, SocialFailureCodes.RateLimited,
                $"A send limit holds this draft until {retryAfter:u}. It goes at the next wake after that, or with Send.", retryAfter);

        var token = await _tokens.GetTokenAsync(draft.AccountId, ct).ConfigureAwait(false);
        if (!token.IsSuccess)
            return Held(draft, token.ErrorCode ?? SocialFailureCodes.TokenInvalid, token.Error ?? "No token for the account.", null);

        var sending = await _store.TransitionDraftAsync(draft.Id, new TransitionReplyDraftCommand
        {
            From = ReplyDraftStatuses.Approved,
            To = ReplyDraftStatuses.Sending,
            At = Now(),
        }, ct).ConfigureAwait(false);
        if (!sending.IsSuccess)
        {
            // Conflict here is a pause or reconnect that landed meanwhile, or another sender winning the draft.
            if (sending.ErrorCode == SocialFailureCodes.Conflict)
                return Held(draft, SocialFailureCodes.Conflict, sending.Error!, null);
            return StepFailed<SocialSendOutcome>("move the draft to sending", sending.ErrorCode, sending.Error, draftId);
        }

        await _realtime.DraftChangedAsync(draft.Id, draft.AccountId, ReplyDraftStatuses.Sending, ct).ConfigureAwait(false);

        var reply = await _sender.ReplyAsync(account.Value, token.Value!, new SocialReplyRequest
        {
            PlatformPostId = ev.Value!.PlatformPostId ?? string.Empty,
            ParentPlatformCommentId = ev.Value.PlatformCommentId ?? string.Empty,
            Text = draft.DraftText,
        }, draft.Id, actorUserId, ct).ConfigureAwait(false);

        return reply.IsSuccess
            ? await RecordSentAsync(draft, reply.Value!.PlatformCommentId, ct).ConfigureAwait(false)
            : await RecordSendFailedAsync(draft, reply.ErrorCode, reply.Error, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the approved drafts a limit or a pause held back, oldest first. An account that is held
    /// again is not tried for the rest of the pass. Called by the orchestrator at startup and after each
    /// event it takes in; there is no timer.
    /// </summary>
    public async Task<int> SendWaitingAsync(CancellationToken ct)
    {
        var waiting = await _store.ListDraftsAsync(ReplyDraftStatuses.Approved, null, WaitingBatch, ct).ConfigureAwait(false);
        if (!waiting.IsSuccess)
        {
            Fail("list approved drafts", waiting.ErrorCode, waiting.Error, null);
            return 0;
        }

        var heldAccounts = new HashSet<Guid>();
        var sent = 0;
        foreach (var draft in waiting.Value!.OrderBy(d => d.DecidedAt ?? d.CreatedAt))
        {
            if (heldAccounts.Contains(draft.AccountId))
                continue;

            var outcome = await SendAsync(draft.Id, actorUserId: null, ct).ConfigureAwait(false);
            if (!outcome.IsSuccess)
                continue;
            if (outcome.Value!.Held)
                heldAccounts.Add(draft.AccountId);
            if (outcome.Value.Sent)
                sent++;
        }

        return sent;
    }

    /// <summary>The links a reply may carry: the link of the post it is under, when that post is stored.</summary>
    public async Task<Result<List<string>>> ApprovedLinksAsync(EngagementEvent ev, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ev.PlatformPostId))
            return Result<List<string>>.Success(new List<string>());

        var posts = await _store.ListPostsAsync(ev.AccountId, SocialOrchestrator.PostLookup, ct).ConfigureAwait(false);
        if (!posts.IsSuccess)
            return Result<List<string>>.Failure(posts.Error!, posts.ErrorCode ?? SocialFailureCodes.StoreUnavailable);

        var post = posts.Value!.FirstOrDefault(p => string.Equals(p.PlatformPostId, ev.PlatformPostId, StringComparison.Ordinal));
        return Result<List<string>>.Success(
            string.IsNullOrWhiteSpace(post?.LinkUrl) ? new List<string>() : new List<string> { post!.LinkUrl! });
    }

    /// <summary>Null when the text may be sent, the rules it breaks when it may not; a failed Result when the Reviewer could not run.</summary>
    private Result<List<string>?> Review(ReplyDraft draft, List<string> links)
    {
        if (string.IsNullOrWhiteSpace(draft.DraftText))
            return Result<List<string>?>.Success(new List<string>());

        var review = _reviewer.Review(new OutboundText
        {
            Kind = OutboundTextKinds.Reply,
            Platform = draft.Platform,
            Text = draft.DraftText,
            ApprovedLinks = links,
        });
        if (!review.IsSuccess)
            return Result<List<string>?>.Failure(review.Error!, review.ErrorCode ?? SocialFailureCodes.InvalidRequest);

        return Result<List<string>?>.Success(review.Value!.Passed ? null : review.Value.Violations.ToList());
    }

    /// <summary>An approved draft that is empty or breaks a rule is rejected, not sent: approving it was a mistake the screen should have stopped.</summary>
    private async Task<Result<SocialSendOutcome>> RejectUnsendableAsync(ReplyDraft draft, List<string> violations, CancellationToken ct)
    {
        var rejected = await _store.TransitionDraftAsync(draft.Id, new TransitionReplyDraftCommand
        {
            From = ReplyDraftStatuses.Approved,
            To = ReplyDraftStatuses.Rejected,
            At = Now(),
        }, ct).ConfigureAwait(false);
        if (!rejected.IsSuccess)
            return StepFailed<SocialSendOutcome>("reject an unsendable draft", rejected.ErrorCode, rejected.Error, draft.Id);

        var why = violations.Count == 0 ? "it has no text" : $"it breaks: {string.Join(", ", violations)}";
        _logger.LogError("Approved draft {DraftId} was not sent and is now rejected: {Why}.", draft.Id, why);
        _status.RecordFailure("send a draft", SocialFailureCodes.InvalidRequest, Now());
        await _realtime.DraftChangedAsync(draft.Id, draft.AccountId, ReplyDraftStatuses.Rejected, ct).ConfigureAwait(false);
        return Result<SocialSendOutcome>.Success(new SocialSendOutcome
        {
            Code = SocialFailureCodes.InvalidRequest,
            Error = $"Not sent, because {why}. The draft is rejected.",
            Draft = rejected.Value,
        });
    }

    /// <summary>When the account may send again, or null when no limit holds it now.</summary>
    private async Task<Result<DateTime?>> CheckLimitsAsync(Guid accountId, CancellationToken ct)
    {
        var audit = await _store.ListAuditAsync(accountId, SocialSendLimits.AuditLookback, ct).ConfigureAwait(false);
        if (!audit.IsSuccess)
            return Result<DateTime?>.Failure(audit.Error!, audit.ErrorCode ?? SocialFailureCodes.StoreUnavailable);

        var now = Now();
        var sends = audit.Value!.Where(a => a.Action is ActionKinds.Reply or ActionKinds.React or ActionKinds.Publish).ToList();

        var coolDownEnds = sends
            .Where(a => a.Outcome != ActionOutcomes.Succeeded && a.FailureCode is not null && SocialSendLimits.CoolDownCodes.Contains(a.FailureCode))
            .Select(a => a.OccurredAt + SocialSendLimits.CoolDown)
            .Where(end => end > now)
            .DefaultIfEmpty()
            .Max();
        if (coolDownEnds != default)
            return Result<DateTime?>.Success(coolDownEnds);

        var lastHour = sends.Where(a => a.Action == ActionKinds.Reply && a.OccurredAt > now - TimeSpan.FromHours(1)).ToList();
        if (lastHour.Count >= SocialSendLimits.RepliesPerAccountPerHour)
            return Result<DateTime?>.Success(lastHour.Min(a => a.OccurredAt) + TimeSpan.FromHours(1));

        return Result<DateTime?>.Success(null);
    }

    private async Task<Result<SocialSendOutcome>> RecordSentAsync(ReplyDraft draft, string platformCommentId, CancellationToken ct)
    {
        var sent = await _store.TransitionDraftAsync(draft.Id, new TransitionReplyDraftCommand
        {
            From = ReplyDraftStatuses.Sending,
            To = ReplyDraftStatuses.Sent,
            SentPlatformCommentId = platformCommentId,
            At = Now(),
        }, ct).ConfigureAwait(false);

        _status.RecordSend(SocialSendKind.Sent);
        if (!sent.IsSuccess)
        {
            // The reply is live and the store would not say so. The draft stays sending, and the next
            // startup marks it outcome_unknown, so it is never sent twice.
            Fail("record a sent draft", sent.ErrorCode, sent.Error, draft.Id);
            _logger.LogCritical(
                "Draft {DraftId} was sent as comment {CommentId} and could not be recorded as sent; it stays sending and is never resent.",
                draft.Id, platformCommentId);
            return Result<SocialSendOutcome>.Success(new SocialSendOutcome
            {
                Sent = true,
                Code = sent.ErrorCode ?? SocialFailureCodes.StoreUnavailable,
                Error = "The reply is live, and the store could not record it as sent.",
                Draft = draft,
            });
        }

        _logger.LogInformation("Draft {DraftId} was sent.", draft.Id);
        await _realtime.DraftChangedAsync(draft.Id, draft.AccountId, ReplyDraftStatuses.Sent, ct).ConfigureAwait(false);
        return Result<SocialSendOutcome>.Success(new SocialSendOutcome { Sent = true, Draft = sent.Value });
    }

    private async Task<Result<SocialSendOutcome>> RecordSendFailedAsync(ReplyDraft draft, string? code, string? error, CancellationToken ct)
    {
        var failure = SocialFailureCodes.IsKnown(code) ? code! : SocialFailureCodes.PlatformRejected;
        _status.RecordSend(SocialSendKind.Failed);
        _status.RecordFailure("send a draft", failure, Now());

        var failed = await _store.TransitionDraftAsync(draft.Id, new TransitionReplyDraftCommand
        {
            From = ReplyDraftStatuses.Sending,
            To = ReplyDraftStatuses.Failed,
            FailureCode = failure,
            At = Now(),
        }, ct).ConfigureAwait(false);
        if (!failed.IsSuccess)
        {
            Fail("record a failed send", failed.ErrorCode, failed.Error, draft.Id);
            return Result<SocialSendOutcome>.Success(new SocialSendOutcome
            {
                Code = failure,
                Error = $"{error} The store could not record the failure; the draft stays sending and is never resent.",
                Draft = draft,
            });
        }

        await _realtime.DraftChangedAsync(draft.Id, draft.AccountId, ReplyDraftStatuses.Failed, ct).ConfigureAwait(false);
        return Result<SocialSendOutcome>.Success(new SocialSendOutcome { Code = failure, Error = error, Draft = failed.Value });
    }

    private Result<SocialSendOutcome> Held(ReplyDraft draft, string code, string reason, DateTime? retryAfter)
    {
        _status.RecordSend(SocialSendKind.Held);
        _logger.LogWarning("Draft {DraftId} is held and stays approved: {Code}.", draft.Id, code);
        return Result<SocialSendOutcome>.Success(new SocialSendOutcome
        {
            Held = true,
            Code = code,
            Error = reason,
            RetryAfter = retryAfter,
            Draft = draft,
        });
    }

    private Result<T> StepFailed<T>(string step, string? code, string? error, Guid draftId)
    {
        Fail(step, code, error, draftId);
        return Result<T>.Failure(error ?? $"Could not {step}.", code ?? SocialFailureCodes.StoreUnavailable);
    }

    private void Fail(string step, string? code, string? error, Guid? draftId)
    {
        _status.RecordFailure(step, code ?? "unknown", Now());
        if (draftId is { } id)
            _logger.LogError("The send step could not {Step} for draft {DraftId}: {Code}. {Error}", step, id, code, error);
        else
            _logger.LogError("The send step could not {Step}: {Code}. {Error}", step, code, error);
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
}
