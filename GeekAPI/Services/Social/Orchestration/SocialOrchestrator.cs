using GeekAPI.Services.Social.Realtime;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.Orchestration;

/// <summary>
/// The orchestrator: woken with the id of a stored event, it carries a new comment through Triage, the
/// drafter and the Reviewer, and stores the result as a draft and an outcome on the event. It is the
/// only component that knows the order of those steps. It never calls a language model and never calls
/// a platform: it reaches the agents only through their contracts and holds state only in
/// <see cref="ISocialStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// A pending draft waits for a person to approve it; nothing is sent from the reply path. Approved
/// drafts a limit held back are sent by the send step (<see cref="ISocialSendStep"/>) after each event
/// taken in and at startup. A comment the account itself wrote (the Page's own reply arriving back
/// through the webhook) is never triaged.
/// </para>
/// <para>
/// Every failure is logged where it happens and kept in the store (<see cref="ISocialStore.RecordStepFailureAsync"/>)
/// for the status page of the tenant whose comment it was, or of every tenant when it belongs to none.
/// What an agent decided, including that it could not run, is stored on the event, so an operator sees
/// it. A step the store could not complete leaves the event undecided, and recovery finds it again.
/// There is no timer (AGENTS.md forbids polling), so recovery that could not finish at startup runs
/// again at the next wake.
/// </para>
/// </remarks>
public sealed class SocialOrchestrator : BackgroundService
{
    /// <summary>How many undecided comments one recovery read takes. Recovery reads again until none is left.</summary>
    internal const int RecoveryBatch = 500;

    /// <summary>How many of an account's posts are read to find the one a comment is under (the store's own maximum).</summary>
    internal const int PostLookup = 200;

    private readonly SocialWake _wake;
    private readonly IServiceScopeFactory _scopes;
    private readonly ISocialRealtime _realtime;
    private readonly TimeProvider _clock;
    private readonly ILogger<SocialOrchestrator> _logger;
    private bool _recovered;

    /// <summary>Where one event was left.</summary>
    internal enum Handled
    {
        /// <summary>An outcome is stored on the event (by this call, or one that won a race).</summary>
        Decided,

        /// <summary>Nothing to decide: not a comment, or already decided.</summary>
        Skipped,

        /// <summary>The store did not complete a step; the event stays undecided and recovery finds it again.</summary>
        Stalled,
    }

    public SocialOrchestrator(
        SocialWake wake,
        IServiceScopeFactory scopes,
        ISocialRealtime realtime,
        TimeProvider clock,
        ILogger<SocialOrchestrator> logger)
    {
        _wake = wake;
        _scopes = scopes;
        _realtime = realtime;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SocialOrchestrator starting.");
        try
        {
            await RecoverAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // A recovery that throws must not stop the host; it is kept and tried again at the next wake.
            _logger.LogError(ex, "SocialOrchestrator could not finish its startup recovery; it will try again at the next wake.");
            await KeepUnexpectedFailureAsync("recover at startup", stoppingToken).ConfigureAwait(false);
        }

        await foreach (var eventId in _wake.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                // Recovery that failed at startup (the store was not up yet) is tried again here.
                if (!_recovered)
                    await RecoverAsync(stoppingToken).ConfigureAwait(false);

                await TakeInAsync(eventId, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SocialOrchestrator failed on event {EventId}; it stays undecided and is found again at startup.", eventId);
                await KeepUnexpectedFailureAsync("take in an event", stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Fails any send a restart interrupted, scrubs text past its retention, and carries every comment
    /// no agent has decided through the reply path, oldest first, reading again while a full batch made
    /// progress. Every step is tried even when an earlier one failed, and the pass counts as recovered
    /// only when all of them did.
    /// </summary>
    public async Task RecoverAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var allSucceeded = true;
        var interrupted = 0;
        var recovered = 0;

        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISocialStore>();

        var sends = await store.FailInterruptedSendsAsync(now, ct).ConfigureAwait(false);
        if (sends.IsSuccess)
            interrupted = sends.Value!.DraftIds.Count;
        else
            allSucceeded &= await FailAsync(store, null, "fail interrupted sends", sends.ErrorCode, sends.Error, null, ct).ConfigureAwait(false);

        allSucceeded &= await PurgeAsync(store, ct).ConfigureAwait(false);

        // An event this pass already tried is not tried again in it, so one that keeps stalling cannot
        // spin the loop or call an agent twice.
        var attempted = new HashSet<Guid>();
        while (true)
        {
            var batch = await store.ListUntriagedEventsAsync(RecoveryBatch, ct).ConfigureAwait(false);
            if (!batch.IsSuccess)
            {
                allSucceeded &= await FailAsync(store, null, "list undecided events", batch.ErrorCode, batch.Error, null, ct).ConfigureAwait(false);
                break;
            }

            var fresh = batch.Value!.Where(e => attempted.Add(e.Id)).ToList();
            var stalled = 0;
            foreach (var pending in fresh)
            {
                recovered++;
                if (await HandleSafelyAsync(scope.ServiceProvider, store, pending.TenantId, pending.Id, ct).ConfigureAwait(false) == Handled.Stalled)
                    stalled++;
            }

            if (stalled > 0)
                allSucceeded = false;

            if (batch.Value!.Count < RecoveryBatch || fresh.Count == 0 || stalled == fresh.Count)
                break;
        }

        await SendWaitingAsync(scope.ServiceProvider, ct).ConfigureAwait(false);

        _recovered = allSucceeded;
        _logger.LogInformation(
            "SocialOrchestrator recovery: {Interrupted} interrupted send(s) failed, {Recovered} undecided comment(s) handled, complete: {Complete}.",
            interrupted, recovered, allSucceeded);
    }

    /// <summary>Carries the event the Listener stored through the reply path, then purges what has outlived its retention.</summary>
    public async Task TakeInAsync(Guid eventId, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISocialStore>();

        // The wake carries only the event id: read it once, across tenants, to learn whose it is.
        var owner = await store.GetEventAcrossTenantsAsync(eventId, ct).ConfigureAwait(false);
        if (owner.IsSuccess)
            await HandleAsync(scope.ServiceProvider, store, owner.Value!.TenantId, eventId, ct).ConfigureAwait(false);
        else
            await FailAsync(store, null, "load an event", owner.ErrorCode, owner.Error, eventId, ct).ConfigureAwait(false);

        await PurgeAsync(store, ct).ConfigureAwait(false);
        await SendWaitingAsync(scope.ServiceProvider, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// An operator's "run again" on a comment that is held for clearance or failed: the reply path runs
    /// once more. A comment that was decided (a reply needed or not) is a conflict and is not run.
    /// Returns the outcome stored on the comment afterwards.
    /// </summary>
    public async Task<Result<EngagementEvent>> RerunAsync(Guid tenantId, Guid eventId, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISocialStore>();

        var before = await store.GetEventAsync(tenantId, eventId, ct).ConfigureAwait(false);
        if (!before.IsSuccess)
            return before;
        if (before.Value!.Kind != EngagementKinds.CommentCreated)
            return Result<EngagementEvent>.Failure("Only a comment is triaged.", SocialFailureCodes.InvalidRequest);
        if (before.Value.TriageOutcome is { } decided && !TriageOutcomes.Rerunnable.Contains(decided))
            return Result<EngagementEvent>.Failure($"The comment is already decided ({decided}).", SocialFailureCodes.Conflict);

        var handled = await HandleAsync(scope.ServiceProvider, store, tenantId, eventId, ct, rerun: true).ConfigureAwait(false);
        if (handled == Handled.Stalled)
            return Result<EngagementEvent>.Failure(
                "A step could not complete; the comment is unchanged. The failure is on the status page.", SocialFailureCodes.StoreUnavailable);

        return await store.GetEventAsync(tenantId, eventId, ct).ConfigureAwait(false);
    }

    private async Task SendWaitingAsync(IServiceProvider services, CancellationToken ct)
    {
        var sent = await services.GetRequiredService<ISocialSendStep>().SendWaitingAsync(ct).ConfigureAwait(false);
        if (sent > 0)
            _logger.LogInformation("SocialOrchestrator sent {Sent} approved draft(s) that were waiting.", sent);
    }

    /// <summary>One event in recovery: an exception is recorded and logged and the rest still run.</summary>
    private async Task<Handled> HandleSafelyAsync(IServiceProvider services, ISocialStore store, Guid tenantId, Guid eventId, CancellationToken ct)
    {
        try
        {
            return await HandleAsync(services, store, tenantId, eventId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SocialOrchestrator failed on event {EventId}; it stays undecided.", eventId);
            await KeepFailureAsync(store, tenantId, "handle an event", SocialStepFailures.UnexpectedError, ct).ConfigureAwait(false);
            return Handled.Stalled;
        }
    }

    /// <summary>The reply path for one event, in the plan's order (steps 1 to 6). It ends at a draft a person approves; nothing is sent from here.</summary>
    internal async Task<Handled> HandleAsync(IServiceProvider services, ISocialStore store, Guid tenantId, Guid eventId, CancellationToken ct, bool rerun = false)
    {
        // 1. Load it. Only a new comment is triaged, and a decided one is never decided again here; an
        // operator action re-runs a blocked or failed one.
        var loaded = await store.GetEventAsync(tenantId, eventId, ct).ConfigureAwait(false);
        if (!loaded.IsSuccess)
        {
            await FailAsync(store, tenantId, "load an event", loaded.ErrorCode, loaded.Error, eventId, ct).ConfigureAwait(false);
            return Handled.Stalled;
        }

        var ev = loaded.Value!;
        if (ev.Kind != EngagementKinds.CommentCreated)
        {
            _logger.LogDebug("Event {EventId} is {Kind}; it is stored and not triaged.", ev.Id, ev.Kind);
            return Handled.Skipped;
        }

        if (ev.TriageOutcome is not null && !(rerun && TriageOutcomes.Rerunnable.Contains(ev.TriageOutcome)))
        {
            _logger.LogInformation("Event {EventId} was already decided ({Outcome}); nothing to do.", ev.Id, ev.TriageOutcome);
            return Handled.Skipped;
        }

        // 2. Text gone (past its retention) or never there: nothing can be decided from it.
        if (string.IsNullOrWhiteSpace(ev.Text))
        {
            _logger.LogError("Event {EventId} has no comment text (scrubbed past its retention, or empty), so it cannot be triaged.", ev.Id);
            return await RecordAsync(store, ev.TenantId, ev.Id, TriageOutcomes.Failed, SocialFailureCodes.InvalidRequest, ct).ConfigureAwait(false);
        }

        // 3. The account and the post, for context and for the only link a reply may carry.
        var account = await store.GetAccountAsync(ev.TenantId, ev.AccountId, ct).ConfigureAwait(false);
        if (!account.IsSuccess)
        {
            await FailAsync(store, ev.TenantId, "load the account", account.ErrorCode, account.Error, ev.Id, ct).ConfigureAwait(false);
            return Handled.Stalled;
        }

        // The Page's own comments (its replies, arriving back through the webhook) are never triaged:
        // no text of ours goes to the model and the Page never answers itself.
        if (!string.IsNullOrEmpty(ev.AuthorPlatformId)
            && string.Equals(ev.AuthorPlatformId, account.Value!.PlatformAccountId, StringComparison.Ordinal))
        {
            _logger.LogInformation("Event {EventId} is the account's own comment; no reply is needed.", ev.Id);
            return await RecordAsync(store, ev.TenantId, ev.Id, TriageOutcomes.NoReplyNeeded, null, ct).ConfigureAwait(false);
        }

        SocialPost? post = null;
        if (!string.IsNullOrWhiteSpace(ev.PlatformPostId))
        {
            var posts = await store.ListPostsAsync(ev.TenantId, ev.AccountId, PostLookup, ct).ConfigureAwait(false);
            if (!posts.IsSuccess)
            {
                await FailAsync(store, ev.TenantId, "load the post", posts.ErrorCode, posts.Error, ev.Id, ct).ConfigureAwait(false);
                return Handled.Stalled;
            }

            post = posts.Value!.FirstOrDefault(p => string.Equals(p.PlatformPostId, ev.PlatformPostId, StringComparison.Ordinal));
        }

        // 3b. A tenant's comments reach the AI only after that tenant accepted the AI terms. Until then the comment is held
        // (blocked_no_clearance, which an operator re-runs once they have), and never sent to the model.
        var tenant = await store.GetTenantAsync(ev.TenantId, ct).ConfigureAwait(false);
        if (!tenant.IsSuccess)
        {
            await FailAsync(store, ev.TenantId, "load the tenant", tenant.ErrorCode, tenant.Error, ev.Id, ct).ConfigureAwait(false);
            return Handled.Stalled;
        }

        if (tenant.Value!.AiConsentAcceptedAt is null)
            return await RecordAsync(store, ev.TenantId, ev.Id, TriageOutcomes.BlockedNoClearance, null, ct).ConfigureAwait(false);

        // 4. Triage.
        var triage = await services.GetRequiredService<IEngagementTriage>().TriageAsync(new TriageRequest
        {
            Platform = ev.Platform,
            AccountDisplayName = account.Value!.DisplayName,
            CommentText = ev.Text,
            PostText = post?.Text,
            AuthorDisplayName = ev.AuthorDisplayName,
        }, ct).ConfigureAwait(false);

        if (!triage.IsSuccess)
            return await RecordAgentFailureAsync(store, ev, "triage", triage.ErrorCode, ct).ConfigureAwait(false);

        var decision = triage.Value!;
        if (!decision.NeedsReply)
            return await RecordAsync(store, ev.TenantId, ev.Id, TriageOutcomes.NoReplyNeeded, null, ct).ConfigureAwait(false);

        // 5. A person writes it, or the drafter does and the Reviewer checks it.
        CreateReplyDraftCommand draft;
        if (decision.NeedsHumanReason is not null)
        {
            draft = new CreateReplyDraftCommand
            {
                EngagementEventId = ev.Id,
                Category = decision.Category,
                DraftText = string.Empty,
                NeedsHumanReason = decision.NeedsHumanReason,
            };
        }
        else
        {
            var approvedLinks = string.IsNullOrWhiteSpace(post?.LinkUrl) ? new List<string>() : new List<string> { post!.LinkUrl! };
            var written = await services.GetRequiredService<IReplyDrafter>().DraftAsync(new ReplyDraftRequest
            {
                Platform = ev.Platform,
                AccountDisplayName = account.Value!.DisplayName,
                Category = decision.Category,
                CommentText = ev.Text,
                PostText = post?.Text,
                AuthorDisplayName = ev.AuthorDisplayName,
                ApprovedLinks = approvedLinks,
            }, ct).ConfigureAwait(false);

            if (!written.IsSuccess)
                return await RecordAgentFailureAsync(store, ev, "draft a reply", written.ErrorCode, ct).ConfigureAwait(false);

            var review = services.GetRequiredService<IOutboundTextReviewer>().Review(new OutboundText
            {
                Kind = OutboundTextKinds.Reply,
                Platform = ev.Platform,
                Text = written.Value!.DraftText,
                ApprovedLinks = approvedLinks,
            });

            if (!review.IsSuccess)
                return await RecordAgentFailureAsync(store, ev, "review the draft", review.ErrorCode, ct).ConfigureAwait(false);

            var refused = !review.Value!.Passed;
            draft = new CreateReplyDraftCommand
            {
                EngagementEventId = ev.Id,
                Category = decision.Category,
                DraftText = written.Value.DraftText,
                ReviewRefusal = refused ? review.Value.Violations.ToList() : null,
            };
            if (refused)
            {
                _logger.LogWarning(
                    "The Reviewer refused the draft for event {EventId} ({Rules}); it is stored rejected for the operator to see.",
                    ev.Id, string.Join(", ", review.Value.Violations));
            }
        }

        var created = await store.CreateDraftAsync(ev.TenantId, draft, ct).ConfigureAwait(false);
        if (created.IsSuccess)
        {
            await _realtime.DraftChangedAsync(ev.TenantId, created.Value!.Id, ev.AccountId, created.Value.Status, ct).ConfigureAwait(false);
        }
        else if (created.ErrorCode == SocialFailureCodes.Conflict)
        {
            // The one-answer index: a live draft for this comment already exists (a crash after it was
            // created and before the outcome was stored), so the comment is already drafted.
            _logger.LogInformation("Event {EventId} already has a draft; recording its outcome.", ev.Id);
        }
        else
        {
            await FailAsync(store, ev.TenantId, "create a draft", created.ErrorCode, created.Error, ev.Id, ct).ConfigureAwait(false);
            return Handled.Stalled;
        }

        // 6. Only now is the comment marked answered, so a crash before this leaves it for recovery. A person
        // approves the draft before anything is sent (docs/PLATFORM-CAPABILITIES.md section 1a).
        return await RecordAsync(store, ev.TenantId, ev.Id, TriageOutcomes.ReplyNeeded, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// An agent could not complete. A platform that is not cleared is blocked_no_clearance; anything else
    /// is failed with the agent's code. The agent has already logged its reason.
    /// </summary>
    private async Task<Handled> RecordAgentFailureAsync(ISocialStore store, EngagementEvent ev, string step, string? code, CancellationToken ct)
    {
        if (code == SocialFailureCodes.ClearanceRequired)
        {
            _logger.LogWarning("Event {EventId} is held: {Platform} is not cleared to send comment text to the AI provider.", ev.Id, ev.Platform);
            return await RecordAsync(store, ev.TenantId, ev.Id, TriageOutcomes.BlockedNoClearance, null, ct).ConfigureAwait(false);
        }

        var failure = SocialFailureCodes.IsKnown(code) ? code! : SocialFailureCodes.ModelFailed;
        await FailAsync(store, ev.TenantId, step, failure, null, ev.Id, ct).ConfigureAwait(false);
        return await RecordAsync(store, ev.TenantId, ev.Id, TriageOutcomes.Failed, failure, ct).ConfigureAwait(false);
    }

    private async Task<Handled> RecordAsync(ISocialStore store, Guid tenantId, Guid eventId, string outcome, string? failureCode, CancellationToken ct)
    {
        var recorded = await store.RecordTriageOutcomeAsync(tenantId, eventId, new RecordTriageOutcomeCommand
        {
            Outcome = outcome,
            FailureCode = failureCode,
            At = _clock.GetUtcNow().UtcDateTime,
        }, ct).ConfigureAwait(false);

        if (recorded.IsSuccess)
        {
            await _realtime.CommentChangedAsync(tenantId, eventId, recorded.Value!.AccountId, outcome, ct).ConfigureAwait(false);
            return Handled.Decided;
        }

        if (recorded.ErrorCode == SocialFailureCodes.Conflict)
        {
            _logger.LogInformation("Event {EventId} was decided by another pass first; {Outcome} was not stored.", eventId, outcome);
            return Handled.Decided;
        }

        await FailAsync(store, tenantId, "record the triage outcome", recorded.ErrorCode, recorded.Error, eventId, ct).ConfigureAwait(false);
        return Handled.Stalled;
    }

    private async Task<bool> PurgeAsync(ISocialStore store, CancellationToken ct)
    {
        var purged = await store.PurgeExpiredAsync(_clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        if (!purged.IsSuccess)
            return await FailAsync(store, null, "purge expired text", purged.ErrorCode, purged.Error, null, ct).ConfigureAwait(false);

        var result = purged.Value!;
        if (result.EventsScrubbed > 0 || result.FailuresDeleted > 0)
        {
            _logger.LogInformation(
                "SocialOrchestrator purge: {Scrubbed} event(s) scrubbed past their retention, {Deleted} kept failure(s) deleted.",
                result.EventsScrubbed, result.FailuresDeleted);
        }

        return true;
    }

    /// <summary>
    /// Logs a step that could not complete and keeps it for the status page of <paramref name="tenantId"/>, or of every
    /// tenant when it belongs to none. Always false, so a caller can fold it into "did every step succeed".
    /// </summary>
    private async Task<bool> FailAsync(ISocialStore store, Guid? tenantId, string step, string? code, string? error, Guid? eventId, CancellationToken ct)
    {
        if (eventId is { } id)
            _logger.LogError("SocialOrchestrator could not {Step} for event {EventId}: {Code}. {Error}", step, id, code, error);
        else
            _logger.LogError("SocialOrchestrator could not {Step}: {Code}. {Error}", step, code, error);

        await KeepFailureAsync(store, tenantId, step, code ?? SocialStepFailures.Unknown, ct).ConfigureAwait(false);
        return false;
    }

    /// <summary>
    /// Keeps a failure for the status page. A store that cannot keep it is logged as an error of its own: the failure
    /// itself was already logged, and nothing pretends it was kept.
    /// </summary>
    private async Task KeepFailureAsync(ISocialStore store, Guid? tenantId, string step, string code, CancellationToken ct)
    {
        var kept = await store.RecordStepFailureAsync(new RecordSocialStepFailureCommand
        {
            TenantId = tenantId,
            Step = step,
            Code = code,
            At = _clock.GetUtcNow().UtcDateTime,
        }, ct).ConfigureAwait(false);

        if (!kept.IsSuccess)
        {
            _logger.LogError(
                "SocialOrchestrator could not keep the failure of {Step} ({Code}) for the status page: {KeepCode}. {Error}",
                step, code, kept.ErrorCode, kept.Error);
        }
    }

    /// <summary>
    /// Keeps a step that threw in the wake loop, which belongs to no one tenant. It runs inside a catch, so anything it
    /// throws other than shutdown is logged rather than allowed to end the loop.
    /// </summary>
    private async Task KeepUnexpectedFailureAsync(string step, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ISocialStore>();
            await KeepFailureAsync(store, null, step, SocialStepFailures.UnexpectedError, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SocialOrchestrator could not keep the failure of {Step} for the status page.", step);
        }
    }
}
