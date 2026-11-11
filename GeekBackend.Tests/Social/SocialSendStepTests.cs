using GeekAPI.Services.Social.Orchestration;
using GeekAPI.Services.Social.Review;
using GeekAPI.Services.Social.Sending;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The Sender writes one audit row per attempt and never retries. The send step sends only an approved
/// draft from a connected, unpaused account, only text the Reviewer passes, only inside the limits, and
/// only with a token in hand; then it moves the draft approved, sending, sent or failed, and never sends
/// a draft twice. Auto-send never touches complaint, sensitive, a refused draft or one a person must write.
/// </summary>
public class SocialSendStepTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid DraftId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private const string PostLink = "https://geekatyourspot.com/bookkeeping";

    internal sealed class FakeWriter : ISocialEngagementWriter, ISocialPublisher
    {
        public Func<SocialReplyRequest, Result<SocialReplyResult>> OnReply { get; set; } =
            _ => Result<SocialReplyResult>.Success(new SocialReplyResult { PlatformCommentId = "REPLY1" });

        public Func<SocialReactionRequest, Result<SocialReactionResult>> OnReact { get; set; } =
            _ => Result<SocialReactionResult>.Success(new SocialReactionResult());

        public Func<SocialPostRequest, Result<SocialPostResult>> OnPublish { get; set; } =
            _ => Result<SocialPostResult>.Success(new SocialPostResult { PlatformPostId = "POST9" });

        public List<SocialReplyRequest> Replies { get; } = new();
        public List<SocialReactionRequest> Reactions { get; } = new();
        public List<SocialPostRequest> Posts { get; } = new();

        public string Platform => SocialPlatforms.Facebook;

        public Task<Result<SocialReplyResult>> ReplyAsync(SocialAccount account, SocialToken token, SocialReplyRequest request, CancellationToken ct = default)
        {
            Replies.Add(request);
            return Task.FromResult(OnReply(request));
        }

        public Task<Result<SocialReactionResult>> ReactAsync(SocialAccount account, SocialToken token, SocialReactionRequest request, CancellationToken ct = default)
        {
            Reactions.Add(request);
            return Task.FromResult(OnReact(request));
        }

        public Task<Result<SocialPostResult>> PublishAsync(SocialAccount account, SocialToken token, SocialPostRequest request, CancellationToken ct = default)
        {
            Posts.Add(request);
            return Task.FromResult(OnPublish(request));
        }
    }

    internal sealed class FakeTokens(Func<Guid, Result<SocialToken>> answer) : ISocialTokenProvider
    {
        public int Calls { get; private set; }

        public Task<Result<SocialToken>> GetTokenAsync(Guid accountId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(answer(accountId));
        }

        public static FakeTokens Good() => new(_ => Result<SocialToken>.Success(new SocialToken("PRIVATE-TOKEN")));
    }

    private sealed class Rig
    {
        public required SocialSendStep Step { get; init; }
        public required FakeSocialStore Store { get; init; }
        public required FakeWriter Writer { get; init; }
        public required FakeTokens Tokens { get; init; }
        public required FakeRealtime Realtime { get; init; }
        public required SocialOrchestratorStatus Status { get; init; }
        public required RecordingLog<SocialSendStep> Log { get; init; }
        public required RecordingLog<SocialSender> SenderLog { get; init; }
    }

    private static ReplyDraft Draft(string status = ReplyDraftStatuses.Approved, string text = "Thanks for asking. We do.", string category = ReplyCategories.Question) => new()
    {
        Id = DraftId,
        AccountId = AccountId,
        EngagementEventId = EventId,
        Platform = SocialPlatforms.Facebook,
        Category = category,
        DraftText = text,
        Status = status,
        CreatedAt = Now.UtcDateTime.AddMinutes(-5),
    };

    private static SocialAccount Account(bool paused = false, string status = SocialAccountStatuses.Connected, params string[] autoSend) => new()
    {
        Id = AccountId,
        Platform = SocialPlatforms.Facebook,
        PlatformAccountId = "PAGE1",
        DisplayName = "Geek @ Your Spot",
        Status = status,
        SendsDisabled = paused,
        AutoSendCategories = autoSend.ToList(),
    };

    private static ActionAudit Audit(string action, string outcome, string? code, TimeSpan ago) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = AccountId,
        Platform = SocialPlatforms.Facebook,
        Action = action,
        Outcome = outcome,
        FailureCode = code,
        OccurredAt = Now.UtcDateTime - ago,
    };

    private static Rig Build(Action<FakeSocialStore>? script = null, FakeTokens? tokens = null)
    {
        var draft = Draft();
        var store = new FakeSocialStore
        {
            OnGetDraft = _ => Result<ReplyDraft>.Success(draft),
            OnGetAccount = _ => Result<SocialAccount>.Success(Account()),
            OnGetEvent = id => Result<EngagementEvent>.Success(new EngagementEvent
            {
                Id = id, AccountId = AccountId, Platform = SocialPlatforms.Facebook, Kind = EngagementKinds.CommentCreated,
                PlatformPostId = "PAGE1_POST1", PlatformCommentId = "COMMENT1", Text = "PRIVATE-COMMENT",
            }),
            OnListPosts = (_, _) => Result<IReadOnlyList<SocialPost>>.Success(new[]
            {
                new SocialPost { AccountId = AccountId, PlatformPostId = "PAGE1_POST1", LinkUrl = PostLink, Text = "A post" },
            }),
            OnListAudit = (_, _) => Result<IReadOnlyList<ActionAudit>>.Success(Array.Empty<ActionAudit>()),
            OnRecordAudit = a => Result<ActionAudit>.Success(a),
        };
        store.OnTransitionDraft = (id, command) =>
        {
            var moved = Draft(command.To);
            moved.SentPlatformCommentId = command.SentPlatformCommentId;
            moved.FailureCode = command.FailureCode;
            return Result<ReplyDraft>.Success(moved);
        };
        script?.Invoke(store);

        var writer = new FakeWriter();
        var services = new ServiceCollection()
            .AddKeyedSingleton<ISocialEngagementWriter>(SocialPlatforms.Facebook, writer)
            .AddKeyedSingleton<ISocialPublisher>(SocialPlatforms.Facebook, writer)
            .BuildServiceProvider();
        var clock = new MetaEngagementReaderTests.FixedClock(Now);
        var senderLog = new RecordingLog<SocialSender>();
        var sender = new SocialSender(services, store, clock, senderLog);
        var status = new SocialOrchestratorStatus();
        var realtime = new FakeRealtime();
        var log = new RecordingLog<SocialSendStep>();
        tokens ??= FakeTokens.Good();

        return new Rig
        {
            Step = new SocialSendStep(store, tokens, sender, new OutboundTextReviewer(), status, realtime, clock, log),
            Store = store,
            Writer = writer,
            Tokens = tokens,
            Realtime = realtime,
            Status = status,
            Log = log,
            SenderLog = senderLog,
        };
    }

    private static void AssertNothingPrivate(Rig rig)
    {
        foreach (var line in rig.Log.Messages.Concat(rig.SenderLog.Messages))
            Assert.DoesNotContain("PRIVATE", line, StringComparison.Ordinal);
        foreach (var audit in rig.Store.Audits)
            Assert.DoesNotContain("PRIVATE", audit.FailureDetail ?? string.Empty, StringComparison.Ordinal);
    }

    // ----- Sending one approved draft -----

    [Fact]
    public async Task An_approved_draft_is_sent_once_recorded_sent_and_audited()
    {
        var rig = Build();

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.Sent);
        var reply = Assert.Single(rig.Writer.Replies);
        Assert.Equal("COMMENT1", reply.ParentPlatformCommentId);
        Assert.Equal("PAGE1_POST1", reply.PlatformPostId);
        Assert.Equal(new[] { "approved->sending", "sending->sent" }, rig.Store.Writes);
        Assert.Equal("REPLY1", rig.Store.Transitions[^1].Command.SentPlatformCommentId);

        var audit = Assert.Single(rig.Store.Audits);
        Assert.Equal(ActionKinds.Reply, audit.Action);
        Assert.Equal(ActionOutcomes.Succeeded, audit.Outcome);
        Assert.Equal("REPLY1", audit.PlatformObjectId);
        Assert.Equal(DraftId, audit.ReplyDraftId);
        Assert.Equal("user-1", audit.ActorUserId);

        Assert.Contains(("draftChanged", DraftId, AccountId, ReplyDraftStatuses.Sent), rig.Realtime.Sent);
        Assert.Equal(1, rig.Status.Snapshot().DraftsSent);
    }

    [Theory]
    [InlineData(SocialFailureCodes.PlatformRejected, ActionOutcomes.Failed)]
    [InlineData(SocialFailureCodes.OutcomeUnknown, ActionOutcomes.OutcomeUnknown)]
    [InlineData(SocialFailureCodes.RateLimited, ActionOutcomes.Failed)]
    public async Task A_send_that_failed_is_recorded_failed_audited_and_never_tried_again(string code, string outcome)
    {
        var rig = Build();
        rig.Writer.OnReply = _ => Result<SocialReplyResult>.Failure("Meta refused the call (HTTP 400, error 100).", code);

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Sent);
        Assert.Equal(code, result.Value.Code);
        Assert.Single(rig.Writer.Replies);
        Assert.Equal(new[] { "approved->sending", "sending->failed" }, rig.Store.Writes);
        Assert.Equal(code, rig.Store.Transitions[^1].Command.FailureCode);
        Assert.Equal(outcome, Assert.Single(rig.Store.Audits).Outcome);
        Assert.Contains(rig.SenderLog.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal(1, rig.Status.Snapshot().DraftsSendFailed);
        AssertNothingPrivate(rig);
    }

    [Fact]
    public async Task A_reply_that_went_live_and_could_not_be_recorded_stays_sending_and_is_logged_critical()
    {
        var rig = Build(store => store.OnTransitionDraft = (_, command) => command.To == ReplyDraftStatuses.Sent
            ? Result<ReplyDraft>.Failure("GeekRepository is down.", SocialFailureCodes.StoreUnavailable)
            : Result<ReplyDraft>.Success(Draft(command.To)));

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.Value!.Sent);
        Assert.Equal(SocialFailureCodes.StoreUnavailable, result.Value.Code);
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Critical);
    }

    [Theory]
    [InlineData(ReplyDraftStatuses.Pending)]
    [InlineData(ReplyDraftStatuses.Sent)]
    [InlineData(ReplyDraftStatuses.Failed)]
    [InlineData(ReplyDraftStatuses.Rejected)]
    public async Task Only_an_approved_draft_is_sent(string status)
    {
        var rig = Build(store => store.OnGetDraft = _ => Result<ReplyDraft>.Success(Draft(status)));

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.Equal(SocialFailureCodes.Conflict, result.ErrorCode);
        Assert.Empty(rig.Writer.Replies);
        Assert.Empty(rig.Store.Transitions);
    }

    [Fact]
    public async Task A_paused_account_holds_the_draft_and_sends_nothing()
    {
        var rig = Build(store => store.OnGetAccount = _ => Result<SocialAccount>.Success(Account(paused: true)));

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.Value!.Held);
        Assert.Empty(rig.Writer.Replies);
        Assert.Empty(rig.Store.Transitions);
        Assert.Equal(0, rig.Tokens.Calls);
    }

    [Fact]
    public async Task An_account_that_must_be_reconnected_holds_the_draft()
    {
        var rig = Build(store => store.OnGetAccount = _ => Result<SocialAccount>.Success(Account(status: SocialAccountStatuses.ReconnectRequired)));

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.Value!.Held);
        Assert.Equal(SocialFailureCodes.TokenInvalid, result.Value.Code);
        Assert.Empty(rig.Writer.Replies);
    }

    [Fact]
    public async Task Text_the_reviewer_refuses_is_rejected_and_not_sent()
    {
        var rig = Build(store => store.OnGetDraft = _ => Result<ReplyDraft>.Success(Draft(text: "Call us — today at 561-555-0100.")));

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.False(result.Value!.Sent);
        Assert.False(result.Value.Held);
        Assert.Equal(new[] { "approved->rejected" }, rig.Store.Writes);
        Assert.Empty(rig.Writer.Replies);
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task The_post_link_is_the_only_link_a_reply_may_carry()
    {
        var other = Build(store => store.OnGetDraft = _ => Result<ReplyDraft>.Success(Draft(text: "See https://example.com/elsewhere for more.")));
        var own = Build(store => store.OnGetDraft = _ => Result<ReplyDraft>.Success(Draft(text: $"See {PostLink} for more.")));

        Assert.False((await other.Step.SendAsync(DraftId, "u", CancellationToken.None)).Value!.Sent);
        Assert.True((await own.Step.SendAsync(DraftId, "u", CancellationToken.None)).Value!.Sent);
    }

    [Fact]
    public async Task A_recent_throttle_opens_a_cool_down_that_holds_the_draft()
    {
        var rig = Build(store => store.OnListAudit = (_, _) => Result<IReadOnlyList<ActionAudit>>.Success(new[]
        {
            Audit(ActionKinds.Reply, ActionOutcomes.Failed, SocialFailureCodes.RateLimited, TimeSpan.FromMinutes(5)),
        }));

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.Value!.Held);
        Assert.Equal(SocialFailureCodes.RateLimited, result.Value.Code);
        Assert.Equal(Now.UtcDateTime.AddMinutes(10), result.Value.RetryAfter);
        Assert.Empty(rig.Writer.Replies);
        Assert.Equal(0, rig.Tokens.Calls);
    }

    [Fact]
    public async Task A_throttle_older_than_the_cool_down_or_a_refused_request_does_not_hold()
    {
        var rig = Build(store => store.OnListAudit = (_, _) => Result<IReadOnlyList<ActionAudit>>.Success(new[]
        {
            Audit(ActionKinds.Reply, ActionOutcomes.Failed, SocialFailureCodes.RateLimited, TimeSpan.FromMinutes(20)),
            Audit(ActionKinds.Reply, ActionOutcomes.Failed, SocialFailureCodes.InvalidRequest, TimeSpan.FromMinutes(1)),
        }));

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.Value!.Sent);
    }

    [Fact]
    public async Task The_hourly_reply_limit_holds_the_draft_until_the_oldest_falls_out()
    {
        var rows = Enumerable.Range(1, SocialSendLimits.RepliesPerAccountPerHour)
            .Select(i => Audit(ActionKinds.Reply, ActionOutcomes.Succeeded, null, TimeSpan.FromMinutes(i)))
            .ToArray();
        var rig = Build(store => store.OnListAudit = (_, _) => Result<IReadOnlyList<ActionAudit>>.Success(rows));

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.Value!.Held);
        Assert.Equal(Now.UtcDateTime.AddMinutes(60 - SocialSendLimits.RepliesPerAccountPerHour), result.Value.RetryAfter);
    }

    [Fact]
    public async Task A_token_that_cannot_be_had_holds_the_draft_approved_and_sends_nothing()
    {
        var rig = Build(tokens: new FakeTokens(_ => Result<SocialToken>.Failure("Meta is down.", SocialFailureCodes.PlatformUnavailable)));

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.Value!.Held);
        Assert.Equal(SocialFailureCodes.PlatformUnavailable, result.Value.Code);
        Assert.Empty(rig.Store.Transitions);
        Assert.Empty(rig.Writer.Replies);
    }

    [Fact]
    public async Task Losing_the_move_to_sending_holds_and_sends_nothing()
    {
        var rig = Build(store => store.OnTransitionDraft = (_, _) =>
            Result<ReplyDraft>.Failure("Sends are paused for this account.", SocialFailureCodes.Conflict));

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.Value!.Held);
        Assert.Empty(rig.Writer.Replies);
    }

    [Fact]
    public async Task A_store_that_is_down_fails_the_step_and_is_logged()
    {
        var rig = Build(store => store.OnGetDraft = _ => Result<ReplyDraft>.Failure("down", SocialFailureCodes.StoreUnavailable));

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.Equal(SocialFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Error);
        Assert.NotEmpty(rig.Status.Snapshot().Failures);
    }

    // ----- The Sender -----

    [Fact]
    public async Task The_sender_answers_not_configured_and_audits_nothing_for_a_platform_with_no_writer()
    {
        var store = new FakeSocialStore();
        var log = new RecordingLog<SocialSender>();
        var sender = new SocialSender(new ServiceCollection().BuildServiceProvider(), store, new MetaEngagementReaderTests.FixedClock(Now), log);

        var result = await sender.ReplyAsync(Account(), new SocialToken("t"), new SocialReplyRequest(), DraftId, null);

        Assert.Equal(SocialFailureCodes.NotConfigured, result.ErrorCode);
        Assert.Empty(store.Audits);
        Assert.Equal(LogLevel.Error, Assert.Single(log.Entries).Level);
    }

    [Fact]
    public async Task An_audit_row_that_cannot_be_stored_does_not_undo_the_platform_result_and_is_logged()
    {
        var rig = Build(store => store.OnRecordAudit = _ => Result<ActionAudit>.Failure("down", SocialFailureCodes.StoreUnavailable));

        var result = await rig.Step.SendAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.Value!.Sent);
        Assert.Contains(rig.SenderLog.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("audit row", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_sender_audits_a_publish_and_a_like_once_each()
    {
        var rig = Build();
        var services = new ServiceCollection()
            .AddKeyedSingleton<ISocialEngagementWriter>(SocialPlatforms.Facebook, rig.Writer)
            .AddKeyedSingleton<ISocialPublisher>(SocialPlatforms.Facebook, rig.Writer)
            .BuildServiceProvider();
        var sender = new SocialSender(services, rig.Store, new MetaEngagementReaderTests.FixedClock(Now), new RecordingLog<SocialSender>());

        await sender.PublishAsync(Account(), new SocialToken("t"), new SocialPostRequest { Text = "x", LinkUrl = PostLink }, "user-1");
        await sender.ReactAsync(Account(), new SocialToken("t"), new SocialReactionRequest { PlatformTargetId = "COMMENT1" }, "user-1");

        Assert.Equal(new[] { ActionKinds.Publish, ActionKinds.React }, rig.Store.Audits.Select(a => a.Action));
        Assert.Equal(new[] { "POST9", "COMMENT1" }, rig.Store.Audits.Select(a => a.PlatformObjectId));
    }

    // ----- Auto-send -----

    [Fact]
    public async Task A_category_the_account_opted_into_is_approved_and_sent_without_a_person()
    {
        var rig = Build(store => store.OnGetDraft = _ => Result<ReplyDraft>.Success(Draft()));
        var pending = Draft(ReplyDraftStatuses.Pending, category: ReplyCategories.Praise);

        var result = await rig.Step.AutoSendAsync(pending, Account(autoSend: ReplyCategories.Praise), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Value!.Sent);
        Assert.Equal(new[] { "pending->approved", "approved->sending", "sending->sent" }, rig.Store.Writes);
        Assert.Null(Assert.Single(rig.Store.Audits).ActorUserId);
        Assert.Equal(1, rig.Status.Snapshot().DraftsAutoApproved);
    }

    public static TheoryData<string, string[], bool, string?, string?> NotAutoSent => new()
    {
        { ReplyCategories.Complaint, new[] { ReplyCategories.Complaint }, false, null, null },
        { ReplyCategories.Sensitive, new[] { ReplyCategories.Sensitive }, false, null, null },
        { ReplyCategories.Question, new[] { ReplyCategories.Praise }, false, null, null },
        { ReplyCategories.Praise, new[] { ReplyCategories.Praise }, true, null, null },
        { ReplyCategories.Praise, new[] { ReplyCategories.Praise }, false, "A person should answer.", null },
        { ReplyCategories.Praise, new[] { ReplyCategories.Praise }, false, null, ReviewRules.EmDash },
    };

    [Theory]
    [MemberData(nameof(NotAutoSent))]
    public async Task Complaint_sensitive_unchosen_paused_for_a_person_or_refused_is_never_auto_sent(
        string category, string[] optedIn, bool paused, string? needsHuman, string? refusal)
    {
        var rig = Build();
        var pending = Draft(ReplyDraftStatuses.Pending, category: category);
        pending.NeedsHumanReason = needsHuman;
        pending.ReviewRefusal = refusal is null ? null : new List<string> { refusal };

        var result = await rig.Step.AutoSendAsync(pending, Account(paused, autoSend: optedIn), CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(rig.Store.Transitions);
        Assert.Empty(rig.Writer.Replies);
    }

    // ----- Drafts a limit held back -----

    [Fact]
    public async Task Waiting_drafts_are_sent_oldest_first_and_an_account_held_again_is_skipped()
    {
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        var rig = Build(store =>
        {
            store.OnListDrafts = (status, _, _) =>
            {
                Assert.Equal(ReplyDraftStatuses.Approved, status);
                return Result<IReadOnlyList<ReplyDraft>>.Success(new[]
                {
                    new ReplyDraft { Id = newer, AccountId = AccountId, Status = ReplyDraftStatuses.Approved, DecidedAt = Now.UtcDateTime.AddMinutes(-1) },
                    new ReplyDraft { Id = older, AccountId = AccountId, Status = ReplyDraftStatuses.Approved, DecidedAt = Now.UtcDateTime.AddMinutes(-30) },
                });
            };
            store.OnListAudit = (_, _) => Result<IReadOnlyList<ActionAudit>>.Success(new[]
            {
                Audit(ActionKinds.Reply, ActionOutcomes.Failed, SocialFailureCodes.RateLimited, TimeSpan.FromMinutes(1)),
            });
        });
        var asked = new List<Guid>();
        rig.Store.OnGetDraft = id =>
        {
            asked.Add(id);
            var d = Draft();
            d.Id = id;
            return Result<ReplyDraft>.Success(d);
        };

        var sent = await rig.Step.SendWaitingAsync(CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Equal(new[] { older }, asked);
        Assert.Empty(rig.Writer.Replies);
    }

    [Fact]
    public async Task A_waiting_list_the_store_cannot_read_is_logged_and_recorded()
    {
        var rig = Build(store => store.OnListDrafts = (_, _, _) =>
            Result<IReadOnlyList<ReplyDraft>>.Failure("down", SocialFailureCodes.StoreUnavailable));

        var sent = await rig.Step.SendWaitingAsync(CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(rig.Status.Snapshot().Failures, f => f.Step == "list approved drafts");
    }
}
