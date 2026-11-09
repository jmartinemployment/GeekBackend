using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using GeekRepository.Controllers.Social;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The repo/social/* controllers turn a store's failure code into a status code, one way for all
/// of them: 404, 400, 409, and 503 for everything else. The caller never has to read a message
/// to tell a lost race from a store that is down.
/// </summary>
public class SocialControllerMappingTests
{
    /// <summary>A store whose three calls under test answer what the test scripts; any other call is a mistake in the test.</summary>
    private sealed class ScriptedStore : ISocialStore
    {
        public Func<Result<SocialAccount>>? Account { get; init; }
        public Func<Result<ReplyDraft>>? Transition { get; init; }
        public Func<Result<bool>>? Event { get; init; }
        public Func<Result<EngagementEvent>>? Triage { get; init; }
        public Func<string, Result<IReadOnlyList<EngagementEvent>>>? ListEvents { get; init; }

        public Task<Result<EngagementEvent>> RecordTriageOutcomeAsync(Guid eventId, RecordTriageOutcomeCommand command, CancellationToken ct = default) => Task.FromResult(Triage!());
        public Task<Result<IReadOnlyList<EngagementEvent>>> ListEventsAsync(string triageState, Guid? accountId, int limit, CancellationToken ct = default) => Task.FromResult(ListEvents!(triageState));

        public Task<Result<SocialAccount>> GetAccountAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(Account!());
        public Task<Result<ReplyDraft>> TransitionDraftAsync(Guid draftId, TransitionReplyDraftCommand command, CancellationToken ct = default) => Task.FromResult(Transition!());
        public Task<Result<bool>> RecordEventAsync(EngagementEvent engagementEvent, CancellationToken ct = default) => Task.FromResult(Event!());

        public Task<Result<SocialAccount>> ConnectAccountAsync(ConnectSocialAccountCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<IReadOnlyList<SocialAccount>>> ListAccountsAsync(CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialAccountCredential>> GetCredentialAsync(Guid accountId, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialAccount>> StoreTokenAsync(Guid accountId, StoreSocialTokenCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialAccount>> MarkReconnectRequiredAsync(Guid accountId, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialAccount>> UpdateSettingsAsync(Guid accountId, UpdateSocialAccountSettingsCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialPost>> RecordPostAsync(RecordSocialPostCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<IReadOnlyList<SocialPost>>> ListPostsAsync(Guid accountId, int limit, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<EngagementEvent>> GetEventAsync(Guid eventId, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<ReplyDraft>> CreateDraftAsync(CreateReplyDraftCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<ReplyDraft>> GetDraftAsync(Guid draftId, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<IReadOnlyList<ReplyDraft>>> ListDraftsAsync(string? status, Guid? accountId, int limit, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<ReplyDraft>> EditDraftAsync(Guid draftId, EditReplyDraftCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<ActionAudit>> RecordAuditAsync(ActionAudit audit, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<IReadOnlyList<ActionAudit>>> ListAuditAsync(Guid? accountId, int limit, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialPurgeResult>> PurgeExpiredAsync(DateTime asOf, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialInterruptedSends>> FailInterruptedSendsAsync(DateTime at, CancellationToken ct = default) => throw Unscripted();

        private static NotSupportedException Unscripted() => new("This test did not script that call.");
    }

    [Theory]
    [InlineData(SocialFailureCodes.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(SocialFailureCodes.InvalidRequest, StatusCodes.Status400BadRequest)]
    [InlineData(SocialFailureCodes.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(SocialFailureCodes.StoreUnavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(SocialFailureCodes.EncryptionUnavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData("something-new", StatusCodes.Status503ServiceUnavailable)]
    [InlineData(null, StatusCodes.Status503ServiceUnavailable)]
    public async Task A_failure_code_becomes_one_status(string? code, int expected)
    {
        var failure = code is null ? Result<SocialAccount>.Failure("It failed.") : Result<SocialAccount>.Failure("It failed.", code);
        var controller = new SocialAccountsController(new ScriptedStore { Account = () => failure });

        var action = await controller.Get(Guid.NewGuid(), CancellationToken.None);

        var status = Assert.IsAssignableFrom<IStatusCodeActionResult>(action);
        Assert.Equal(expected, status.StatusCode);
    }

    [Fact]
    public async Task A_lost_race_on_a_draft_is_a_409_with_the_reason()
    {
        var controller = new SocialDraftsController(new ScriptedStore
        {
            Transition = () => Result<ReplyDraft>.Failure("The draft is sent, not pending.", SocialFailureCodes.Conflict),
        });

        var action = await controller.Transition(
            Guid.NewGuid(),
            new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Pending, To = ReplyDraftStatuses.Approved, At = DateTime.UtcNow },
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(action);
        Assert.Equal("The draft is sent, not pending.", conflict.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_event_answers_whether_it_was_new(bool inserted)
    {
        var controller = new SocialEventsController(new ScriptedStore { Event = () => Result<bool>.Success(inserted) });

        var action = await controller.Record(new EngagementEvent(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal(inserted, Assert.IsType<SocialEventStored>(ok.Value).Inserted);
    }

    [Fact]
    public async Task A_failed_event_is_the_failure_not_an_inserted_flag()
    {
        var controller = new SocialEventsController(new ScriptedStore { Event = () => Result<bool>.Failure("No connected facebook account matches the event's account.", SocialFailureCodes.NotFound) });

        var action = await controller.Record(new EngagementEvent(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(action);
    }

    [Fact]
    public async Task A_triage_outcome_for_an_event_already_decided_is_a_409_with_the_reason()
    {
        var controller = new SocialEventsController(new ScriptedStore
        {
            Triage = () => Result<EngagementEvent>.Failure("The event's triage is already reply_needed.", SocialFailureCodes.Conflict),
        });

        var action = await controller.RecordTriage(
            Guid.NewGuid(),
            new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.NoReplyNeeded, At = DateTime.UtcNow },
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(action);
        Assert.Equal("The event's triage is already reply_needed.", conflict.Value);
    }

    [Fact]
    public async Task Listing_events_without_a_triage_state_asks_the_store_with_an_empty_one_and_gets_its_400()
    {
        string? asked = null;
        var controller = new SocialEventsController(new ScriptedStore
        {
            ListEvents = state =>
            {
                asked = state;
                return Result<IReadOnlyList<EngagementEvent>>.Failure("Unknown triage state ''.", SocialFailureCodes.InvalidRequest);
            },
        });

        var action = await controller.List(triageState: null, accountId: null, limit: 0, CancellationToken.None);

        Assert.Equal(string.Empty, asked);
        Assert.IsType<BadRequestObjectResult>(action);
    }
}
