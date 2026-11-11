using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;

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
/// Phase 4a ends with a draft in the store and nothing sent: a pending draft waits for a person. The
/// send step, auto-send and the Sender are Phase 4b.
/// </para>
/// <para>
/// Every failure is logged where it happens and recorded on <see cref="SocialOrchestratorStatus"/>.
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
    private readonly SocialOrchestratorStatus _status;
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
        SocialOrchestratorStatus status,
        TimeProvider clock,
        ILogger<SocialOrchestrator> logger)
    {
        _wake = wake;
        _scopes = scopes;
        _status = status;
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
            // A recovery that throws must not stop the host; it is recorded and tried again at the next wake.
            _status.RecordFailure("recover at startup", "unexpected_error", _clock.GetUtcNow().UtcDateTime);
            _logger.LogError(ex, "SocialOrchestrator could not finish its startup recovery; it will try again at the next wake.");
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
                _status.RecordFailure("take in an event", "unexpected_error", _clock.GetUtcNow().UtcDateTime);
                _logger.LogError(ex, "SocialOrchestrator failed on event {EventId}; it stays undecided and is found again at startup.", eventId);
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
            allSucceeded &= Fail("fail interrupted sends", sends.ErrorCode, sends.Error, null);

        allSucceeded &= await PurgeAsync(store, ct).ConfigureAwait(false);

        // An event this pass already tried is not tried again in it, so one that keeps stalling cannot
        // spin the loop or call an agent twice.
        var attempted = new HashSet<Guid>();
        while (true)
        {
            var batch = await store.ListEventsAsync(TriageOutcomes.Untriaged, null, RecoveryBatch, ct).ConfigureAwait(false);
            if (!batch.IsSuccess)
            {
                allSucceeded &= Fail("list undecided events", batch.ErrorCode, batch.Error, null);
                break;
            }

            var fresh = batch.Value!.Where(e => attempted.Add(e.Id)).ToList();
            var stalled = 0;
            foreach (var pending in fresh)
            {
                recovered++;
                if (await HandleSafelyAsync(scope.ServiceProvider, store, pending.Id, ct).ConfigureAwait(false) == Handled.Stalled)
                    stalled++;
            }

            if (stalled > 0)
                allSucceeded = false;

            if (batch.Value!.Count < RecoveryBatch || fresh.Count == 0 || stalled == fresh.Count)
                break;
        }

        _recovered = allSucceeded;
        _status.RecordRecovered(now, recovered, interrupted);
        _logger.LogInformation(
            "SocialOrchestrator recovery: {Interrupted} interrupted send(s) failed, {Recovered} undecided comment(s) handled, complete: {Complete}.",
            interrupted, recovered, allSucceeded);
    }

    /// <summary>Carries the event the Listener stored through the reply path, then purges what has outlived its retention.</summary>
    public async Task TakeInAsync(Guid eventId, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISocialStore>();

        await HandleAsync(scope.ServiceProvider, store, eventId, ct).ConfigureAwait(false);
        await PurgeAsync(store, ct).ConfigureAwait(false);
    }

    /// <summary>One event in recovery: an exception is recorded and logged and the rest still run.</summary>
    private async Task<Handled> HandleSafelyAsync(IServiceProvider services, ISocialStore store, Guid eventId, CancellationToken ct)
    {
        try
        {
            return await HandleAsync(services, store, eventId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _status.RecordFailure("handle an event", "unexpected_error", _clock.GetUtcNow().UtcDateTime);
            _logger.LogError(ex, "SocialOrchestrator failed on event {EventId}; it stays undecided.", eventId);
            return Handled.Stalled;
        }
    }

    /// <summary>The reply path for one event, in the plan's order (steps 1 to 6).</summary>
    internal async Task<Handled> HandleAsync(IServiceProvider services, ISocialStore store, Guid eventId, CancellationToken ct)
    {
        // 1. Load it. Only a new comment is triaged, and a decided one is never decided again here; an
        // operator action re-runs a blocked or failed one.
        var loaded = await store.GetEventAsync(eventId, ct).ConfigureAwait(false);
        if (!loaded.IsSuccess)
        {
            Fail("load an event", loaded.ErrorCode, loaded.Error, eventId);
            return Handled.Stalled;
        }

        _status.RecordEventTakenIn();
        var ev = loaded.Value!;
        if (ev.Kind != EngagementKinds.CommentCreated)
        {
            _logger.LogDebug("Event {EventId} is {Kind}; it is stored and not triaged.", ev.Id, ev.Kind);
            return Handled.Skipped;
        }

        if (ev.TriageOutcome is not null)
        {
            _logger.LogInformation("Event {EventId} was already decided ({Outcome}); nothing to do.", ev.Id, ev.TriageOutcome);
            return Handled.Skipped;
        }

        // 2. Text gone (past its retention) or never there: nothing can be decided from it.
        if (string.IsNullOrWhiteSpace(ev.Text))
        {
            _logger.LogError("Event {EventId} has no comment text (scrubbed past its retention, or empty), so it cannot be triaged.", ev.Id);
            return await RecordAsync(store, ev.Id, TriageOutcomes.Failed, SocialFailureCodes.InvalidRequest, ct).ConfigureAwait(false);
        }

        // 3. The account and the post, for context and for the only link a reply may carry.
        var account = await store.GetAccountAsync(ev.AccountId, ct).ConfigureAwait(false);
        if (!account.IsSuccess)
        {
            Fail("load the account", account.ErrorCode, account.Error, ev.Id);
            return Handled.Stalled;
        }

        SocialPost? post = null;
        if (!string.IsNullOrWhiteSpace(ev.PlatformPostId))
        {
            var posts = await store.ListPostsAsync(ev.AccountId, PostLookup, ct).ConfigureAwait(false);
            if (!posts.IsSuccess)
            {
                Fail("load the post", posts.ErrorCode, posts.Error, ev.Id);
                return Handled.Stalled;
            }

            post = posts.Value!.FirstOrDefault(p => string.Equals(p.PlatformPostId, ev.PlatformPostId, StringComparison.Ordinal));
        }

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
            return await RecordAsync(store, ev.Id, TriageOutcomes.NoReplyNeeded, null, ct).ConfigureAwait(false);

        // 5. A person writes it, or the drafter does and the Reviewer checks it.
        CreateReplyDraftCommand draft;
        SocialDraftKind kind;
        if (decision.NeedsHumanReason is not null)
        {
            draft = new CreateReplyDraftCommand
            {
                EngagementEventId = ev.Id,
                Category = decision.Category,
                DraftText = string.Empty,
                NeedsHumanReason = decision.NeedsHumanReason,
            };
            kind = SocialDraftKind.ForAPerson;
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
            kind = refused ? SocialDraftKind.Refused : SocialDraftKind.Pending;
            if (refused)
            {
                _logger.LogWarning(
                    "The Reviewer refused the draft for event {EventId} ({Rules}); it is stored rejected for the operator to see.",
                    ev.Id, string.Join(", ", review.Value.Violations));
            }
        }

        var created = await store.CreateDraftAsync(draft, ct).ConfigureAwait(false);
        if (created.IsSuccess)
        {
            _status.RecordDraft(kind);
        }
        else if (created.ErrorCode == SocialFailureCodes.Conflict)
        {
            // The one-answer index: a live draft for this comment already exists (a crash after it was
            // created and before the outcome was stored), so the comment is already drafted.
            _logger.LogInformation("Event {EventId} already has a draft; recording its outcome.", ev.Id);
        }
        else
        {
            Fail("create a draft", created.ErrorCode, created.Error, ev.Id);
            return Handled.Stalled;
        }

        // 6. Only now is the comment marked answered, so a crash before this leaves it for recovery.
        return await RecordAsync(store, ev.Id, TriageOutcomes.ReplyNeeded, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// An agent could not complete. A platform that is not cleared is blocked_no_clearance; anything else
    /// is failed with the agent's code. The agent has already logged its reason.
    /// </summary>
    private Task<Handled> RecordAgentFailureAsync(ISocialStore store, EngagementEvent ev, string step, string? code, CancellationToken ct)
    {
        if (code == SocialFailureCodes.ClearanceRequired)
        {
            _logger.LogWarning("Event {EventId} is held: {Platform} is not cleared to send comment text to Claude.", ev.Id, ev.Platform);
            return RecordAsync(store, ev.Id, TriageOutcomes.BlockedNoClearance, null, ct);
        }

        var failure = SocialFailureCodes.IsKnown(code) ? code! : SocialFailureCodes.ModelFailed;
        Fail(step, failure, null, ev.Id);
        return RecordAsync(store, ev.Id, TriageOutcomes.Failed, failure, ct);
    }

    private async Task<Handled> RecordAsync(ISocialStore store, Guid eventId, string outcome, string? failureCode, CancellationToken ct)
    {
        var recorded = await store.RecordTriageOutcomeAsync(eventId, new RecordTriageOutcomeCommand
        {
            Outcome = outcome,
            FailureCode = failureCode,
            At = _clock.GetUtcNow().UtcDateTime,
        }, ct).ConfigureAwait(false);

        if (recorded.IsSuccess)
        {
            _status.RecordOutcome(outcome);
            return Handled.Decided;
        }

        if (recorded.ErrorCode == SocialFailureCodes.Conflict)
        {
            _logger.LogInformation("Event {EventId} was decided by another pass first; {Outcome} was not stored.", eventId, outcome);
            return Handled.Decided;
        }

        Fail("record the triage outcome", recorded.ErrorCode, recorded.Error, eventId);
        return Handled.Stalled;
    }

    private async Task<bool> PurgeAsync(ISocialStore store, CancellationToken ct)
    {
        var purged = await store.PurgeExpiredAsync(_clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        if (!purged.IsSuccess)
            return Fail("purge expired text", purged.ErrorCode, purged.Error, null);

        _status.RecordScrubbed(purged.Value!.EventsScrubbed);
        return true;
    }

    private bool Fail(string step, string? code, string? error, Guid? eventId)
    {
        _status.RecordFailure(step, code ?? "unknown", _clock.GetUtcNow().UtcDateTime);
        if (eventId is { } id)
            _logger.LogError("SocialOrchestrator could not {Step} for event {EventId}: {Code}. {Error}", step, id, code, error);
        else
            _logger.LogError("SocialOrchestrator could not {Step}: {Code}. {Error}", step, code, error);
        return false;
    }
}
