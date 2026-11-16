using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekBackend.Tests.Social;

/// <summary>
/// A store a test scripts one method at a time. A method a test did not script throws, so a component
/// that reaches for the store in a way the test did not expect fails the test and does not get an
/// empty answer that looks like "nothing there".
/// </summary>
internal sealed class FakeSocialStore : ISocialStore
{
    public Func<Guid, Result<SocialTenant>>? OnGetTenantByOwner { get; set; }
    public Func<Guid, Result<SocialTenant>>? OnGetTenant { get; set; }
    public Func<EngagementEvent, Result<bool>>? OnRecordEvent { get; set; }
    public Func<Guid, Result<EngagementEvent>>? OnGetEvent { get; set; }
    public Func<string, Guid?, int, Result<IReadOnlyList<EngagementEvent>>>? OnListEvents { get; set; }
    public Func<DateTime, Result<SocialPurgeResult>>? OnPurge { get; set; }
    public Func<DateTime, Result<SocialInterruptedSends>>? OnFailInterruptedSends { get; set; }
    public Func<Guid, Result<SocialAccount>>? OnGetAccount { get; set; }
    public Func<Guid, Result<SocialAccountCredential>>? OnGetCredential { get; set; }
    public Func<Guid, StoreSocialTokenCommand, Result<SocialAccount>>? OnStoreToken { get; set; }
    public Func<Guid, Result<SocialAccount>>? OnMarkReconnectRequired { get; set; }
    public Func<ConnectSocialAccountCommand, Result<SocialAccount>>? OnConnectAccount { get; set; }
    public Func<ActionAudit, Result<ActionAudit>>? OnRecordAudit { get; set; }
    public Func<Guid, int, Result<IReadOnlyList<SocialPost>>>? OnListPosts { get; set; }
    public Func<Guid, RecordTriageOutcomeCommand, Result<EngagementEvent>>? OnRecordTriageOutcome { get; set; }
    public Func<CreateReplyDraftCommand, Result<ReplyDraft>>? OnCreateDraft { get; set; }
    public Func<Guid, Result<ReplyDraft>>? OnGetDraft { get; set; }
    public Func<string?, Guid?, int, Result<IReadOnlyList<ReplyDraft>>>? OnListDrafts { get; set; }
    public Func<Guid, EditReplyDraftCommand, Result<ReplyDraft>>? OnEditDraft { get; set; }
    public Func<Guid, TransitionReplyDraftCommand, Result<ReplyDraft>>? OnTransitionDraft { get; set; }
    public Func<Guid?, int, Result<IReadOnlyList<ActionAudit>>>? OnListAudit { get; set; }
    public Func<RecordSocialPostCommand, Result<SocialPost>>? OnRecordPost { get; set; }
    public Func<Guid, UpdateSocialAccountSettingsCommand, Result<SocialAccount>>? OnUpdateSettings { get; set; }

    /// <summary>Every transition asked for, in order, whether or not the script accepted it.</summary>
    public List<(Guid DraftId, TransitionReplyDraftCommand Command)> Transitions { get; } = new();

    /// <summary>Every audit row asked for, in order.</summary>
    public List<ActionAudit> Audits { get; } = new();

    /// <summary>Every store write a test may read back, in the order it was made: "draft", "outcome", "edit", "post" or "from->to".</summary>
    public List<string> Writes { get; } = new();

    public List<EngagementEvent> RecordedEvents { get; } = new();

    /// <summary>The tenant of every tenant-scoped call, in order, so a test can check which tenant a component asked for.</summary>
    public List<Guid> TenantsSeen { get; } = new();

    private Task<Result<T>> Seen<T>(Guid tenantId, Result<T> answer)
    {
        TenantsSeen.Add(tenantId);
        return Task.FromResult(answer);
    }

    private static T Scripted<T>(T? script, string method) where T : class =>
        script ?? throw new InvalidOperationException($"The test did not script {method}.");

    public Task<Result<bool>> RecordEventAsync(EngagementEvent engagementEvent, CancellationToken ct = default)
    {
        RecordedEvents.Add(engagementEvent);
        return Task.FromResult(Scripted(OnRecordEvent, nameof(RecordEventAsync))(engagementEvent));
    }

    public Task<Result<EngagementEvent>> GetEventAsync(Guid tenantId, Guid eventId, CancellationToken ct = default) =>
        Seen(tenantId, Scripted(OnGetEvent, nameof(GetEventAsync))(eventId));

    public Task<Result<IReadOnlyList<EngagementEvent>>> ListEventsAsync(Guid tenantId, string triageState, Guid? accountId, int limit, CancellationToken ct = default) =>
        Seen(tenantId, Scripted(OnListEvents, nameof(ListEventsAsync))(triageState, accountId, limit));

    public Task<Result<SocialPurgeResult>> PurgeExpiredAsync(DateTime asOf, CancellationToken ct = default) =>
        Task.FromResult(Scripted(OnPurge, nameof(PurgeExpiredAsync))(asOf));

    public Task<Result<SocialInterruptedSends>> FailInterruptedSendsAsync(DateTime at, CancellationToken ct = default) =>
        Task.FromResult(Scripted(OnFailInterruptedSends, nameof(FailInterruptedSendsAsync))(at));

    public Task<Result<SocialAccount>> GetAccountAsync(Guid tenantId, Guid accountId, CancellationToken ct = default) =>
        Seen(tenantId, Scripted(OnGetAccount, nameof(GetAccountAsync))(accountId));

    public Task<Result<SocialAccountCredential>> GetCredentialAsync(Guid tenantId, Guid accountId, CancellationToken ct = default) =>
        Seen(tenantId, Scripted(OnGetCredential, nameof(GetCredentialAsync))(accountId));

    public Task<Result<SocialAccount>> StoreTokenAsync(Guid tenantId, Guid accountId, StoreSocialTokenCommand command, CancellationToken ct = default) =>
        Seen(tenantId, Scripted(OnStoreToken, nameof(StoreTokenAsync))(accountId, command));

    public Task<Result<SocialAccount>> MarkReconnectRequiredAsync(Guid tenantId, Guid accountId, CancellationToken ct = default) =>
        Seen(tenantId, Scripted(OnMarkReconnectRequired, nameof(MarkReconnectRequiredAsync))(accountId));

    public Task<Result<SocialAccount>> ConnectAccountAsync(Guid tenantId, ConnectSocialAccountCommand command, CancellationToken ct = default) =>
        Seen(tenantId, Scripted(OnConnectAccount, nameof(ConnectAccountAsync))(command));

    public Task<Result<ActionAudit>> RecordAuditAsync(Guid tenantId, ActionAudit audit, CancellationToken ct = default)
    {
        Audits.Add(audit);
        return Seen(tenantId, Scripted(OnRecordAudit, nameof(RecordAuditAsync))(audit));
    }

    public Task<Result<IReadOnlyList<SocialAccount>>> ListAccountsAsync(Guid tenantId, CancellationToken ct = default) => NotUsed<IReadOnlyList<SocialAccount>>();
    public Task<Result<SocialAccount>> UpdateSettingsAsync(Guid tenantId, Guid accountId, UpdateSocialAccountSettingsCommand command, CancellationToken ct = default) =>
        Seen(tenantId, Scripted(OnUpdateSettings, nameof(UpdateSettingsAsync))(accountId, command));
    public Task<Result<SocialPost>> RecordPostAsync(Guid tenantId, RecordSocialPostCommand command, CancellationToken ct = default)
    {
        Writes.Add("post");
        return Seen(tenantId, Scripted(OnRecordPost, nameof(RecordPostAsync))(command));
    }
    public Task<Result<IReadOnlyList<SocialPost>>> ListPostsAsync(Guid tenantId, Guid accountId, int limit, CancellationToken ct = default) =>
        Seen(tenantId, Scripted(OnListPosts, nameof(ListPostsAsync))(accountId, limit));
    public Task<Result<EngagementEvent>> RecordTriageOutcomeAsync(Guid tenantId, Guid eventId, RecordTriageOutcomeCommand command, CancellationToken ct = default)
    {
        Writes.Add("outcome");
        return Seen(tenantId, Scripted(OnRecordTriageOutcome, nameof(RecordTriageOutcomeAsync))(eventId, command));
    }

    public Task<Result<ReplyDraft>> CreateDraftAsync(Guid tenantId, CreateReplyDraftCommand command, CancellationToken ct = default)
    {
        Writes.Add("draft");
        return Seen(tenantId, Scripted(OnCreateDraft, nameof(CreateDraftAsync))(command));
    }
    public Task<Result<ReplyDraft>> GetDraftAsync(Guid tenantId, Guid draftId, CancellationToken ct = default) =>
        Seen(tenantId, Scripted(OnGetDraft, nameof(GetDraftAsync))(draftId));
    public Task<Result<IReadOnlyList<ReplyDraft>>> ListDraftsAsync(Guid tenantId, string? status, Guid? accountId, int limit, CancellationToken ct = default) =>
        Seen(tenantId, Scripted(OnListDrafts, nameof(ListDraftsAsync))(status, accountId, limit));
    public Task<Result<ReplyDraft>> EditDraftAsync(Guid tenantId, Guid draftId, EditReplyDraftCommand command, CancellationToken ct = default)
    {
        Writes.Add("edit");
        return Seen(tenantId, Scripted(OnEditDraft, nameof(EditDraftAsync))(draftId, command));
    }
    public Task<Result<ReplyDraft>> TransitionDraftAsync(Guid tenantId, Guid draftId, TransitionReplyDraftCommand command, CancellationToken ct = default)
    {
        Transitions.Add((draftId, command));
        Writes.Add($"{command.From}->{command.To}");
        return Seen(tenantId, Scripted(OnTransitionDraft, nameof(TransitionDraftAsync))(draftId, command));
    }
    public Task<Result<IReadOnlyList<ActionAudit>>> ListAuditAsync(Guid tenantId, Guid? accountId, int limit, CancellationToken ct = default) =>
        Seen(tenantId, Scripted(OnListAudit, nameof(ListAuditAsync))(accountId, limit));


    public Task<Result<SocialTenant>> GetTenantByOwnerAsync(Guid ownerUserId, CancellationToken ct = default) =>
        Task.FromResult(Scripted(OnGetTenantByOwner, nameof(GetTenantByOwnerAsync))(ownerUserId));
    public Task<Result<SocialTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Scripted(OnGetTenant, nameof(GetTenantAsync))(tenantId));

    public Task<Result<SocialTenant>> CreateTenantAsync(CreateSocialTenantCommand command, CancellationToken ct = default) => throw Unscripted();
    public Task<Result<SocialTenant>> AcceptAiTermsAsync(Guid tenantId, DateTime at, CancellationToken ct = default) => throw Unscripted();

    // The platform reads (no tenant) answer from the script of their tenant-scoped counterpart, so a test scripts one thing.
    public Task<Result<EngagementEvent>> GetEventAcrossTenantsAsync(Guid eventId, CancellationToken ct = default) =>
        Task.FromResult(Scripted(OnGetEvent, nameof(GetEventAcrossTenantsAsync))(eventId));

    public Task<Result<IReadOnlyList<EngagementEvent>>> ListUntriagedEventsAsync(int limit, CancellationToken ct = default) =>
        Task.FromResult(Scripted(OnListEvents, nameof(ListUntriagedEventsAsync))(TriageOutcomes.Untriaged, null, limit));

    public Task<Result<IReadOnlyList<ReplyDraft>>> ListDraftsAcrossTenantsAsync(string status, int limit, CancellationToken ct = default) =>
        Task.FromResult(Scripted(OnListDrafts, nameof(ListDraftsAcrossTenantsAsync))(status, null, limit));

    private static NotSupportedException Unscripted() => new("The component under test should not reach for this store method.");

    private static Task<Result<T>> NotUsed<T>() =>
        throw new InvalidOperationException("This store method is not used by the component under test.");
}
