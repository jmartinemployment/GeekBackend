using System.Data;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using GeekRepository.Data;
using GeekRepository.Infrastructure;
using GeekRepository.Repositories.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Social;

/// <summary>
/// What the store refuses before it touches the database, and that a database failure comes back
/// as a failed Result instead of an exception or an empty answer. The context points at a host
/// that is never opened: every case here returns before a query, or fails at the connection
/// factory, which throws.
/// </summary>
public class SocialRepositoryValidationTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc);

    private sealed class ThrowingConnectionFactory : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() =>
            throw new InvalidOperationException("connection string with a password would be here");
    }

    private static SocialRepository NewRepository() => new(
        new SocialDbContext(new DbContextOptionsBuilder<SocialDbContext>().UseNpgsql("Host=localhost;Database=never-opened").Options),
        new ThrowingConnectionFactory(),
        new FakeTimeProvider(new DateTimeOffset(Now)),
        NullLogger<SocialRepository>.Instance);

    private static SocialEncryptedToken ValidToken() => new()
    {
        Cipher = new byte[] { 1, 2, 3 },
        Iv = new byte[12],
        Tag = new byte[16],
    };

    private static void AssertInvalid<T>(Result<T> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task Connecting_an_account_refuses_an_unknown_platform_and_a_malformed_token()
    {
        var repo = NewRepository();
        var good = new ConnectSocialAccountCommand { Platform = SocialPlatforms.Facebook, PlatformAccountId = "123", DisplayName = "Page", Token = ValidToken(), ConnectedAt = Now };

        AssertInvalid(await repo.ConnectAccountAsync(TestTenant.Id, new ConnectSocialAccountCommand { Platform = "myspace", PlatformAccountId = "1", DisplayName = "x", Token = ValidToken(), ConnectedAt = Now }));
        AssertInvalid(await repo.ConnectAccountAsync(TestTenant.Id, new ConnectSocialAccountCommand { Platform = good.Platform, PlatformAccountId = " ", DisplayName = "x", Token = ValidToken(), ConnectedAt = Now }));
        AssertInvalid(await repo.ConnectAccountAsync(TestTenant.Id, new ConnectSocialAccountCommand { Platform = good.Platform, PlatformAccountId = "1", DisplayName = "", Token = ValidToken(), ConnectedAt = Now }));
        AssertInvalid(await repo.ConnectAccountAsync(TestTenant.Id, new ConnectSocialAccountCommand { Platform = good.Platform, PlatformAccountId = "1", DisplayName = "x", Token = new SocialEncryptedToken { Cipher = new byte[] { 1 }, Iv = new byte[11], Tag = new byte[16] }, ConnectedAt = Now }));
        AssertInvalid(await repo.ConnectAccountAsync(TestTenant.Id, new ConnectSocialAccountCommand { Platform = good.Platform, PlatformAccountId = "1", DisplayName = "x", Token = new SocialEncryptedToken { Cipher = Array.Empty<byte>(), Iv = new byte[12], Tag = new byte[16] }, ConnectedAt = Now }));
        AssertInvalid(await repo.StoreTokenAsync(TestTenant.Id, Guid.NewGuid(), new StoreSocialTokenCommand { Token = new SocialEncryptedToken { Cipher = new byte[] { 1 }, Iv = new byte[12], Tag = new byte[15] } }));
    }

    [Fact]
    public async Task Recording_a_post_refuses_a_missing_account_id_and_an_unknown_kind()
    {
        var repo = NewRepository();

        AssertInvalid(await repo.RecordPostAsync(TestTenant.Id, new RecordSocialPostCommand { AccountId = Guid.Empty, PlatformPostId = "p", Kind = SocialPostKinds.Link }));
        AssertInvalid(await repo.RecordPostAsync(TestTenant.Id, new RecordSocialPostCommand { AccountId = Guid.NewGuid(), PlatformPostId = "", Kind = SocialPostKinds.Link }));
        AssertInvalid(await repo.RecordPostAsync(TestTenant.Id, new RecordSocialPostCommand { AccountId = Guid.NewGuid(), PlatformPostId = "p", Kind = "video" }));
    }

    private static EngagementEvent Comment() => new()
    {
        Platform = SocialPlatforms.LinkedInOrganization,
        PlatformEventId = "n1",
        PlatformAccountId = "urn:li:organization:1",
        Kind = EngagementKinds.CommentCreated,
        PlatformCommentId = "c1",
        OccurredAt = Now,
        ReceivedAt = Now,
    };

    [Fact]
    public async Task Recording_an_event_refuses_what_it_could_not_later_answer()
    {
        var repo = NewRepository();

        var unknownPlatform = Comment(); unknownPlatform.Platform = "myspace";
        var unknownKind = Comment(); unknownKind.Kind = "poke";
        var noEventId = Comment(); noEventId.PlatformEventId = "";
        var noAccount = Comment(); noAccount.PlatformAccountId = "";
        var noTimes = Comment(); noTimes.ReceivedAt = default;
        var commentWithoutId = Comment(); commentWithoutId.PlatformCommentId = null;
        var reactionWithoutType = Comment(); reactionWithoutType.Kind = EngagementKinds.ReactionAdded; reactionWithoutType.ReactionType = null;

        foreach (var bad in new[] { unknownPlatform, unknownKind, noEventId, noAccount, noTimes, commentWithoutId, reactionWithoutType })
            AssertInvalid(await repo.RecordEventAsync(bad));
    }

    [Fact]
    public async Task Creating_a_draft_needs_an_event_a_known_category_and_text_or_a_reason()
    {
        var repo = NewRepository();

        AssertInvalid(await repo.CreateDraftAsync(TestTenant.Id, new CreateReplyDraftCommand { EngagementEventId = Guid.Empty, Category = ReplyCategories.Question, DraftText = "Hi" }));
        AssertInvalid(await repo.CreateDraftAsync(TestTenant.Id, new CreateReplyDraftCommand { EngagementEventId = Guid.NewGuid(), Category = "rant", DraftText = "Hi" }));
        AssertInvalid(await repo.CreateDraftAsync(TestTenant.Id, new CreateReplyDraftCommand { EngagementEventId = Guid.NewGuid(), Category = ReplyCategories.Question, DraftText = " ", NeedsHumanReason = null }));
        AssertInvalid(await repo.EditDraftAsync(TestTenant.Id, Guid.NewGuid(), new EditReplyDraftCommand { DraftText = "  " }));
    }

    [Fact]
    public async Task A_refused_draft_names_real_rules_and_keeps_the_text_that_was_refused()
    {
        var repo = NewRepository();
        var id = Guid.NewGuid();

        AssertInvalid(await repo.CreateDraftAsync(TestTenant.Id, new CreateReplyDraftCommand { EngagementEventId = id, Category = ReplyCategories.Question, DraftText = "Hi", ReviewRefusal = new List<string>() }));
        AssertInvalid(await repo.CreateDraftAsync(TestTenant.Id, new CreateReplyDraftCommand { EngagementEventId = id, Category = ReplyCategories.Question, DraftText = "Hi", ReviewRefusal = new List<string> { "swearing" } }));
        AssertInvalid(await repo.CreateDraftAsync(TestTenant.Id, new CreateReplyDraftCommand { EngagementEventId = id, Category = ReplyCategories.Question, DraftText = "Hi", ReviewRefusal = new List<string> { ReviewRules.EmDash, "swearing" } }));
        AssertInvalid(await repo.CreateDraftAsync(TestTenant.Id, new CreateReplyDraftCommand { EngagementEventId = id, Category = ReplyCategories.Question, DraftText = " ", NeedsHumanReason = "A person must answer.", ReviewRefusal = new List<string> { ReviewRules.EmDash } }));
    }

    [Fact]
    public async Task A_triage_outcome_is_refused_unless_it_is_a_known_outcome_with_the_code_that_belongs_to_it()
    {
        var repo = NewRepository();
        var id = Guid.NewGuid();

        AssertInvalid(await repo.RecordTriageOutcomeAsync(TestTenant.Id, id, new RecordTriageOutcomeCommand { Outcome = "maybe", At = Now }));
        AssertInvalid(await repo.RecordTriageOutcomeAsync(TestTenant.Id, id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.Untriaged, At = Now }));
        AssertInvalid(await repo.RecordTriageOutcomeAsync(TestTenant.Id, id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.Failed, At = Now }));
        AssertInvalid(await repo.RecordTriageOutcomeAsync(TestTenant.Id, id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.Failed, FailureCode = "oops", At = Now }));
        AssertInvalid(await repo.RecordTriageOutcomeAsync(TestTenant.Id, id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.ReplyNeeded, FailureCode = SocialFailureCodes.RateLimited, At = Now }));
        AssertInvalid(await repo.RecordTriageOutcomeAsync(TestTenant.Id, id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.NoReplyNeeded }));
    }

    [Fact]
    public async Task A_transition_is_refused_unless_the_state_machine_and_its_required_fields_allow_it()
    {
        var repo = NewRepository();
        var id = Guid.NewGuid();

        // Not an edge of the state machine.
        AssertInvalid(await repo.TransitionDraftAsync(TestTenant.Id, id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Pending, To = ReplyDraftStatuses.Sent, SentPlatformCommentId = "c", At = Now }));
        AssertInvalid(await repo.TransitionDraftAsync(TestTenant.Id, id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Sent, To = ReplyDraftStatuses.Sending, At = Now }));
        // Sent has no comment id; failed has no code or an unknown one.
        AssertInvalid(await repo.TransitionDraftAsync(TestTenant.Id, id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Sending, To = ReplyDraftStatuses.Sent, At = Now }));
        AssertInvalid(await repo.TransitionDraftAsync(TestTenant.Id, id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Sending, To = ReplyDraftStatuses.Failed, At = Now }));
        AssertInvalid(await repo.TransitionDraftAsync(TestTenant.Id, id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Sending, To = ReplyDraftStatuses.Failed, FailureCode = "oops", At = Now }));
        // A field that belongs to a different state.
        AssertInvalid(await repo.TransitionDraftAsync(TestTenant.Id, id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Pending, To = ReplyDraftStatuses.Approved, FailureCode = SocialFailureCodes.RateLimited, At = Now }));
        AssertInvalid(await repo.TransitionDraftAsync(TestTenant.Id, id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Pending, To = ReplyDraftStatuses.Approved, SentPlatformCommentId = "c", At = Now }));
        AssertInvalid(await repo.TransitionDraftAsync(TestTenant.Id, id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Pending, To = ReplyDraftStatuses.Approved }));
    }

    [Fact]
    public async Task An_audit_row_needs_a_known_action_outcome_and_a_code_when_it_did_not_succeed()
    {
        var repo = NewRepository();
        ActionAudit Row() => new() { Platform = SocialPlatforms.Facebook, Action = ActionKinds.Reply, Outcome = ActionOutcomes.Succeeded, OccurredAt = Now };

        var unknownAction = Row(); unknownAction.Action = "dance";
        var unknownOutcome = Row(); unknownOutcome.Outcome = "fine";
        var failedWithoutCode = Row(); failedWithoutCode.Outcome = ActionOutcomes.Failed;
        var unknownWithBadCode = Row(); unknownWithBadCode.Outcome = ActionOutcomes.OutcomeUnknown; unknownWithBadCode.FailureCode = "oops";
        var noTime = Row(); noTime.OccurredAt = default;

        foreach (var bad in new[] { unknownAction, unknownOutcome, failedWithoutCode, unknownWithBadCode, noTime })
            AssertInvalid(await repo.RecordAuditAsync(TestTenant.Id, bad));
    }

    [Fact]
    public async Task Listing_and_maintenance_refuse_unknown_input()
    {
        var repo = NewRepository();

        AssertInvalid(await repo.ListDraftsAsync(TestTenant.Id, "archived", null, 10));
        AssertInvalid(await repo.ListEventsAsync(TestTenant.Id, "archived", null, 10));
        AssertInvalid(await repo.ListEventsAsync(TestTenant.Id, string.Empty, null, 10));
        AssertInvalid(await repo.PurgeExpiredAsync(default));
        AssertInvalid(await repo.FailInterruptedSendsAsync(default));
    }

    [Fact]
    public async Task A_status_read_the_database_cannot_answer_is_a_failure_never_zero_counts()
    {
        var result = await NewRepository().GetTenantStatusAsync(TestTenant.Id, 10);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_step_failure_is_refused_without_a_step_a_code_shaped_like_one_a_time_or_with_an_empty_tenant()
    {
        var repo = NewRepository();
        var cases = new List<RecordSocialStepFailureCommand>
        {
            new() { TenantId = TestTenant.Id, Step = " ", Code = SocialFailureCodes.StoreUnavailable, At = Now },
            new() { TenantId = TestTenant.Id, Step = new string('x', 101), Code = SocialFailureCodes.StoreUnavailable, At = Now },
            new() { TenantId = TestTenant.Id, Step = "load the account", Code = "", At = Now },
            new() { TenantId = TestTenant.Id, Step = "load the account", Code = "Store Unavailable", At = Now },
            new() { TenantId = TestTenant.Id, Step = "load the account", Code = new string('a', 65), At = Now },
            new() { TenantId = TestTenant.Id, Step = "load the account", Code = SocialFailureCodes.StoreUnavailable, At = default },
            new() { TenantId = Guid.Empty, Step = "load the account", Code = SocialFailureCodes.StoreUnavailable, At = Now },
        };

        foreach (var command in cases)
            AssertInvalid(await repo.RecordStepFailureAsync(command));
        AssertInvalid(await repo.RecordStepFailureAsync(null!));
        AssertInvalid(await repo.GetTenantStatusAsync(Guid.Empty, 10));
    }

    [Fact]
    public async Task A_database_failure_is_a_failed_result_that_says_so_and_leaks_nothing()
    {
        var result = await NewRepository().ListAccountsAsync(TestTenant.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.Null(result.Value);
        Assert.DoesNotContain("password", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
