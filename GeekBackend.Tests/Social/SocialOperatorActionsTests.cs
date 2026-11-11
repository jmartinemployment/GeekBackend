using GeekAPI.Services.Social.Listener;
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
/// What a person does from the screen: an edit or an approval the Reviewer refuses changes nothing; an
/// approval needs text and sends at once; a rejection is final; a post is checked, published once and
/// stored; a like is one audited call; complaint and sensitive can never be set to auto-send.
/// </summary>
public class SocialOperatorActionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid DraftId = Guid.NewGuid();
    private const string PostLink = "https://geekatyourspot.com/bookkeeping";

    private sealed class Rig
    {
        public required SocialOperatorActions Actions { get; init; }
        public required FakeSocialStore Store { get; init; }
        public required FakeSendStep SendStep { get; init; }
        public required SocialSendStepTests.FakeWriter Writer { get; init; }
        public required FakeRealtime Realtime { get; init; }
    }

    private static ReplyDraft Draft(string status = ReplyDraftStatuses.Pending, string text = "Thanks for asking.", string? reason = null) => new()
    {
        Id = DraftId, AccountId = AccountId, EngagementEventId = Guid.NewGuid(), Platform = SocialPlatforms.Facebook,
        Category = ReplyCategories.Question, DraftText = text, NeedsHumanReason = reason, Status = status,
    };

    private static Rig Build(Action<FakeSocialStore>? script = null)
    {
        var store = new FakeSocialStore
        {
            OnGetDraft = _ => Result<ReplyDraft>.Success(Draft()),
            OnGetEvent = id => Result<EngagementEvent>.Success(new EngagementEvent
            {
                Id = id, AccountId = AccountId, Platform = SocialPlatforms.Facebook, Kind = EngagementKinds.CommentCreated,
                PlatformPostId = "PAGE1_POST1", PlatformCommentId = "COMMENT1",
            }),
            OnGetAccount = id => Result<SocialAccount>.Success(new SocialAccount
            {
                Id = id, Platform = SocialPlatforms.Facebook, PlatformAccountId = "PAGE1", Status = SocialAccountStatuses.Connected,
            }),
            OnTransitionDraft = (_, command) => Result<ReplyDraft>.Success(Draft(command.To)),
            OnEditDraft = (_, command) => Result<ReplyDraft>.Success(Draft(text: command.DraftText)),
            OnRecordAudit = a => Result<ActionAudit>.Success(a),
            OnRecordPost = command => Result<SocialPost>.Success(new SocialPost { AccountId = command.AccountId, PlatformPostId = command.PlatformPostId, Text = command.Text }),
            OnUpdateSettings = (id, command) => Result<SocialAccount>.Success(new SocialAccount
            {
                Id = id, AutoSendCategories = command.AutoSendCategories, SendsDisabled = command.SendsDisabled, Status = SocialAccountStatuses.Connected,
            }),
        };
        script?.Invoke(store);

        var sendStep = new FakeSendStep { OnApprovedLinks = _ => Result<List<string>>.Success(new List<string> { PostLink }) };
        var writer = new SocialSendStepTests.FakeWriter();
        var services = new ServiceCollection()
            .AddKeyedSingleton<ISocialEngagementWriter>(SocialPlatforms.Facebook, writer)
            .AddKeyedSingleton<ISocialPublisher>(SocialPlatforms.Facebook, writer)
            .BuildServiceProvider();
        var clock = new MetaEngagementReaderTests.FixedClock(Now);
        var tokens = SocialSendStepTests.FakeTokens.Good();
        var sender = new SocialSender(services, store, clock, new RecordingLog<SocialSender>());
        var resync = new SocialResync(services, store, tokens, new SocialWake(), clock, new RecordingLog<SocialResync>());
        var realtime = new FakeRealtime();

        return new Rig
        {
            Actions = new SocialOperatorActions(store, new OutboundTextReviewer(), sendStep, tokens, sender, resync,
                new GeekAPI.Services.Social.Publishing.PostWriter(FakeModelClient.Never(), new RecordingLog<GeekAPI.Services.Social.Publishing.PostWriter>()),
                realtime, clock, new RecordingLog<SocialOperatorActions>()),
            Store = store,
            SendStep = sendStep,
            Writer = writer,
            Realtime = realtime,
        };
    }

    // ----- Editing -----

    [Fact]
    public async Task An_edit_the_reviewer_passes_is_saved_and_announced()
    {
        var rig = Build();

        var result = await rig.Actions.EditAsync(DraftId, $"  Yes, see {PostLink}.  ", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.Violations);
        Assert.Equal($"Yes, see {PostLink}.", result.Value.Value!.DraftText);
        Assert.Contains(rig.Realtime.Sent, m => m.Message == "draftChanged" && m.Id == DraftId);
    }

    [Fact]
    public async Task An_edit_the_reviewer_refuses_saves_nothing_and_names_the_rules()
    {
        var rig = Build();

        var result = await rig.Actions.EditAsync(DraftId, "Email us at owner@example.com — today.", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains(ReviewRules.EmDash, result.Value!.Violations!);
        Assert.Contains(ReviewRules.EmailAddress, result.Value.Violations!);
        Assert.DoesNotContain("edit", rig.Store.Writes);
    }

    [Fact]
    public async Task Only_a_pending_draft_is_edited()
    {
        var rig = Build(store => store.OnGetDraft = _ => Result<ReplyDraft>.Success(Draft(ReplyDraftStatuses.Sent)));

        var result = await rig.Actions.EditAsync(DraftId, "New text.", CancellationToken.None);

        Assert.Equal(SocialFailureCodes.Conflict, result.ErrorCode);
    }

    // ----- Approving and rejecting -----

    [Fact]
    public async Task An_approval_moves_the_draft_to_approved_and_sends_it_with_the_person_named()
    {
        var rig = Build();

        var result = await rig.Actions.ApproveAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.Value!.Send.Sent);
        Assert.Equal(new[] { "pending->approved" }, rig.Store.Writes);
        Assert.Equal((DraftId, (string?)"user-1"), Assert.Single(rig.SendStep.Sends));
    }

    [Fact]
    public async Task A_draft_a_person_must_write_cannot_be_approved_before_its_text_is_written()
    {
        var rig = Build(store => store.OnGetDraft = _ => Result<ReplyDraft>.Success(Draft(text: string.Empty, reason: "It names an invoice.")));

        var result = await rig.Actions.ApproveAsync(DraftId, "user-1", CancellationToken.None);

        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Empty(rig.Store.Transitions);
        Assert.Empty(rig.SendStep.Sends);
    }

    [Fact]
    public async Task An_approval_of_text_the_reviewer_refuses_approves_and_sends_nothing()
    {
        var rig = Build(store => store.OnGetDraft = _ => Result<ReplyDraft>.Success(Draft(text: "Our SMB plan starts today.")));

        var result = await rig.Actions.ApproveAsync(DraftId, "user-1", CancellationToken.None);

        Assert.Contains(ReviewRules.Smb, result.Value!.Violations!);
        Assert.Empty(rig.Store.Transitions);
        Assert.Empty(rig.SendStep.Sends);
    }

    [Fact]
    public async Task A_send_that_could_not_start_after_approval_is_reported_with_the_approved_draft()
    {
        var rig = Build();
        rig.SendStep.OnSend = _ => Result<SocialSendOutcome>.Failure("GeekRepository is down.", SocialFailureCodes.StoreUnavailable);

        var result = await rig.Actions.ApproveAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var approval = result.Value!.Value!;
        Assert.False(approval.Send.Sent);
        Assert.Equal(SocialFailureCodes.StoreUnavailable, approval.Send.Code);
        Assert.Equal(ReplyDraftStatuses.Approved, approval.Draft.Status);
    }

    [Theory]
    [InlineData(ReplyDraftStatuses.Pending)]
    [InlineData(ReplyDraftStatuses.Approved)]
    public async Task A_pending_or_approved_draft_is_rejected_from_where_it_is(string status)
    {
        var rig = Build(store => store.OnGetDraft = _ => Result<ReplyDraft>.Success(Draft(status)));

        var result = await rig.Actions.RejectAsync(DraftId, "user-1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { $"{status}->rejected" }, rig.Store.Writes);
    }

    [Fact]
    public async Task A_sent_draft_cannot_be_rejected()
    {
        var rig = Build(store => store.OnGetDraft = _ => Result<ReplyDraft>.Success(Draft(ReplyDraftStatuses.Sent)));

        var result = await rig.Actions.RejectAsync(DraftId, "user-1", CancellationToken.None);

        Assert.Equal(SocialFailureCodes.Conflict, result.ErrorCode);
    }

    // ----- Publishing and liking -----

    private static SocialPostRequest Post(string text = "A new guide for small businesses.") => new()
    {
        Kind = SocialPostKinds.Link, Text = text, LinkUrl = PostLink,
    };

    [Fact]
    public async Task A_post_that_passes_is_published_once_audited_and_stored()
    {
        var rig = Build();

        var result = await rig.Actions.PublishAsync(AccountId, Post(), "user-1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("POST9", result.Value!.Value!.PlatformPostId);
        Assert.Single(rig.Writer.Posts);
        Assert.Equal(ActionKinds.Publish, Assert.Single(rig.Store.Audits).Action);
        Assert.Contains("post", rig.Store.Writes);
    }

    [Fact]
    public async Task A_post_the_reviewer_refuses_is_not_published()
    {
        var rig = Build();

        var result = await rig.Actions.PublishAsync(AccountId, Post("Read more at https://example.com/other."), "user-1", CancellationToken.None);

        Assert.Contains(ReviewRules.UnapprovedLink, result.Value!.Violations!);
        Assert.Empty(rig.Writer.Posts);
    }

    [Fact]
    public async Task A_post_that_went_live_and_could_not_be_stored_says_so_with_its_id()
    {
        var rig = Build(store => store.OnRecordPost = _ => Result<SocialPost>.Failure("down", SocialFailureCodes.StoreUnavailable));

        var result = await rig.Actions.PublishAsync(AccountId, Post(), "user-1", CancellationToken.None);

        Assert.Equal(SocialFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.Contains("POST9", result.Error, StringComparison.Ordinal);
        Assert.Contains("Do not publish it again", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_paused_account_publishes_nothing()
    {
        var rig = Build(store => store.OnGetAccount = id => Result<SocialAccount>.Success(new SocialAccount
        {
            Id = id, Platform = SocialPlatforms.Facebook, Status = SocialAccountStatuses.Connected, SendsDisabled = true,
        }));

        var result = await rig.Actions.PublishAsync(AccountId, Post(), "user-1", CancellationToken.None);

        Assert.Equal(SocialFailureCodes.Conflict, result.ErrorCode);
        Assert.Empty(rig.Writer.Posts);
    }

    [Fact]
    public async Task A_like_is_one_audited_call_on_the_comment()
    {
        var rig = Build();

        var result = await rig.Actions.ReactAsync(Guid.NewGuid(), SocialReactionTypes.Like, "user-1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("COMMENT1", Assert.Single(rig.Writer.Reactions).PlatformTargetId);
        Assert.Equal(ActionKinds.React, Assert.Single(rig.Store.Audits).Action);
    }

    // ----- Settings -----

    [Theory]
    [InlineData(ReplyCategories.Complaint)]
    [InlineData(ReplyCategories.Sensitive)]
    [InlineData("rant")]
    public async Task Complaint_sensitive_or_an_unknown_category_can_never_be_set_to_auto_send(string category)
    {
        var rig = Build();

        var result = await rig.Actions.UpdateSettingsAsync(AccountId,
            new UpdateSocialAccountSettingsCommand { AutoSendCategories = new List<string> { ReplyCategories.Praise, category } }, CancellationToken.None);

        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public async Task Settings_are_saved_once_each_and_announced()
    {
        var rig = Build();

        var result = await rig.Actions.UpdateSettingsAsync(AccountId, new UpdateSocialAccountSettingsCommand
        {
            AutoSendCategories = new List<string> { ReplyCategories.Praise, ReplyCategories.Praise },
            SendsDisabled = true,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { ReplyCategories.Praise }, result.Value!.AutoSendCategories);
        Assert.True(result.Value.SendsDisabled);
        Assert.Contains(rig.Realtime.Sent, m => m.Message == "accountChanged");
    }
}
