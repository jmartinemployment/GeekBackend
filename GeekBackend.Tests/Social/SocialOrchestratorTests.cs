using GeekAPI.Services.Social.Orchestration;
using GeekAPI.Services.Social.Review;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The orchestrator carries a new comment through Triage, the drafter and the Reviewer and stores the
/// result, in the plan's order, and recovers what a restart left. It reaches the store and the agents'
/// contracts and nothing else. Each test scripts the store and the agents, so a call the orchestrator
/// makes that the test did not expect fails the test; every failure must be logged and recorded.
/// </summary>
public class SocialOrchestratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid AccountId = Guid.NewGuid();
    private const string PostId = "post_1";
    private const string PostLink = "https://geekatyourspot.com/bookkeeping";
    private const string CommentText = "PRIVATE-COMMENT do you work weekends?";

    private sealed class FakeTriage(Func<TriageRequest, Result<TriageResult>> decide) : IEngagementTriage
    {
        public List<TriageRequest> Requests { get; } = new();

        public Task<Result<TriageResult>> TriageAsync(TriageRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(decide(request));
        }
    }

    private sealed class FakeDrafter(Func<ReplyDraftRequest, Result<ReplyDraftSuggestion>> write) : IReplyDrafter
    {
        public List<ReplyDraftRequest> Requests { get; } = new();

        public Task<Result<ReplyDraftSuggestion>> DraftAsync(ReplyDraftRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(write(request));
        }
    }

    private sealed class FailingReviewer : IOutboundTextReviewer
    {
        public Result<OutboundTextReview> Review(OutboundText text) =>
            Result<OutboundTextReview>.Failure("A pattern timed out.", SocialFailureCodes.InvalidRequest);
    }

    private sealed class Rig
    {
        public required SocialOrchestrator Orchestrator { get; init; }
        public required SocialWake Wake { get; init; }
        public required SocialOrchestratorStatus Status { get; init; }
        public required FakeSocialStore Store { get; init; }
        public required FakeTriage Triage { get; init; }
        public required FakeDrafter Drafter { get; init; }
        public required RecordingLog<SocialOrchestrator> Log { get; init; }
        public required FakeRealtime Realtime { get; init; }
        public required FakeSendStep SendStep { get; init; }
        public List<(Guid EventId, RecordTriageOutcomeCommand Command)> Outcomes { get; } = new();
        public List<CreateReplyDraftCommand> Drafts { get; } = new();
    }

    private static Result<TriageResult> Decide(bool needsReply, string category = ReplyCategories.Question, string? reason = null) =>
        Result<TriageResult>.Success(new TriageResult { NeedsReply = needsReply, Category = category, NeedsHumanReason = reason });

    private static Result<ReplyDraftSuggestion> Written(string text) =>
        Result<ReplyDraftSuggestion>.Success(new ReplyDraftSuggestion { DraftText = text });

    private static EngagementEvent Comment(Guid id, string? text = CommentText, string? postId = PostId) => new()
    {
        Id = id,
        AccountId = AccountId,
        Platform = SocialPlatforms.Facebook,
        Kind = EngagementKinds.CommentCreated,
        Text = text,
        PlatformPostId = postId,
        AuthorDisplayName = "PRIVATE-AUTHOR",
    };

    private static Rig Build(
        Action<FakeSocialStore>? script = null,
        Func<TriageRequest, Result<TriageResult>>? triage = null,
        Func<ReplyDraftRequest, Result<ReplyDraftSuggestion>>? drafter = null,
        IOutboundTextReviewer? reviewer = null)
    {
        var store = new FakeSocialStore
        {
            OnFailInterruptedSends = _ => Result<SocialInterruptedSends>.Success(new SocialInterruptedSends()),
            OnPurge = _ => Result<SocialPurgeResult>.Success(new SocialPurgeResult()),
            OnListEvents = (_, _, _) => Result<IReadOnlyList<EngagementEvent>>.Success(Array.Empty<EngagementEvent>()),
            OnGetEvent = id => Result<EngagementEvent>.Success(Comment(id)),
            OnGetAccount = id => Result<SocialAccount>.Success(new SocialAccount { Id = id, Platform = SocialPlatforms.Facebook, DisplayName = "Geek @ Your Spot" }),
            OnListPosts = (_, _) => Result<IReadOnlyList<SocialPost>>.Success(new[]
            {
                new SocialPost { AccountId = AccountId, PlatformPostId = "post_other", Text = "Another post", LinkUrl = "https://example.com/other" },
                new SocialPost { AccountId = AccountId, PlatformPostId = PostId, Text = "PRIVATE-POST about bookkeeping", LinkUrl = PostLink },
            }),
        };

        var fakeTriage = new FakeTriage(triage ?? (_ => Decide(needsReply: false, ReplyCategories.Praise)));
        var fakeDrafter = new FakeDrafter(drafter ?? (_ => throw new InvalidOperationException("The drafter must not be called in this test.")));
        var log = new RecordingLog<SocialOrchestrator>();
        var wake = new SocialWake();
        var status = new SocialOrchestratorStatus();
        var realtime = new FakeRealtime();
        var sendStep = new FakeSendStep();

        var services = new ServiceCollection()
            .AddScoped<ISocialStore>(_ => store)
            .AddScoped<IEngagementTriage>(_ => fakeTriage)
            .AddScoped<IReplyDrafter>(_ => fakeDrafter)
            .AddScoped<GeekAPI.Services.Social.Orchestration.ISocialSendStep>(_ => sendStep)
            .AddSingleton(reviewer ?? new OutboundTextReviewer())
            .BuildServiceProvider();

        var rig = new Rig
        {
            Orchestrator = new SocialOrchestrator(
                wake, services.GetRequiredService<IServiceScopeFactory>(), status, realtime, new MetaEngagementReaderTests.FixedClock(Now), log),
            Realtime = realtime,
            SendStep = sendStep,
            Wake = wake,
            Status = status,
            Store = store,
            Triage = fakeTriage,
            Drafter = fakeDrafter,
            Log = log,
        };

        store.OnRecordTriageOutcome = (id, command) =>
        {
            rig.Outcomes.Add((id, command));
            return Result<EngagementEvent>.Success(Comment(id));
        };
        store.OnCreateDraft = command =>
        {
            rig.Drafts.Add(command);
            return Result<ReplyDraft>.Success(new ReplyDraft { Id = Guid.NewGuid(), EngagementEventId = command.EngagementEventId });
        };

        script?.Invoke(store);
        return rig;
    }

    private static void AssertNothingPrivateLogged(Rig rig)
    {
        foreach (var message in rig.Log.Messages)
            Assert.DoesNotContain("PRIVATE", message, StringComparison.Ordinal);
    }

    // ----- The reply path -----

    [Fact]
    public async Task A_comment_that_needs_no_reply_is_recorded_and_nothing_is_drafted()
    {
        var id = Guid.NewGuid();
        var rig = Build(triage: _ => Decide(needsReply: false, ReplyCategories.Spam));

        await rig.Orchestrator.TakeInAsync(id, CancellationToken.None);

        var (eventId, command) = Assert.Single(rig.Outcomes);
        Assert.Equal(id, eventId);
        Assert.Equal(TriageOutcomes.NoReplyNeeded, command.Outcome);
        Assert.Null(command.FailureCode);
        Assert.Equal(Now.UtcDateTime, command.At);
        Assert.Empty(rig.Drafts);
        Assert.Empty(rig.Drafter.Requests);
        Assert.Equal(1, rig.Status.Snapshot().CommentsNoReplyNeeded);
        Assert.Empty(rig.Status.Snapshot().Failures);
    }

    [Fact]
    public async Task Triage_is_given_the_comment_the_account_and_the_post_it_is_under()
    {
        var rig = Build();

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        var request = Assert.Single(rig.Triage.Requests);
        Assert.Equal(SocialPlatforms.Facebook, request.Platform);
        Assert.Equal("Geek @ Your Spot", request.AccountDisplayName);
        Assert.Equal(CommentText, request.CommentText);
        Assert.Equal("PRIVATE-POST about bookkeeping", request.PostText);
        Assert.Equal("PRIVATE-AUTHOR", request.AuthorDisplayName);
    }

    [Fact]
    public async Task A_comment_under_a_post_the_store_does_not_hold_is_triaged_without_post_text()
    {
        var rig = Build(script: store => store.OnGetEvent = id => Result<EngagementEvent>.Success(Comment(id, postId: "post_unknown")));

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(Assert.Single(rig.Triage.Requests).PostText);
    }

    [Fact]
    public async Task A_platform_that_is_not_cleared_is_recorded_blocked_and_logged_as_a_warning()
    {
        var id = Guid.NewGuid();
        var rig = Build(triage: _ => Result<TriageResult>.Failure("not cleared", SocialFailureCodes.ClearanceRequired));

        await rig.Orchestrator.TakeInAsync(id, CancellationToken.None);

        var command = Assert.Single(rig.Outcomes).Command;
        Assert.Equal(TriageOutcomes.BlockedNoClearance, command.Outcome);
        Assert.Null(command.FailureCode);
        Assert.Empty(rig.Drafts);
        Assert.Equal(1, rig.Status.Snapshot().CommentsBlockedNoClearance);
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(id.ToString(), StringComparison.Ordinal));
        AssertNothingPrivateLogged(rig);
    }

    [Theory]
    [InlineData(SocialFailureCodes.ModelFailed)]
    [InlineData(SocialFailureCodes.NotConfigured)]
    public async Task A_triage_that_failed_is_recorded_failed_with_its_code_and_logged(string code)
    {
        var id = Guid.NewGuid();
        var rig = Build(triage: _ => Result<TriageResult>.Failure("failed", code));

        await rig.Orchestrator.TakeInAsync(id, CancellationToken.None);

        var command = Assert.Single(rig.Outcomes).Command;
        Assert.Equal(TriageOutcomes.Failed, command.Outcome);
        Assert.Equal(code, command.FailureCode);
        Assert.Empty(rig.Drafts);
        Assert.Equal(1, rig.Status.Snapshot().CommentsFailed);
        var error = Assert.Single(rig.Log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(id.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Contains(code, error.Message, StringComparison.Ordinal);
        Assert.Contains(rig.Status.Snapshot().Failures, f => f.Step == "triage" && f.Code == code);
    }

    [Fact]
    public async Task A_comment_a_person_must_answer_gets_a_draft_with_the_reason_and_no_text_before_its_outcome()
    {
        var id = Guid.NewGuid();
        var rig = Build(triage: _ => Decide(needsReply: true, ReplyCategories.Sensitive, "It names a health condition."));

        await rig.Orchestrator.TakeInAsync(id, CancellationToken.None);

        var draft = Assert.Single(rig.Drafts);
        Assert.Equal(id, draft.EngagementEventId);
        Assert.Equal(ReplyCategories.Sensitive, draft.Category);
        Assert.Equal(string.Empty, draft.DraftText);
        Assert.Equal("It names a health condition.", draft.NeedsHumanReason);
        Assert.Null(draft.ReviewRefusal);
        Assert.Empty(rig.Drafter.Requests);
        Assert.Equal(TriageOutcomes.ReplyNeeded, Assert.Single(rig.Outcomes).Command.Outcome);
        Assert.Equal(new[] { "draft", "outcome" }, rig.Store.Writes);
        Assert.Equal(1, rig.Status.Snapshot().DraftsForAPerson);
    }

    [Fact]
    public async Task A_drafted_reply_that_passes_review_is_stored_pending_and_may_carry_the_posts_own_link()
    {
        var id = Guid.NewGuid();
        var text = $"Yes, we work Saturdays. The details are at {PostLink}.";
        var rig = Build(triage: _ => Decide(needsReply: true, ReplyCategories.Question), drafter: _ => Written(text));

        await rig.Orchestrator.TakeInAsync(id, CancellationToken.None);

        var asked = Assert.Single(rig.Drafter.Requests);
        Assert.Equal(ReplyCategories.Question, asked.Category);
        Assert.Equal(CommentText, asked.CommentText);
        Assert.Equal(new[] { PostLink }, asked.ApprovedLinks);

        var draft = Assert.Single(rig.Drafts);
        Assert.Equal(text, draft.DraftText);
        Assert.Null(draft.ReviewRefusal);
        Assert.Null(draft.NeedsHumanReason);
        Assert.Equal(TriageOutcomes.ReplyNeeded, Assert.Single(rig.Outcomes).Command.Outcome);
        Assert.Equal(new[] { "draft", "outcome" }, rig.Store.Writes);
        Assert.Equal(1, rig.Status.Snapshot().DraftsPending);
    }

    [Fact]
    public async Task A_drafted_reply_the_reviewer_refuses_is_stored_with_the_rules_it_broke_and_logged()
    {
        var id = Guid.NewGuid();
        var rig = Build(
            triage: _ => Decide(needsReply: true, ReplyCategories.Question),
            drafter: _ => Written("Yes — see https://example.com/other for SMB pricing."));

        await rig.Orchestrator.TakeInAsync(id, CancellationToken.None);

        var draft = Assert.Single(rig.Drafts);
        Assert.NotNull(draft.ReviewRefusal);
        Assert.Contains(ReviewRules.EmDash, draft.ReviewRefusal!);
        Assert.Contains(ReviewRules.Smb, draft.ReviewRefusal!);
        Assert.Contains(ReviewRules.UnapprovedLink, draft.ReviewRefusal!);
        Assert.Equal(TriageOutcomes.ReplyNeeded, Assert.Single(rig.Outcomes).Command.Outcome);
        Assert.Equal(1, rig.Status.Snapshot().DraftsRefused);
        var warning = Assert.Single(rig.Log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(ReviewRules.EmDash, warning.Message, StringComparison.Ordinal);
        Assert.Contains(id.ToString(), warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_link_in_a_reply_to_a_post_the_store_does_not_hold_is_refused()
    {
        var rig = Build(
            script: store => store.OnGetEvent = id => Result<EngagementEvent>.Success(Comment(id, postId: null)),
            triage: _ => Decide(needsReply: true, ReplyCategories.Question),
            drafter: _ => Written($"See {PostLink}."));

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(Assert.Single(rig.Drafter.Requests).ApprovedLinks);
        Assert.Equal(new[] { ReviewRules.UnapprovedLink }, Assert.Single(rig.Drafts).ReviewRefusal);
    }

    [Fact]
    public async Task A_drafter_that_failed_is_recorded_failed_and_no_draft_is_stored()
    {
        var id = Guid.NewGuid();
        var rig = Build(
            triage: _ => Decide(needsReply: true, ReplyCategories.Question),
            drafter: _ => Result<ReplyDraftSuggestion>.Failure("failed", SocialFailureCodes.ModelFailed));

        await rig.Orchestrator.TakeInAsync(id, CancellationToken.None);

        Assert.Empty(rig.Drafts);
        var command = Assert.Single(rig.Outcomes).Command;
        Assert.Equal((TriageOutcomes.Failed, SocialFailureCodes.ModelFailed), (command.Outcome, command.FailureCode));
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_drafter_held_for_clearance_is_recorded_blocked()
    {
        var rig = Build(
            triage: _ => Decide(needsReply: true, ReplyCategories.Question),
            drafter: _ => Result<ReplyDraftSuggestion>.Failure("not cleared", SocialFailureCodes.ClearanceRequired));

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(rig.Drafts);
        Assert.Equal(TriageOutcomes.BlockedNoClearance, Assert.Single(rig.Outcomes).Command.Outcome);
    }

    [Fact]
    public async Task A_reviewer_that_could_not_run_is_recorded_failed_and_the_draft_is_not_stored()
    {
        var rig = Build(
            triage: _ => Decide(needsReply: true, ReplyCategories.Question),
            drafter: _ => Written("Yes, we do."),
            reviewer: new FailingReviewer());

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(rig.Drafts);
        var command = Assert.Single(rig.Outcomes).Command;
        Assert.Equal((TriageOutcomes.Failed, SocialFailureCodes.InvalidRequest), (command.Outcome, command.FailureCode));
        Assert.Contains(rig.Status.Snapshot().Failures, f => f.Step == "review the draft");
    }

    [Fact]
    public async Task A_draft_that_already_exists_is_read_as_already_drafted_and_the_outcome_is_recorded()
    {
        var rig = Build(
            triage: _ => Decide(needsReply: true, ReplyCategories.Question),
            drafter: _ => Written("Yes, we do."));
        rig.Store.OnCreateDraft = _ => Result<ReplyDraft>.Failure("A reply already exists.", SocialFailureCodes.Conflict);

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(TriageOutcomes.ReplyNeeded, Assert.Single(rig.Outcomes).Command.Outcome);
        Assert.Empty(rig.Status.Snapshot().Failures);
        Assert.DoesNotContain(rig.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_draft_the_store_could_not_create_leaves_the_comment_undecided_and_is_logged()
    {
        var id = Guid.NewGuid();
        var rig = Build(
            triage: _ => Decide(needsReply: true, ReplyCategories.Question),
            drafter: _ => Written("Yes, we do."));
        rig.Store.OnCreateDraft = _ => Result<ReplyDraft>.Failure("down", SocialFailureCodes.StoreUnavailable);

        await rig.Orchestrator.TakeInAsync(id, CancellationToken.None);

        Assert.Empty(rig.Outcomes);
        var failure = Assert.Single(rig.Status.Snapshot().Failures);
        Assert.Equal(("create a draft", SocialFailureCodes.StoreUnavailable), (failure.Step, failure.Code));
        var error = Assert.Single(rig.Log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(id.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_outcome_another_pass_recorded_first_is_not_a_failure()
    {
        var rig = Build();
        rig.Store.OnRecordTriageOutcome = (_, _) => Result<EngagementEvent>.Failure("Already decided.", SocialFailureCodes.Conflict);

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(rig.Status.Snapshot().Failures);
        Assert.DoesNotContain(rig.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task An_outcome_the_store_could_not_record_is_recorded_for_the_operator_and_logged()
    {
        var rig = Build();
        rig.Store.OnRecordTriageOutcome = (_, _) => Result<EngagementEvent>.Failure("down", SocialFailureCodes.StoreUnavailable);

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        var failure = Assert.Single(rig.Status.Snapshot().Failures);
        Assert.Equal(("record the triage outcome", SocialFailureCodes.StoreUnavailable), (failure.Step, failure.Code));
        Assert.Single(rig.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_crash_between_the_draft_and_the_outcome_is_finished_by_recovery()
    {
        var id = Guid.NewGuid();
        var drafted = false;
        var rig = Build(
            triage: _ => Decide(needsReply: true, ReplyCategories.Question),
            drafter: _ => Written("Yes, we do."));
        rig.Store.OnCreateDraft = _ =>
        {
            if (drafted)
                return Result<ReplyDraft>.Failure("A reply already exists.", SocialFailureCodes.Conflict);
            drafted = true;
            return Result<ReplyDraft>.Success(new ReplyDraft { Id = Guid.NewGuid(), EngagementEventId = id });
        };
        var recordings = 0;
        rig.Store.OnRecordTriageOutcome = (eventId, command) =>
        {
            if (++recordings == 1)
                return Result<EngagementEvent>.Failure("down", SocialFailureCodes.StoreUnavailable);
            rig.Outcomes.Add((eventId, command));
            return Result<EngagementEvent>.Success(Comment(eventId));
        };

        await rig.Orchestrator.TakeInAsync(id, CancellationToken.None);
        Assert.Empty(rig.Outcomes);

        rig.Store.OnListEvents = (_, _, _) => Result<IReadOnlyList<EngagementEvent>>.Success(new[] { Comment(id) });
        await rig.Orchestrator.RecoverAsync(CancellationToken.None);

        Assert.Equal(TriageOutcomes.ReplyNeeded, Assert.Single(rig.Outcomes).Command.Outcome);
    }

    [Fact]
    public async Task An_event_that_is_not_a_new_comment_is_not_triaged()
    {
        var rig = Build(script: store => store.OnGetEvent = id =>
            Result<EngagementEvent>.Success(new EngagementEvent { Id = id, Kind = EngagementKinds.ReactionAdded }));

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(rig.Triage.Requests);
        Assert.Empty(rig.Store.Writes);
        Assert.Equal(1, rig.Status.Snapshot().EventsTakenIn);
    }

    [Theory]
    [InlineData(TriageOutcomes.ReplyNeeded)]
    [InlineData(TriageOutcomes.NoReplyNeeded)]
    [InlineData(TriageOutcomes.BlockedNoClearance)]
    [InlineData(TriageOutcomes.Failed)]
    public async Task A_comment_that_was_already_decided_is_never_decided_again_by_a_wake(string outcome)
    {
        var rig = Build(script: store => store.OnGetEvent = id =>
        {
            var ev = Comment(id);
            ev.TriageOutcome = outcome;
            return Result<EngagementEvent>.Success(ev);
        });

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(rig.Triage.Requests);
        Assert.Empty(rig.Store.Writes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task A_comment_whose_text_is_gone_is_recorded_failed_without_calling_triage(string? text)
    {
        var id = Guid.NewGuid();
        var rig = Build(script: store => store.OnGetEvent = eventId => Result<EngagementEvent>.Success(Comment(eventId, text)));

        await rig.Orchestrator.TakeInAsync(id, CancellationToken.None);

        Assert.Empty(rig.Triage.Requests);
        var command = Assert.Single(rig.Outcomes).Command;
        Assert.Equal((TriageOutcomes.Failed, SocialFailureCodes.InvalidRequest), (command.Outcome, command.FailureCode));
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_account_the_store_could_not_load_leaves_the_comment_undecided()
    {
        var rig = Build(script: store => store.OnGetAccount = _ => Result<SocialAccount>.Failure("down", SocialFailureCodes.StoreUnavailable));

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(rig.Triage.Requests);
        Assert.Empty(rig.Store.Writes);
        Assert.Equal("load the account", Assert.Single(rig.Status.Snapshot().Failures).Step);
    }

    [Fact]
    public async Task Posts_the_store_could_not_list_leave_the_comment_undecided()
    {
        var rig = Build(script: store => store.OnListPosts = (_, _) => Result<IReadOnlyList<SocialPost>>.Failure("down", SocialFailureCodes.StoreUnavailable));

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(rig.Triage.Requests);
        Assert.Equal("load the post", Assert.Single(rig.Status.Snapshot().Failures).Step);
    }

    [Fact]
    public async Task Posts_are_read_up_to_the_stores_own_maximum()
    {
        int? limit = null;
        var rig = Build(script: store => store.OnListPosts = (_, requested) =>
        {
            limit = requested;
            return Result<IReadOnlyList<SocialPost>>.Success(Array.Empty<SocialPost>());
        });

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SocialOrchestrator.PostLookup, limit);
    }

    [Fact]
    public async Task Nothing_private_reaches_a_log_line_on_any_failure_path()
    {
        var rig = Build(
            triage: _ => Decide(needsReply: true, ReplyCategories.Question),
            drafter: _ => Written("PRIVATE-DRAFT — with a dash"));
        rig.Store.OnCreateDraft = _ => Result<ReplyDraft>.Failure("down", SocialFailureCodes.StoreUnavailable);

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotEmpty(rig.Log.Entries);
        AssertNothingPrivateLogged(rig);
    }

    // ----- Recovery -----

    [Fact]
    public async Task Recovery_fails_interrupted_sends_purges_and_handles_every_undecided_comment_oldest_first()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var drafts = new[] { Guid.NewGuid(), Guid.NewGuid() };
        DateTime? purgedAt = null;
        DateTime? failedAt = null;
        string? listedState = null;
        var rig = Build(script: store =>
        {
            store.OnFailInterruptedSends = at =>
            {
                failedAt = at;
                return Result<SocialInterruptedSends>.Success(new SocialInterruptedSends { DraftIds = drafts.ToList() });
            };
            store.OnPurge = at =>
            {
                purgedAt = at;
                return Result<SocialPurgeResult>.Success(new SocialPurgeResult { EventsScrubbed = 3 });
            };
            store.OnListEvents = (state, _, _) =>
            {
                listedState = state;
                return Result<IReadOnlyList<EngagementEvent>>.Success(new[] { Comment(first), Comment(second) });
            };
        });

        await rig.Orchestrator.RecoverAsync(CancellationToken.None);

        Assert.Equal(Now.UtcDateTime, failedAt);
        Assert.Equal(Now.UtcDateTime, purgedAt);
        Assert.Equal(TriageOutcomes.Untriaged, listedState);
        Assert.Equal(new[] { first, second }, rig.Outcomes.Select(o => o.EventId));
        Assert.False(rig.Wake.Reader.TryRead(out _));
        var snapshot = rig.Status.Snapshot();
        Assert.Equal(Now.UtcDateTime, snapshot.RecoveredAt);
        Assert.Equal(2, snapshot.EventsRecoveredAtStartup);
        Assert.Equal(2, snapshot.InterruptedSendsFailed);
        Assert.Equal(3, snapshot.EventsScrubbed);
        Assert.Empty(snapshot.Failures);
    }

    [Fact]
    public async Task Recovery_reads_in_bounded_batches_until_a_batch_is_not_full()
    {
        var limits = new List<int>();
        var full = Enumerable.Range(0, SocialOrchestrator.RecoveryBatch).Select(_ => Comment(Guid.NewGuid())).ToArray();
        var last = Comment(Guid.NewGuid());
        var rig = Build(script: store => store.OnListEvents = (_, _, requested) =>
        {
            limits.Add(requested);
            return Result<IReadOnlyList<EngagementEvent>>.Success(limits.Count == 1 ? full : new[] { last });
        });

        await rig.Orchestrator.RecoverAsync(CancellationToken.None);

        Assert.Equal(new[] { SocialOrchestrator.RecoveryBatch, SocialOrchestrator.RecoveryBatch }, limits);
        Assert.Equal(SocialOrchestrator.RecoveryBatch + 1, rig.Outcomes.Count);
        Assert.Equal(SocialOrchestrator.RecoveryBatch + 1, rig.Status.Snapshot().EventsRecoveredAtStartup);
    }

    [Fact]
    public async Task Recovery_does_not_spin_or_try_a_comment_twice_when_the_store_keeps_failing()
    {
        var full = Enumerable.Range(0, SocialOrchestrator.RecoveryBatch).Select(_ => Comment(Guid.NewGuid())).ToArray();
        var listings = 0;
        var rig = Build(script: store =>
        {
            store.OnListEvents = (_, _, _) =>
            {
                listings++;
                return Result<IReadOnlyList<EngagementEvent>>.Success(full);
            };
            store.OnGetAccount = _ => Result<SocialAccount>.Failure("down", SocialFailureCodes.StoreUnavailable);
        });

        await rig.Orchestrator.RecoverAsync(CancellationToken.None);

        Assert.Equal(1, listings);
        Assert.Empty(rig.Triage.Requests);
        Assert.Equal(SocialOrchestrator.RecoveryBatch, rig.Status.Snapshot().EventsRecoveredAtStartup);
    }

    [Fact]
    public async Task Recovery_skips_a_comment_it_already_tried_in_the_same_pass()
    {
        var stuck = Comment(Guid.NewGuid());
        var full = Enumerable.Range(0, SocialOrchestrator.RecoveryBatch - 1).Select(_ => Comment(Guid.NewGuid())).Append(stuck).ToArray();
        var listings = 0;
        var accountLoads = 0;
        var rig = Build(script: store =>
        {
            store.OnListEvents = (_, _, _) => Result<IReadOnlyList<EngagementEvent>>.Success(++listings == 1 ? full : new[] { stuck });
            store.OnGetEvent = id => Result<EngagementEvent>.Success(id == stuck.Id ? stuck : Comment(id));
        });
        rig.Store.OnRecordTriageOutcome = (id, command) =>
        {
            if (id == stuck.Id)
                return Result<EngagementEvent>.Failure("down", SocialFailureCodes.StoreUnavailable);
            rig.Outcomes.Add((id, command));
            return Result<EngagementEvent>.Success(Comment(id));
        };
        rig.Store.OnGetAccount = id =>
        {
            accountLoads++;
            return Result<SocialAccount>.Success(new SocialAccount { Id = id, DisplayName = "Geek @ Your Spot" });
        };

        await rig.Orchestrator.RecoverAsync(CancellationToken.None);

        Assert.Equal(2, listings);
        Assert.Equal(SocialOrchestrator.RecoveryBatch, accountLoads);
        Assert.Equal(SocialOrchestrator.RecoveryBatch, rig.Triage.Requests.Count);
    }

    [Fact]
    public async Task A_failed_recovery_step_is_recorded_for_the_operator_and_the_other_steps_still_run()
    {
        var pending = Guid.NewGuid();
        var purged = false;
        var rig = Build(script: store =>
        {
            store.OnFailInterruptedSends = _ =>
                Result<SocialInterruptedSends>.Failure("The store is down.", SocialFailureCodes.StoreUnavailable);
            store.OnPurge = _ =>
            {
                purged = true;
                return Result<SocialPurgeResult>.Success(new SocialPurgeResult());
            };
            store.OnListEvents = (_, _, _) => Result<IReadOnlyList<EngagementEvent>>.Success(new[] { Comment(pending) });
        });

        await rig.Orchestrator.RecoverAsync(CancellationToken.None);

        Assert.True(purged);
        Assert.Equal(pending, Assert.Single(rig.Outcomes).EventId);
        var failure = Assert.Single(rig.Status.Snapshot().Failures);
        Assert.Equal("fail interrupted sends", failure.Step);
        Assert.Equal(SocialFailureCodes.StoreUnavailable, failure.Code);
        Assert.Equal(Now.UtcDateTime, failure.At);
        Assert.Single(rig.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_failed_purge_and_a_failed_listing_are_both_recorded_and_handle_nothing()
    {
        var rig = Build(script: store =>
        {
            store.OnPurge = _ => Result<SocialPurgeResult>.Failure("down", SocialFailureCodes.StoreUnavailable);
            store.OnListEvents = (_, _, _) =>
                Result<IReadOnlyList<EngagementEvent>>.Failure("down", SocialFailureCodes.StoreUnavailable);
        });

        await rig.Orchestrator.RecoverAsync(CancellationToken.None);

        Assert.Empty(rig.Triage.Requests);
        Assert.Equal(new[] { "purge expired text", "list undecided events" }, rig.Status.Snapshot().Failures.Select(f => f.Step));
        Assert.Equal(2, rig.Log.Entries.Count(e => e.Level == LogLevel.Error));
    }

    [Fact]
    public async Task One_comment_that_throws_in_recovery_is_recorded_and_the_rest_are_still_handled()
    {
        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();
        var rig = Build(script: store =>
        {
            store.OnListEvents = (_, _, _) => Result<IReadOnlyList<EngagementEvent>>.Success(new[] { Comment(bad), Comment(good) });
            store.OnGetEvent = id => id == bad
                ? throw new InvalidOperationException("the store blew up")
                : Result<EngagementEvent>.Success(Comment(id));
        });

        await rig.Orchestrator.RecoverAsync(CancellationToken.None);

        Assert.Equal(good, Assert.Single(rig.Outcomes).EventId);
        Assert.Equal(("handle an event", "unexpected_error"), (rig.Status.Snapshot().Failures[0].Step, rig.Status.Snapshot().Failures[0].Code));
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    // ----- Intake and the service loop -----

    [Fact]
    public async Task Taking_an_event_in_handles_it_and_then_purges()
    {
        var id = Guid.NewGuid();
        var purges = 0;
        var rig = Build(script: store => store.OnPurge = _ =>
        {
            purges++;
            return Result<SocialPurgeResult>.Success(new SocialPurgeResult { EventsScrubbed = 1 });
        });

        await rig.Orchestrator.TakeInAsync(id, CancellationToken.None);

        Assert.Equal(id, Assert.Single(rig.Outcomes).EventId);
        Assert.Equal(1, purges);
        var snapshot = rig.Status.Snapshot();
        Assert.Equal(1, snapshot.EventsTakenIn);
        Assert.Equal(1, snapshot.EventsScrubbed);
    }

    [Fact]
    public async Task An_event_that_cannot_be_loaded_is_recorded_and_the_purge_still_runs()
    {
        var purged = false;
        var rig = Build(script: store =>
        {
            store.OnGetEvent = _ => Result<EngagementEvent>.Failure("No such event.", SocialFailureCodes.NotFound);
            store.OnPurge = _ =>
            {
                purged = true;
                return Result<SocialPurgeResult>.Success(new SocialPurgeResult());
            };
        });

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(purged);
        Assert.Equal(0, rig.Status.Snapshot().EventsTakenIn);
        var failure = Assert.Single(rig.Status.Snapshot().Failures);
        Assert.Equal(("load an event", SocialFailureCodes.NotFound), (failure.Step, failure.Code));
        Assert.Single(rig.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task The_service_handles_an_event_it_is_woken_with()
    {
        var id = Guid.NewGuid();
        var handled = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rig = Build();
        rig.Store.OnRecordTriageOutcome = (eventId, _) =>
        {
            handled.TrySetResult(eventId);
            return Result<EngagementEvent>.Success(Comment(eventId));
        };

        await rig.Orchestrator.StartAsync(CancellationToken.None);
        rig.Wake.Wake(id);
        var seen = await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await rig.Orchestrator.StopAsync(CancellationToken.None);

        Assert.Equal(id, seen);
    }

    [Fact]
    public async Task Recovery_that_failed_at_startup_runs_again_at_the_next_wake()
    {
        var listings = 0;
        var woken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rig = Build(script: store =>
        {
            store.OnListEvents = (_, _, _) =>
            {
                listings++;
                return listings == 1
                    ? Result<IReadOnlyList<EngagementEvent>>.Failure("not up yet", SocialFailureCodes.StoreUnavailable)
                    : Result<IReadOnlyList<EngagementEvent>>.Success(Array.Empty<EngagementEvent>());
            };
            store.OnGetEvent = requested =>
            {
                woken.TrySetResult();
                return Result<EngagementEvent>.Success(Comment(requested));
            };
        });

        await rig.Orchestrator.StartAsync(CancellationToken.None);
        rig.Wake.Wake(Guid.NewGuid());
        await woken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await rig.Orchestrator.StopAsync(CancellationToken.None);

        Assert.Equal(2, listings);
        Assert.Single(rig.Status.Snapshot().Failures);
    }

    [Fact]
    public async Task Recovery_that_completed_is_not_run_again_at_each_wake()
    {
        var listings = 0;
        var woken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rig = Build(script: store =>
        {
            store.OnListEvents = (_, _, _) =>
            {
                listings++;
                return Result<IReadOnlyList<EngagementEvent>>.Success(Array.Empty<EngagementEvent>());
            };
            store.OnGetEvent = requested =>
            {
                woken.TrySetResult();
                return Result<EngagementEvent>.Success(Comment(requested));
            };
        });

        await rig.Orchestrator.StartAsync(CancellationToken.None);
        rig.Wake.Wake(Guid.NewGuid());
        await woken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await rig.Orchestrator.StopAsync(CancellationToken.None);

        Assert.Equal(1, listings);
    }

    [Fact]
    public async Task A_store_that_throws_on_one_event_does_not_stop_the_next_one_being_handled()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var loadedSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rig = Build(script: store => store.OnGetEvent = requested =>
        {
            if (requested == first)
                throw new InvalidOperationException("the store blew up");
            loadedSecond.TrySetResult();
            return Result<EngagementEvent>.Success(Comment(requested));
        });

        await rig.Orchestrator.StartAsync(CancellationToken.None);
        rig.Wake.Wake(first);
        rig.Wake.Wake(second);
        await loadedSecond.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await rig.Orchestrator.StopAsync(CancellationToken.None);

        var failure = Assert.Single(rig.Status.Snapshot().Failures);
        Assert.Equal(("take in an event", "unexpected_error"), (failure.Step, failure.Code));
    }

    [Fact]
    public async Task A_startup_recovery_that_throws_is_recorded_and_the_service_keeps_running()
    {
        var calls = 0;
        var woken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rig = Build(script: store =>
        {
            store.OnFailInterruptedSends = _ =>
                ++calls == 1
                    ? throw new InvalidOperationException("boom")
                    : Result<SocialInterruptedSends>.Success(new SocialInterruptedSends());
            store.OnGetEvent = requested =>
            {
                woken.TrySetResult();
                return Result<EngagementEvent>.Success(Comment(requested));
            };
        });

        await rig.Orchestrator.StartAsync(CancellationToken.None);
        rig.Wake.Wake(Guid.NewGuid());
        await woken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await rig.Orchestrator.StopAsync(CancellationToken.None);

        Assert.Equal(("recover at startup", "unexpected_error"), (rig.Status.Snapshot().Failures[0].Step, rig.Status.Snapshot().Failures[0].Code));
        Assert.Equal(2, calls);
    }

    // ----- The send side: own comments, auto-send, waiting drafts, re-runs and live updates -----

    [Fact]
    public async Task The_accounts_own_comment_is_never_triaged_and_needs_no_reply()
    {
        var rig = Build(script: store =>
        {
            store.OnGetEvent = id =>
            {
                var own = Comment(id);
                own.AuthorPlatformId = "PAGE1";
                return Result<EngagementEvent>.Success(own);
            };
            store.OnGetAccount = id => Result<SocialAccount>.Success(new SocialAccount
            {
                Id = id, Platform = SocialPlatforms.Facebook, PlatformAccountId = "PAGE1", DisplayName = "Geek @ Your Spot",
            });
        });

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(rig.Triage.Requests);
        Assert.Equal(TriageOutcomes.NoReplyNeeded, Assert.Single(rig.Outcomes).Command.Outcome);
    }

    [Fact]
    public async Task A_pending_draft_in_an_opted_in_category_is_auto_sent_after_its_outcome_is_stored()
    {
        var rig = Build(
            script: store =>
            {
                store.OnGetAccount = id => Result<SocialAccount>.Success(new SocialAccount
                {
                    Id = id, Platform = SocialPlatforms.Facebook, PlatformAccountId = "PAGE1", DisplayName = "Geek @ Your Spot",
                    Status = SocialAccountStatuses.Connected, AutoSendCategories = new List<string> { ReplyCategories.Praise },
                });
            },
            triage: _ => Decide(needsReply: true, ReplyCategories.Praise),
            drafter: _ => Written("Thank you, that is kind of you."));
        rig.Store.OnCreateDraft = command => Result<ReplyDraft>.Success(new ReplyDraft
        {
            Id = Guid.NewGuid(), AccountId = AccountId, EngagementEventId = command.EngagementEventId, Platform = SocialPlatforms.Facebook,
            Category = command.Category, DraftText = command.DraftText, Status = ReplyDraftStatuses.Pending,
        });

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Single(rig.SendStep.AutoSent);
        Assert.Equal(new[] { "draft", "outcome" }, rig.Store.Writes);
    }

    [Fact]
    public async Task A_draft_in_a_category_not_opted_into_waits_for_a_person()
    {
        var rig = Build(triage: _ => Decide(needsReply: true, ReplyCategories.Question), drafter: _ => Written("We do, on Saturdays."));

        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(rig.SendStep.AutoSent);
        Assert.Single(rig.Drafts);
    }

    [Fact]
    public async Task Each_event_taken_in_and_each_recovery_sends_the_drafts_that_were_waiting()
    {
        var rig = Build();

        await rig.Orchestrator.RecoverAsync(CancellationToken.None);
        await rig.Orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, rig.SendStep.WaitingPasses);
    }

    [Fact]
    public async Task The_screen_is_told_of_each_outcome_and_draft_by_id_and_state_only()
    {
        var rig = Build(triage: _ => Decide(needsReply: true, ReplyCategories.Question), drafter: _ => Written("We do, on Saturdays."));
        var id = Guid.NewGuid();

        await rig.Orchestrator.TakeInAsync(id, CancellationToken.None);

        Assert.Contains(rig.Realtime.Sent, m => m.Message == "draftChanged");
        Assert.Contains(("commentChanged", id, AccountId, TriageOutcomes.ReplyNeeded), rig.Realtime.Sent);
    }

    [Fact]
    public async Task Running_a_held_comment_again_triages_it_and_returns_its_new_outcome()
    {
        var id = Guid.NewGuid();
        var outcome = TriageOutcomes.BlockedNoClearance;
        var rig = Build(script: store => store.OnGetEvent = eventId =>
        {
            var held = Comment(eventId);
            held.TriageOutcome = outcome;
            return Result<EngagementEvent>.Success(held);
        });
        rig.Store.OnRecordTriageOutcome = (eventId, command) =>
        {
            rig.Outcomes.Add((eventId, command));
            outcome = command.Outcome;
            return Result<EngagementEvent>.Success(Comment(eventId));
        };

        var result = await rig.Orchestrator.RerunAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(rig.Triage.Requests);
        Assert.Equal(TriageOutcomes.NoReplyNeeded, result.Value!.TriageOutcome);
    }

    [Theory]
    [InlineData(TriageOutcomes.ReplyNeeded)]
    [InlineData(TriageOutcomes.NoReplyNeeded)]
    public async Task A_decided_comment_is_not_run_again(string decided)
    {
        var rig = Build(script: store => store.OnGetEvent = id =>
        {
            var done = Comment(id);
            done.TriageOutcome = decided;
            return Result<EngagementEvent>.Success(done);
        });

        var result = await rig.Orchestrator.RerunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SocialFailureCodes.Conflict, result.ErrorCode);
        Assert.Empty(rig.Triage.Requests);
    }

    // ----- What it may reach -----

    [Fact]
    public void The_orchestrator_takes_no_agent_model_or_platform_in_its_constructor()
    {
        var constructor = Assert.Single(typeof(SocialOrchestrator).GetConstructors());
        var taken = constructor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.Equal(
            new[]
            {
                typeof(SocialWake), typeof(IServiceScopeFactory), typeof(SocialOrchestratorStatus),
                typeof(GeekAPI.Services.Social.Realtime.ISocialRealtime), typeof(TimeProvider), typeof(ILogger<SocialOrchestrator>),
            },
            taken);
    }

    [Fact]
    public void The_status_keeps_only_the_most_recent_failures()
    {
        var status = new SocialOrchestratorStatus();
        for (var i = 0; i < 30; i++)
            status.RecordFailure($"step {i}", SocialFailureCodes.StoreUnavailable, Now.UtcDateTime);

        var kept = status.Snapshot().Failures;

        Assert.Equal(20, kept.Count);
        Assert.Equal("step 10", kept[0].Step);
        Assert.Equal("step 29", kept[^1].Step);
    }

    [Fact]
    public void The_status_refuses_an_outcome_the_store_does_not_hold()
    {
        var status = new SocialOrchestratorStatus();

        Assert.Throws<ArgumentOutOfRangeException>(() => status.RecordOutcome(TriageOutcomes.Untriaged));
    }
}
