using System.Text.Json;
using GeekAPI.Services.Social;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekBackend.Tests.Social;

/// <summary>The ids of one tenant's rows in a <see cref="TwoTenantSocialStore"/>.</summary>
internal sealed class TenantRows
{
    public required Guid TenantId { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid EventId { get; init; }
    public required Guid PendingDraftId { get; init; }
    public required Guid ApprovedDraftId { get; init; }
    public required Guid PostId { get; init; }
    public required Guid AuditId { get; init; }
    public required string FailureStep { get; init; }

    /// <summary>Ids that belong to no tenant at all, to compare another tenant's answer with.</summary>
    public static TenantRows Nowhere() => new()
    {
        TenantId = Guid.NewGuid(),
        AccountId = Guid.NewGuid(),
        EventId = Guid.NewGuid(),
        PendingDraftId = Guid.NewGuid(),
        ApprovedDraftId = Guid.NewGuid(),
        PostId = Guid.NewGuid(),
        AuditId = Guid.NewGuid(),
        FailureStep = "nowhere",
    };
}

/// <summary>
/// A store holding two tenants' rows that keeps them apart the way SocialRepository's SQL does: every method that
/// takes a tenantId reads and writes only that tenant's rows, a row of another tenant answers not_found exactly as a
/// row that does not exist, and a list filtered by another tenant's account comes back empty. It records the tenant
/// of every tenant-scoped call and every platform (cross-tenant) call, so a test can show which tenant a route asked
/// for and that no route reached across tenants.
/// </summary>
internal sealed class TwoTenantSocialStore : ISocialStore
{
    public static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    private readonly List<SocialTenant> _tenants = new();
    private readonly List<SocialAccount> _accounts = new();
    private readonly Dictionary<Guid, SocialEncryptedToken> _credentials = new();
    private readonly List<EngagementEvent> _events = new();
    private readonly List<ReplyDraft> _drafts = new();
    private readonly List<(Guid TenantId, SocialPost Post)> _posts = new();
    private readonly List<(Guid TenantId, ActionAudit Audit)> _audit = new();
    private readonly List<SocialStepFailure> _failures = new();

    /// <summary>The tenant of every tenant-scoped call, in order.</summary>
    public List<Guid> TenantsAsked { get; } = new();

    /// <summary>Every platform operation called: these have no signed-in person, and no route may make one.</summary>
    public List<string> AcrossTenantCalls { get; } = new();

    /// <summary>Every step failure kept, in order, with the tenant it was kept for.</summary>
    public List<RecordSocialStepFailureCommand> FailuresKept { get; } = new();

    public TenantRows Seed(Guid ownerUserId, string name, bool aiTermsAccepted, SocialCredentialProtector protector)
    {
        var tag = name.Split(' ')[0].ToLowerInvariant();
        var tenant = new SocialTenant
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerUserId, Name = name,
            AiConsentAcceptedAt = aiTermsAccepted ? Now.AddDays(-7) : null, CreatedAt = Now.AddDays(-30),
        };
        _tenants.Add(tenant);

        var account = new SocialAccount
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Platform = SocialPlatforms.Facebook, PlatformAccountId = $"PAGE-{tag}",
            DisplayName = $"{name} Page", Status = SocialAccountStatuses.Connected, ConnectedAt = Now.AddDays(-30),
        };
        _accounts.Add(account);
        var token = protector.Protect(new SocialToken($"TOKEN-{tag}"));
        _credentials[account.Id] = token.Value ?? throw new InvalidOperationException($"The test token did not seal: {token.Error}");

        var post = new SocialPost
        {
            Id = Guid.NewGuid(), AccountId = account.Id, Platform = SocialPlatforms.Facebook, PlatformPostId = $"POST-{tag}",
            Kind = SocialPostKinds.Link, Text = "Our new guide.", LinkUrl = $"https://example.test/{tag}/guide",
            PublishedAt = Now.AddDays(-1), CreatedAt = Now.AddDays(-1),
        };
        _posts.Add((tenant.Id, post));

        var ev = Comment(tenant.Id, account, post, tag, TriageOutcomes.ReplyNeeded);
        _events.Add(ev);

        var pending = Draft(tenant.Id, account, ev, ReplyDraftStatuses.Pending);
        var approved = Draft(tenant.Id, account, ev, ReplyDraftStatuses.Approved);
        _drafts.Add(pending);
        _drafts.Add(approved);

        var audit = new ActionAudit
        {
            Id = Guid.NewGuid(), AccountId = account.Id, Platform = SocialPlatforms.Facebook, Action = ActionKinds.React,
            Outcome = ActionOutcomes.Succeeded, OccurredAt = Now.AddDays(-2),
        };
        _audit.Add((tenant.Id, audit));

        var failureStep = $"send for {tag}";
        _failures.Add(new SocialStepFailure { Id = Guid.NewGuid(), TenantId = tenant.Id, Step = failureStep, Code = SocialFailureCodes.NotFound, OccurredAt = Now.AddHours(-3) });

        return new TenantRows
        {
            TenantId = tenant.Id, AccountId = account.Id, EventId = ev.Id, PendingDraftId = pending.Id,
            ApprovedDraftId = approved.Id, PostId = post.Id, AuditId = audit.Id, FailureStep = failureStep,
        };
    }

    /// <summary>A comment and a draft beyond the seed, so two tenants' counts differ.</summary>
    public void AddSentDraftAndDeclinedComment(TenantRows rows)
    {
        var account = _accounts.Single(a => a.Id == rows.AccountId);
        var post = _posts.Single(p => p.Post.Id == rows.PostId).Post;
        var declined = Comment(rows.TenantId, account, post, $"declined-{rows.AccountId:N}", TriageOutcomes.NoReplyNeeded);
        _events.Add(declined);
        var sent = Draft(rows.TenantId, account, declined, ReplyDraftStatuses.Sent);
        sent.SentPlatformCommentId = "REPLY-SENT";
        _drafts.Add(sent);
    }

    /// <summary>A failure that belongs to no one tenant, shown to every tenant.</summary>
    public void AddFailureForEveryTenant(string step) =>
        _failures.Add(new SocialStepFailure { Id = Guid.NewGuid(), TenantId = null, Step = step, Code = SocialFailureCodes.StoreUnavailable, OccurredAt = Now.AddHours(-1) });

    public SocialTenant Tenant(Guid tenantId) => _tenants.Single(t => t.Id == tenantId);

    /// <summary>Everything the store holds for one tenant, as JSON, so a test can show none of it changed.</summary>
    public string Snapshot(Guid tenantId) => JsonSerializer.Serialize(new
    {
        Tenant = Tenant(tenantId),
        Accounts = _accounts.Where(a => a.TenantId == tenantId),
        Credentials = _accounts.Where(a => a.TenantId == tenantId).Select(a => _credentials.GetValueOrDefault(a.Id)),
        Events = _events.Where(e => e.TenantId == tenantId),
        Drafts = _drafts.Where(d => d.TenantId == tenantId),
        Posts = _posts.Where(p => p.TenantId == tenantId).Select(p => p.Post),
        Audit = _audit.Where(a => a.TenantId == tenantId).Select(a => a.Audit),
        Failures = _failures.Where(f => f.TenantId == tenantId),
    });

    private static EngagementEvent Comment(Guid tenantId, SocialAccount account, SocialPost post, string tag, string outcome) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, AccountId = account.Id, Platform = account.Platform,
        PlatformEventId = $"EVENT-{tag}", PlatformAccountId = account.PlatformAccountId, Kind = EngagementKinds.CommentCreated,
        PlatformPostId = post.PlatformPostId, PlatformCommentId = $"COMMENT-{tag}", AuthorPlatformId = $"AUTHOR-{tag}",
        AuthorDisplayName = $"Commenter {tag}", Text = "When do you open on Saturday?", OccurredAt = Now.AddHours(-5),
        ReceivedAt = Now.AddHours(-5), TriageOutcome = outcome, TriagedAt = Now.AddHours(-5),
    };

    private static ReplyDraft Draft(Guid tenantId, SocialAccount account, EngagementEvent ev, string status) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, AccountId = account.Id, EngagementEventId = ev.Id, Platform = account.Platform,
        Category = ReplyCategories.Question, DraftText = "Thanks for asking. We open at nine.", Status = status,
        CreatedAt = Now.AddHours(-4), DecidedAt = status == ReplyDraftStatuses.Pending ? null : Now.AddHours(-4),
    };

    private Guid Asked(Guid tenantId)
    {
        TenantsAsked.Add(tenantId);
        return tenantId;
    }

    private static Task<Result<T>> Answer<T>(Result<T> result) => Task.FromResult(result);

    private static Task<Result<T>> NotFound<T>(string what) => Answer(Result<T>.Failure($"No such {what}.", SocialFailureCodes.NotFound));

    // ----- Tenants -----

    public Task<Result<SocialTenant>> GetTenantByOwnerAsync(Guid ownerUserId, CancellationToken ct = default) =>
        _tenants.FirstOrDefault(t => t.OwnerUserId == ownerUserId) is { } tenant
            ? Answer(Result<SocialTenant>.Success(tenant))
            : NotFound<SocialTenant>("tenant");

    public Task<Result<SocialTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        _tenants.FirstOrDefault(t => t.Id == Asked(tenantId)) is { } tenant
            ? Answer(Result<SocialTenant>.Success(tenant))
            : NotFound<SocialTenant>("tenant");

    public Task<Result<SocialTenant>> CreateTenantAsync(CreateSocialTenantCommand command, CancellationToken ct = default)
    {
        if (_tenants.Any(t => t.OwnerUserId == command.OwnerUserId))
            return Answer(Result<SocialTenant>.Failure("This person already has a GeekSocial tenant.", SocialFailureCodes.Conflict));
        var tenant = new SocialTenant { Id = Guid.NewGuid(), OwnerUserId = command.OwnerUserId, Name = command.Name.Trim(), CreatedAt = Now };
        _tenants.Add(tenant);
        return Answer(Result<SocialTenant>.Success(tenant));
    }

    public Task<Result<SocialTenant>> AcceptAiTermsAsync(Guid tenantId, DateTime at, CancellationToken ct = default)
    {
        if (_tenants.FirstOrDefault(t => t.Id == Asked(tenantId)) is not { } tenant)
            return NotFound<SocialTenant>("tenant");
        tenant.AiConsentAcceptedAt ??= at;
        return Answer(Result<SocialTenant>.Success(tenant));
    }

    // ----- Accounts -----

    private SocialAccount? OwnedAccount(Guid tenantId, Guid accountId) =>
        _accounts.FirstOrDefault(a => a.Id == accountId && a.TenantId == Asked(tenantId));

    public Task<Result<SocialAccount>> ConnectAccountAsync(Guid tenantId, ConnectSocialAccountCommand command, CancellationToken ct = default)
    {
        var account = new SocialAccount
        {
            Id = Guid.NewGuid(), TenantId = Asked(tenantId), Platform = command.Platform, PlatformAccountId = command.PlatformAccountId,
            DisplayName = command.DisplayName, Status = SocialAccountStatuses.Connected, ConnectedAt = command.ConnectedAt,
        };
        _accounts.Add(account);
        _credentials[account.Id] = command.Token;
        return Answer(Result<SocialAccount>.Success(account));
    }

    public Task<Result<IReadOnlyList<SocialAccount>>> ListAccountsAsync(Guid tenantId, CancellationToken ct = default) =>
        Answer(Result<IReadOnlyList<SocialAccount>>.Success(_accounts.Where(a => a.TenantId == Asked(tenantId)).ToList()));

    public Task<Result<SocialAccount>> GetAccountAsync(Guid tenantId, Guid accountId, CancellationToken ct = default) =>
        OwnedAccount(tenantId, accountId) is { } account ? Answer(Result<SocialAccount>.Success(account)) : NotFound<SocialAccount>("account");

    public Task<Result<SocialAccountCredential>> GetCredentialAsync(Guid tenantId, Guid accountId, CancellationToken ct = default) =>
        OwnedAccount(tenantId, accountId) is { } account && _credentials.TryGetValue(account.Id, out var token)
            ? Answer(Result<SocialAccountCredential>.Success(new SocialAccountCredential { AccountId = account.Id, Token = token }))
            : NotFound<SocialAccountCredential>("account");

    public Task<Result<SocialAccount>> StoreTokenAsync(Guid tenantId, Guid accountId, StoreSocialTokenCommand command, CancellationToken ct = default)
    {
        if (OwnedAccount(tenantId, accountId) is not { } account)
            return NotFound<SocialAccount>("account");
        _credentials[account.Id] = command.Token;
        account.Status = SocialAccountStatuses.Connected;
        account.TokenExpiresAt = command.Token.ExpiresAt;
        return Answer(Result<SocialAccount>.Success(account));
    }

    public Task<Result<SocialAccount>> MarkReconnectRequiredAsync(Guid tenantId, Guid accountId, CancellationToken ct = default)
    {
        if (OwnedAccount(tenantId, accountId) is not { } account)
            return NotFound<SocialAccount>("account");
        account.Status = SocialAccountStatuses.ReconnectRequired;
        return Answer(Result<SocialAccount>.Success(account));
    }

    public Task<Result<SocialAccount>> UpdateSettingsAsync(Guid tenantId, Guid accountId, UpdateSocialAccountSettingsCommand command, CancellationToken ct = default)
    {
        if (OwnedAccount(tenantId, accountId) is not { } account)
            return NotFound<SocialAccount>("account");
        account.SendsDisabled = command.SendsDisabled;
        return Answer(Result<SocialAccount>.Success(account));
    }

    // ----- Posts -----

    public Task<Result<SocialPost>> RecordPostAsync(Guid tenantId, RecordSocialPostCommand command, CancellationToken ct = default)
    {
        if (OwnedAccount(tenantId, command.AccountId) is not { } account)
            return NotFound<SocialPost>("account");
        var post = new SocialPost
        {
            Id = Guid.NewGuid(), AccountId = account.Id, Platform = account.Platform, PlatformPostId = command.PlatformPostId,
            Kind = command.Kind, Text = command.Text, LinkUrl = command.LinkUrl, ScheduledFor = command.ScheduledFor,
            PublishedAt = command.PublishedAt, CreatedAt = Now,
        };
        _posts.Add((tenantId, post));
        return Answer(Result<SocialPost>.Success(post));
    }

    public Task<Result<IReadOnlyList<SocialPost>>> ListPostsAsync(Guid tenantId, Guid accountId, int limit, CancellationToken ct = default) =>
        Answer(Result<IReadOnlyList<SocialPost>>.Success(_posts
            .Where(p => p.TenantId == Asked(tenantId) && p.Post.AccountId == accountId)
            .Select(p => p.Post).OrderByDescending(p => p.CreatedAt).Take(limit).ToList()));

    // ----- Engagement events -----

    public Task<Result<bool>> RecordEventAsync(EngagementEvent engagementEvent, CancellationToken ct = default)
    {
        if (_events.Any(e => e.Platform == engagementEvent.Platform && e.PlatformEventId == engagementEvent.PlatformEventId))
            return Answer(Result<bool>.Success(false));
        _events.Add(engagementEvent);
        return Answer(Result<bool>.Success(true));
    }

    public Task<Result<EngagementEvent>> GetEventAsync(Guid tenantId, Guid eventId, CancellationToken ct = default) =>
        _events.FirstOrDefault(e => e.Id == eventId && e.TenantId == Asked(tenantId)) is { } ev
            ? Answer(Result<EngagementEvent>.Success(ev))
            : NotFound<EngagementEvent>("event");

    public Task<Result<IReadOnlyList<EngagementEvent>>> ListEventsAsync(Guid tenantId, string triageState, Guid? accountId, int limit, CancellationToken ct = default)
    {
        if (!TriageOutcomes.IsKnownFilter(triageState))
            return Answer(Result<IReadOnlyList<EngagementEvent>>.Failure($"Unknown triage state '{triageState}'.", SocialFailureCodes.InvalidRequest));
        var untriaged = triageState == TriageOutcomes.Untriaged;
        return Answer(Result<IReadOnlyList<EngagementEvent>>.Success(_events
            .Where(e => e.TenantId == Asked(tenantId))
            .Where(e => untriaged ? e.TriageOutcome is null && e.Kind == EngagementKinds.CommentCreated : e.TriageOutcome == triageState)
            .Where(e => accountId is null || e.AccountId == accountId)
            .OrderByDescending(e => e.ReceivedAt).Take(limit).ToList()));
    }

    public Task<Result<EngagementEvent>> GetEventAcrossTenantsAsync(Guid eventId, CancellationToken ct = default)
    {
        AcrossTenantCalls.Add(nameof(GetEventAcrossTenantsAsync));
        return _events.FirstOrDefault(e => e.Id == eventId) is { } ev ? Answer(Result<EngagementEvent>.Success(ev)) : NotFound<EngagementEvent>("event");
    }

    public Task<Result<IReadOnlyList<EngagementEvent>>> ListUntriagedEventsAsync(int limit, CancellationToken ct = default)
    {
        AcrossTenantCalls.Add(nameof(ListUntriagedEventsAsync));
        return Answer(Result<IReadOnlyList<EngagementEvent>>.Success(_events
            .Where(e => e.TriageOutcome is null && e.Kind == EngagementKinds.CommentCreated)
            .OrderBy(e => e.ReceivedAt).Take(limit).ToList()));
    }

    public Task<Result<EngagementEvent>> RecordTriageOutcomeAsync(Guid tenantId, Guid eventId, RecordTriageOutcomeCommand command, CancellationToken ct = default)
    {
        if (_events.FirstOrDefault(e => e.Id == eventId && e.TenantId == Asked(tenantId)) is not { } ev)
            return NotFound<EngagementEvent>("event");
        ev.TriageOutcome = command.Outcome;
        ev.TriageFailureCode = command.FailureCode;
        ev.TriagedAt = command.At;
        return Answer(Result<EngagementEvent>.Success(ev));
    }

    // ----- Reply drafts -----

    private ReplyDraft? OwnedDraft(Guid tenantId, Guid draftId) =>
        _drafts.FirstOrDefault(d => d.Id == draftId && d.TenantId == Asked(tenantId));

    public Task<Result<ReplyDraft>> CreateDraftAsync(Guid tenantId, CreateReplyDraftCommand command, CancellationToken ct = default)
    {
        if (_events.FirstOrDefault(e => e.Id == command.EngagementEventId && e.TenantId == Asked(tenantId)) is not { } ev)
            return NotFound<ReplyDraft>("event");
        var draft = new ReplyDraft
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AccountId = ev.AccountId, EngagementEventId = ev.Id, Platform = ev.Platform,
            Category = command.Category, DraftText = command.DraftText, NeedsHumanReason = command.NeedsHumanReason,
            ReviewRefusal = command.ReviewRefusal, Status = ReplyDraftStatuses.Pending, CreatedAt = Now,
        };
        _drafts.Add(draft);
        return Answer(Result<ReplyDraft>.Success(draft));
    }

    public Task<Result<ReplyDraft>> GetDraftAsync(Guid tenantId, Guid draftId, CancellationToken ct = default) =>
        OwnedDraft(tenantId, draftId) is { } draft ? Answer(Result<ReplyDraft>.Success(draft)) : NotFound<ReplyDraft>("draft");

    public Task<Result<IReadOnlyList<ReplyDraft>>> ListDraftsAsync(Guid tenantId, string? status, Guid? accountId, int limit, CancellationToken ct = default) =>
        Answer(Result<IReadOnlyList<ReplyDraft>>.Success(_drafts
            .Where(d => d.TenantId == Asked(tenantId))
            .Where(d => status is null || d.Status == status)
            .Where(d => accountId is null || d.AccountId == accountId)
            .OrderByDescending(d => d.CreatedAt).Take(limit).ToList()));

    public Task<Result<IReadOnlyList<ReplyDraft>>> ListDraftsAcrossTenantsAsync(string status, int limit, CancellationToken ct = default)
    {
        AcrossTenantCalls.Add(nameof(ListDraftsAcrossTenantsAsync));
        return Answer(Result<IReadOnlyList<ReplyDraft>>.Success(_drafts.Where(d => d.Status == status).Take(limit).ToList()));
    }

    public Task<Result<ReplyDraft>> EditDraftAsync(Guid tenantId, Guid draftId, EditReplyDraftCommand command, CancellationToken ct = default)
    {
        if (OwnedDraft(tenantId, draftId) is not { } draft)
            return NotFound<ReplyDraft>("draft");
        if (draft.Status != ReplyDraftStatuses.Pending)
            return Answer(Result<ReplyDraft>.Failure($"The draft is {draft.Status} and can no longer be edited.", SocialFailureCodes.Conflict));
        draft.DraftText = command.DraftText;
        return Answer(Result<ReplyDraft>.Success(draft));
    }

    public Task<Result<ReplyDraft>> TransitionDraftAsync(Guid tenantId, Guid draftId, TransitionReplyDraftCommand command, CancellationToken ct = default)
    {
        if (OwnedDraft(tenantId, draftId) is not { } draft)
            return NotFound<ReplyDraft>("draft");
        if (draft.Status != command.From)
            return Answer(Result<ReplyDraft>.Failure($"The draft is {draft.Status}, not {command.From}.", SocialFailureCodes.Conflict));
        draft.Status = command.To;
        draft.SentPlatformCommentId = command.SentPlatformCommentId;
        draft.FailureCode = command.FailureCode;
        if (command.To is ReplyDraftStatuses.Approved or ReplyDraftStatuses.Rejected)
            draft.DecidedAt = command.At;
        if (command.To == ReplyDraftStatuses.Sending)
            draft.SendingStartedAt = command.At;
        return Answer(Result<ReplyDraft>.Success(draft));
    }

    // ----- Audit -----

    public Task<Result<ActionAudit>> RecordAuditAsync(Guid tenantId, ActionAudit audit, CancellationToken ct = default)
    {
        _audit.Add((Asked(tenantId), audit));
        return Answer(Result<ActionAudit>.Success(audit));
    }

    public Task<Result<IReadOnlyList<ActionAudit>>> ListAuditAsync(Guid tenantId, Guid? accountId, int limit, CancellationToken ct = default) =>
        Answer(Result<IReadOnlyList<ActionAudit>>.Success(_audit
            .Where(a => a.TenantId == Asked(tenantId) && (accountId is null || a.Audit.AccountId == accountId))
            .Select(a => a.Audit).OrderByDescending(a => a.OccurredAt).Take(limit).ToList()));

    // ----- Status -----

    public Task<Result<SocialStepFailure>> RecordStepFailureAsync(RecordSocialStepFailureCommand command, CancellationToken ct = default)
    {
        FailuresKept.Add(command);
        var failure = new SocialStepFailure { Id = Guid.NewGuid(), TenantId = command.TenantId, Step = command.Step, Code = command.Code, OccurredAt = command.At };
        _failures.Add(failure);
        return Answer(Result<SocialStepFailure>.Success(failure));
    }

    public Task<Result<SocialTenantStatus>> GetTenantStatusAsync(Guid tenantId, int failureLimit, CancellationToken ct = default)
    {
        var comments = _events.Where(e => e.TenantId == Asked(tenantId) && e.Kind == EngagementKinds.CommentCreated).ToList();
        var drafts = _drafts.Where(d => d.TenantId == tenantId).ToList();
        return Answer(Result<SocialTenantStatus>.Success(new SocialTenantStatus
        {
            Comments = new SocialCommentCounts
            {
                Undecided = comments.Count(e => e.TriageOutcome is null),
                ReplyNeeded = comments.Count(e => e.TriageOutcome == TriageOutcomes.ReplyNeeded),
                NoReplyNeeded = comments.Count(e => e.TriageOutcome == TriageOutcomes.NoReplyNeeded),
                BlockedNoClearance = comments.Count(e => e.TriageOutcome == TriageOutcomes.BlockedNoClearance),
                Failed = comments.Count(e => e.TriageOutcome == TriageOutcomes.Failed),
            },
            Drafts = new SocialDraftCounts
            {
                Pending = drafts.Count(d => d.Status == ReplyDraftStatuses.Pending),
                Approved = drafts.Count(d => d.Status == ReplyDraftStatuses.Approved),
                Sending = drafts.Count(d => d.Status == ReplyDraftStatuses.Sending),
                Sent = drafts.Count(d => d.Status == ReplyDraftStatuses.Sent),
                Failed = drafts.Count(d => d.Status == ReplyDraftStatuses.Failed),
                Rejected = drafts.Count(d => d.Status == ReplyDraftStatuses.Rejected),
            },
            Failures = _failures
                .Where(f => f.TenantId == tenantId || f.TenantId is null)
                .OrderByDescending(f => f.OccurredAt).Take(failureLimit).ToList(),
        }));
    }

    // ----- Retention and recovery (platform operations) -----

    public Task<Result<SocialPurgeResult>> PurgeExpiredAsync(DateTime asOf, CancellationToken ct = default)
    {
        AcrossTenantCalls.Add(nameof(PurgeExpiredAsync));
        var scrubbed = 0;
        foreach (var ev in _events.Where(e => e.TextExpiresAt <= asOf && e.Text is not null))
        {
            ev.Text = null;
            ev.AuthorDisplayName = null;
            scrubbed++;
        }
        var deleted = _failures.RemoveAll(f => f.OccurredAt <= asOf - SocialStepFailures.Retention);
        return Answer(Result<SocialPurgeResult>.Success(new SocialPurgeResult { EventsScrubbed = scrubbed, FailuresDeleted = deleted }));
    }

    public Task<Result<SocialInterruptedSends>> FailInterruptedSendsAsync(DateTime at, CancellationToken ct = default)
    {
        AcrossTenantCalls.Add(nameof(FailInterruptedSendsAsync));
        var interrupted = _drafts.Where(d => d.Status == ReplyDraftStatuses.Sending).ToList();
        foreach (var draft in interrupted)
        {
            draft.Status = ReplyDraftStatuses.Failed;
            draft.FailureCode = SocialFailureCodes.OutcomeUnknown;
        }
        return Answer(Result<SocialInterruptedSends>.Success(new SocialInterruptedSends { DraftIds = interrupted.Select(d => d.Id).ToList() }));
    }
}
