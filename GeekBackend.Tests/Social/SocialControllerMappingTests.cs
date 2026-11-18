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
        public Func<RecordSocialStepFailureCommand, Result<SocialStepFailure>>? KeepFailure { get; init; }
        public Func<Guid, Result<SocialTenantStatus>>? TenantStatus { get; init; }

        public Task<Result<SocialStepFailure>> RecordStepFailureAsync(RecordSocialStepFailureCommand command, CancellationToken ct = default) => Task.FromResult(KeepFailure!(command));
        public Task<Result<SocialTenantStatus>> GetTenantStatusAsync(Guid tenantId, int failureLimit, CancellationToken ct = default) => Task.FromResult(TenantStatus!(tenantId));

        public Task<Result<EngagementEvent>> RecordTriageOutcomeAsync(Guid tenantId, Guid eventId, RecordTriageOutcomeCommand command, CancellationToken ct = default) => Task.FromResult(Triage!());
        public Task<Result<IReadOnlyList<EngagementEvent>>> ListEventsAsync(Guid tenantId, string triageState, Guid? accountId, int limit, CancellationToken ct = default) => Task.FromResult(ListEvents!(triageState));

        public Task<Result<SocialAccount>> GetAccountAsync(Guid tenantId, Guid accountId, CancellationToken ct = default) => Task.FromResult(Account!());
        public Task<Result<ReplyDraft>> TransitionDraftAsync(Guid tenantId, Guid draftId, TransitionReplyDraftCommand command, CancellationToken ct = default) => Task.FromResult(Transition!());
        public Task<Result<bool>> RecordEventAsync(EngagementEvent engagementEvent, CancellationToken ct = default) => Task.FromResult(Event!());

        public Task<Result<SocialAccount>> ConnectAccountAsync(Guid tenantId, ConnectSocialAccountCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<IReadOnlyList<SocialAccount>>> ListAccountsAsync(Guid tenantId, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialAccountCredential>> GetCredentialAsync(Guid tenantId, Guid accountId, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialAccount>> StoreTokenAsync(Guid tenantId, Guid accountId, StoreSocialTokenCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialAccount>> MarkReconnectRequiredAsync(Guid tenantId, Guid accountId, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialAccount>> UpdateSettingsAsync(Guid tenantId, Guid accountId, UpdateSocialAccountSettingsCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialPost>> RecordPostAsync(Guid tenantId, RecordSocialPostCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<IReadOnlyList<SocialPost>>> ListPostsAsync(Guid tenantId, Guid accountId, int limit, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<EngagementEvent>> GetEventAsync(Guid tenantId, Guid eventId, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<ReplyDraft>> CreateDraftAsync(Guid tenantId, CreateReplyDraftCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<ReplyDraft>> GetDraftAsync(Guid tenantId, Guid draftId, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<IReadOnlyList<ReplyDraft>>> ListDraftsAsync(Guid tenantId, string? status, Guid? accountId, int limit, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<ReplyDraft>> EditDraftAsync(Guid tenantId, Guid draftId, EditReplyDraftCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<ActionAudit>> RecordAuditAsync(Guid tenantId, ActionAudit audit, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<IReadOnlyList<ActionAudit>>> ListAuditAsync(Guid tenantId, Guid? accountId, int limit, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialPurgeResult>> PurgeExpiredAsync(DateTime asOf, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialInterruptedSends>> FailInterruptedSendsAsync(DateTime at, CancellationToken ct = default) => throw Unscripted();

        public Task<Result<SocialTenant>> GetTenantByOwnerAsync(Guid ownerUserId, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialTenant>> CreateTenantAsync(CreateSocialTenantCommand command, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<SocialTenant>> AcceptAiTermsAsync(Guid tenantId, DateTime at, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<EngagementEvent>> GetEventAcrossTenantsAsync(Guid eventId, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<IReadOnlyList<EngagementEvent>>> ListUntriagedEventsAsync(int limit, CancellationToken ct = default) => throw Unscripted();
        public Task<Result<IReadOnlyList<ReplyDraft>>> ListDraftsAcrossTenantsAsync(string status, int limit, CancellationToken ct = default) => throw Unscripted();

        private static NotSupportedException Unscripted() => new("This test did not script that call.");
    }

    /// <summary>A request that carries the tenant header, as GeekAPI's calls do.</summary>
    private static ControllerContext WithTenant()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Social-Tenant-Id"] = TestTenant.Id.ToString();
        return new ControllerContext { HttpContext = http };
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
        controller.ControllerContext = WithTenant();

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
        controller.ControllerContext = WithTenant();

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
        controller.ControllerContext = WithTenant();

        var action = await controller.Record(new EngagementEvent(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal(inserted, Assert.IsType<SocialEventStored>(ok.Value).Inserted);
    }

    [Fact]
    public async Task A_failed_event_is_the_failure_not_an_inserted_flag()
    {
        var controller = new SocialEventsController(new ScriptedStore { Event = () => Result<bool>.Failure("No connected facebook account matches the event's account.", SocialFailureCodes.NotFound) });
        controller.ControllerContext = WithTenant();

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
        controller.ControllerContext = WithTenant();

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
        controller.ControllerContext = WithTenant();

        var action = await controller.List(triageState: null, accountId: null, limit: 0, CancellationToken.None);

        Assert.Equal(string.Empty, asked);
        Assert.IsType<BadRequestObjectResult>(action);
    }

    // ----- Kept failures and a tenant's status -----

    private static RecordSocialStepFailureCommand Failure() =>
        new() { Step = "load the account", Code = SocialFailureCodes.StoreUnavailable, At = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc) };

    private static Result<SocialStepFailure> Kept(RecordSocialStepFailureCommand command) =>
        Result<SocialStepFailure>.Success(new SocialStepFailure { Id = Guid.NewGuid(), TenantId = command.TenantId, Step = command.Step, Code = command.Code, OccurredAt = command.At });

    [Fact]
    public async Task A_failure_is_kept_for_the_tenant_the_header_names()
    {
        RecordSocialStepFailureCommand? asked = null;
        var controller = new SocialStatusController(new ScriptedStore { KeepFailure = c => Kept(asked = c) }) { ControllerContext = WithTenant() };

        var answer = await controller.RecordFailure(Failure(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(answer);
        Assert.Equal(TestTenant.Id, asked!.TenantId);
    }

    [Fact]
    public async Task A_failure_with_no_header_belongs_to_no_tenant_and_a_body_cannot_name_one()
    {
        RecordSocialStepFailureCommand? asked = null;
        var controller = new SocialStatusController(new ScriptedStore { KeepFailure = c => Kept(asked = c) })
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var command = Failure();
        command.TenantId = Guid.NewGuid();

        var answer = await controller.RecordFailure(command, CancellationToken.None);

        Assert.IsType<OkObjectResult>(answer);
        Assert.Null(asked!.TenantId);
    }

    [Fact]
    public async Task A_header_that_is_not_a_tenant_id_is_refused_not_read_as_no_tenant()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Social-Tenant-Id"] = "not-a-tenant";
        var controller = new SocialStatusController(new ScriptedStore()) { ControllerContext = new ControllerContext { HttpContext = http } };

        var answer = await controller.RecordFailure(Failure(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(answer);
    }

    [Fact]
    public async Task A_status_read_needs_the_tenant_header_and_reads_only_that_tenant()
    {
        Guid? read = null;
        var store = new ScriptedStore { TenantStatus = id => { read = id; return Result<SocialTenantStatus>.Success(new SocialTenantStatus()); } };

        var refused = await new SocialStatusController(store)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        }.Status(10, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(refused);
        Assert.Null(read);

        var answered = await new SocialStatusController(store) { ControllerContext = WithTenant() }.Status(10, CancellationToken.None);
        Assert.IsType<OkObjectResult>(answered);
        Assert.Equal(TestTenant.Id, read);
    }
}
