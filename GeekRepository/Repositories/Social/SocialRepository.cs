using System.Data;
using Dapper;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using GeekRepository.Data;
using GeekRepository.Data.Entities.Social;
using GeekRepository.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeekRepository.Repositories.Social;

/// <summary>
/// geek_social in GeekRepository's own Postgres. Writes go through <see cref="SocialDbContext"/>;
/// reads are Dapper queries that name each column. Every method answers a Result and nothing throws
/// past its boundary: a database failure is a failed Result with a <see cref="SocialFailureCodes"/>
/// value, logged by operation and never with a row's content.
/// </summary>
/// <remarks>
/// The rules the database enforces, so two callers cannot break them by racing: a draft's state
/// moves only from the state the caller read (a compare-and-set update), a comment holds at most one
/// answer (a partial unique index), an event is stored once however often it is delivered (a unique
/// key and ON CONFLICT DO NOTHING), and a draft cannot begin sending from an account whose kill
/// switch is on or that needs reconnecting. A comment's text and a commenter's name are never read
/// back past their retention deadline, whether or not the purge has run.
/// </remarks>
public sealed class SocialRepository : ISocialStore
{
    public const int DefaultListLimit = 50;
    public const int MaxListLimit = 200;

    private const string AccountColumns = """
        id AS Id,
        platform AS Platform,
        platform_account_id AS PlatformAccountId,
        display_name AS DisplayName,
        status AS Status,
        auto_send_categories AS AutoSendCategories,
        sends_disabled AS SendsDisabled,
        token_expires_at AS TokenExpiresAt,
        connected_at AS ConnectedAt
        """;

    private const string PostColumns = """
        id AS Id,
        account_id AS AccountId,
        platform AS Platform,
        platform_post_id AS PlatformPostId,
        kind AS Kind,
        text AS Text,
        link_url AS LinkUrl,
        scheduled_for AS ScheduledFor,
        published_at AS PublishedAt,
        created_at AS CreatedAt
        """;

    // The text and the author's name come back only while their deadline is ahead.
    private const string EventSelect = """
        SELECT e.id AS Id,
               e.account_id AS AccountId,
               e.platform AS Platform,
               e.platform_event_id AS PlatformEventId,
               a.platform_account_id AS PlatformAccountId,
               e.kind AS Kind,
               e.platform_post_id AS PlatformPostId,
               e.platform_comment_id AS PlatformCommentId,
               e.parent_platform_comment_id AS ParentPlatformCommentId,
               e.author_platform_id AS AuthorPlatformId,
               CASE WHEN e.author_expires_at IS NULL OR e.author_expires_at > @now THEN e.author_display_name END AS AuthorDisplayName,
               CASE WHEN e.text_expires_at IS NULL OR e.text_expires_at > @now THEN e.text END AS Text,
               e.reaction_type AS ReactionType,
               e.occurred_at AS OccurredAt,
               e.received_at AS ReceivedAt,
               e.text_expires_at AS TextExpiresAt,
               e.author_expires_at AS AuthorExpiresAt
        FROM geek_social.engagement_events e
        INNER JOIN geek_social.social_accounts a ON a.id = e.account_id
        """;

    private const string DraftColumns = """
        id AS Id,
        account_id AS AccountId,
        engagement_event_id AS EngagementEventId,
        platform AS Platform,
        category AS Category,
        draft_text AS DraftText,
        needs_human_reason AS NeedsHumanReason,
        status AS Status,
        sent_platform_comment_id AS SentPlatformCommentId,
        failure_code AS FailureCode,
        created_at AS CreatedAt,
        decided_at AS DecidedAt,
        sending_started_at AS SendingStartedAt
        """;

    private const string AuditColumns = """
        id AS Id,
        account_id AS AccountId,
        platform AS Platform,
        action AS Action,
        outcome AS Outcome,
        platform_object_id AS PlatformObjectId,
        reply_draft_id AS ReplyDraftId,
        failure_code AS FailureCode,
        failure_detail AS FailureDetail,
        actor_user_id AS ActorUserId,
        occurred_at AS OccurredAt
        """;

    /// <summary>The categories an account may be set to send without approval: every category a person need not read first.</summary>
    private static readonly string[] AutoSendable =
        ReplyCategories.All.Where(c => !ReplyCategories.NeverAutoSend.Contains(c)).ToArray();

    private readonly SocialDbContext _db;
    private readonly IDbConnectionFactory _connections;
    private readonly TimeProvider _time;
    private readonly ILogger<SocialRepository> _logger;

    public SocialRepository(
        SocialDbContext db,
        IDbConnectionFactory connections,
        TimeProvider time,
        ILogger<SocialRepository> logger)
    {
        _db = db;
        _connections = connections;
        _time = time;
        _logger = logger;
    }

    // ----- Accounts -----

    public Task<Result<SocialAccount>> ConnectAccountAsync(ConnectSocialAccountCommand command, CancellationToken ct = default) =>
        GuardAsync("connect an account", async () =>
        {
            if (command is null) return Invalid<SocialAccount>("An account is required.");
            if (!SocialPlatforms.IsKnown(command.Platform)) return Invalid<SocialAccount>($"Unknown platform '{command.Platform}'.");
            if (string.IsNullOrWhiteSpace(command.PlatformAccountId)) return Invalid<SocialAccount>("platformAccountId is required.");
            if (string.IsNullOrWhiteSpace(command.DisplayName)) return Invalid<SocialAccount>("displayName is required.");
            if (TokenProblem(command.Token) is { } tokenProblem) return Invalid<SocialAccount>(tokenProblem);

            var row = await _db.Accounts
                .FirstOrDefaultAsync(a => a.Platform == command.Platform && a.PlatformAccountId == command.PlatformAccountId, ct)
                .ConfigureAwait(false);

            if (row is null)
            {
                row = new SocialAccountRow
                {
                    Id = Guid.NewGuid(),
                    Platform = command.Platform,
                    PlatformAccountId = command.PlatformAccountId.Trim(),
                };
                _db.Accounts.Add(row);
            }
            else
            {
                row.Version++;
            }

            row.DisplayName = command.DisplayName.Trim();
            row.Status = SocialAccountStatuses.Connected;
            ApplyToken(row, command.Token);
            row.ConnectedAt = SocialTime.Normalize(command.ConnectedAt);

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<SocialAccount>.Success(ToAccount(row));
        }, ct);

    public Task<Result<IReadOnlyList<SocialAccount>>> ListAccountsAsync(CancellationToken ct = default) =>
        GuardAsync("list accounts", async () =>
        {
            using var conn = await OpenAsync(ct).ConfigureAwait(false);
            var rows = await conn.QueryAsync<AccountReadRow>(new CommandDefinition(
                $"SELECT {AccountColumns} FROM geek_social.social_accounts ORDER BY display_name, platform", cancellationToken: ct)).ConfigureAwait(false);
            return Result<IReadOnlyList<SocialAccount>>.Success(rows.Select(r => r.ToModel()).ToList());
        }, ct);

    public Task<Result<SocialAccount>> GetAccountAsync(Guid accountId, CancellationToken ct = default) =>
        GuardAsync("get an account", async () =>
        {
            var account = await ReadAccountAsync(accountId, ct).ConfigureAwait(false);
            return account is null ? NotFound<SocialAccount>("No such account.") : Result<SocialAccount>.Success(account);
        }, ct);

    public Task<Result<SocialAccountCredential>> GetCredentialAsync(Guid accountId, CancellationToken ct = default) =>
        GuardAsync("get a credential", async () =>
        {
            using var conn = await OpenAsync(ct).ConfigureAwait(false);
            var row = await conn.QuerySingleOrDefaultAsync<CredentialReadRow>(new CommandDefinition(
                """
                SELECT id AS Id, token_cipher AS TokenCipher, token_iv AS TokenIv, token_tag AS TokenTag, token_expires_at AS TokenExpiresAt
                FROM geek_social.social_accounts WHERE id = @accountId
                """, new { accountId }, cancellationToken: ct)).ConfigureAwait(false);

            return row is null
                ? NotFound<SocialAccountCredential>("No such account.")
                : Result<SocialAccountCredential>.Success(new SocialAccountCredential
                {
                    AccountId = row.Id,
                    Token = new SocialEncryptedToken
                    {
                        Cipher = row.TokenCipher,
                        Iv = row.TokenIv,
                        Tag = row.TokenTag,
                        ExpiresAt = row.TokenExpiresAt,
                    },
                });
        }, ct);

    public Task<Result<SocialAccount>> StoreTokenAsync(Guid accountId, StoreSocialTokenCommand command, CancellationToken ct = default) =>
        GuardAsync("store a token", async () =>
        {
            if (command is null) return Invalid<SocialAccount>("A token is required.");
            if (TokenProblem(command.Token) is { } tokenProblem) return Invalid<SocialAccount>(tokenProblem);

            var row = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct).ConfigureAwait(false);
            if (row is null) return NotFound<SocialAccount>("No such account.");

            ApplyToken(row, command.Token);
            row.Status = SocialAccountStatuses.Connected;
            row.Version++;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<SocialAccount>.Success(ToAccount(row));
        }, ct);

    public Task<Result<SocialAccount>> MarkReconnectRequiredAsync(Guid accountId, CancellationToken ct = default) =>
        GuardAsync("mark an account for reconnection", async () =>
        {
            var row = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct).ConfigureAwait(false);
            if (row is null) return NotFound<SocialAccount>("No such account.");

            row.Status = SocialAccountStatuses.ReconnectRequired;
            row.Version++;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<SocialAccount>.Success(ToAccount(row));
        }, ct);

    public Task<Result<SocialAccount>> UpdateSettingsAsync(Guid accountId, UpdateSocialAccountSettingsCommand command, CancellationToken ct = default) =>
        GuardAsync("update account settings", async () =>
        {
            if (command is null) return Invalid<SocialAccount>("Settings are required.");

            var categories = (command.AutoSendCategories ?? new List<string>()).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
            foreach (var category in categories)
            {
                if (ReplyCategories.NeverAutoSend.Contains(category))
                    return Invalid<SocialAccount>($"'{category}' replies are never sent without a person.");
                if (!AutoSendable.Contains(category))
                    return Invalid<SocialAccount>($"Unknown category '{category}'.");
            }

            var row = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct).ConfigureAwait(false);
            if (row is null) return NotFound<SocialAccount>("No such account.");

            row.AutoSendCategories = categories;
            row.SendsDisabled = command.SendsDisabled;
            row.Version++;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<SocialAccount>.Success(ToAccount(row));
        }, ct);

    // ----- Posts -----

    public Task<Result<SocialPost>> RecordPostAsync(RecordSocialPostCommand command, CancellationToken ct = default) =>
        GuardAsync("record a post", async () =>
        {
            if (command is null) return Invalid<SocialPost>("A post is required.");
            if (command.AccountId == Guid.Empty) return Invalid<SocialPost>("accountId is required.");
            if (string.IsNullOrWhiteSpace(command.PlatformPostId)) return Invalid<SocialPost>("platformPostId is required.");
            if (!SocialPostKinds.All.Contains(command.Kind)) return Invalid<SocialPost>($"Unknown post kind '{command.Kind}'.");

            var platform = await _db.Accounts.AsNoTracking()
                .Where(a => a.Id == command.AccountId)
                .Select(a => a.Platform)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (platform is null) return NotFound<SocialPost>("No such account.");

            var row = new SocialPostRow
            {
                Id = Guid.NewGuid(),
                AccountId = command.AccountId,
                Platform = platform,
                PlatformPostId = command.PlatformPostId.Trim(),
                Kind = command.Kind,
                Text = command.Text ?? string.Empty,
                LinkUrl = command.LinkUrl,
                ScheduledFor = command.ScheduledFor is { } s ? SocialTime.Normalize(s) : null,
                PublishedAt = command.PublishedAt is { } p ? SocialTime.Normalize(p) : null,
                CreatedAt = SocialTime.Normalize(_time.GetUtcNow().UtcDateTime),
            };
            _db.Posts.Add(row);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<SocialPost>.Success(ToPost(row));
        }, ct);

    public Task<Result<IReadOnlyList<SocialPost>>> ListPostsAsync(Guid accountId, int limit, CancellationToken ct = default) =>
        GuardAsync("list posts", async () =>
        {
            using var conn = await OpenAsync(ct).ConfigureAwait(false);
            var rows = await conn.QueryAsync<SocialPost>(new CommandDefinition(
                $"SELECT {PostColumns} FROM geek_social.social_posts WHERE account_id = @accountId ORDER BY created_at DESC LIMIT @limit",
                new { accountId, limit = ClampLimit(limit) }, cancellationToken: ct)).ConfigureAwait(false);
            return Result<IReadOnlyList<SocialPost>>.Success(rows.ToList());
        }, ct);

    // ----- Engagement events -----

    public Task<Result<bool>> RecordEventAsync(EngagementEvent engagementEvent, CancellationToken ct = default) =>
        GuardAsync("record an event", async () =>
        {
            if (engagementEvent is null) return Invalid<bool>("An event is required.");
            if (!SocialPlatforms.IsKnown(engagementEvent.Platform)) return Invalid<bool>($"Unknown platform '{engagementEvent.Platform}'.");
            if (!EngagementKinds.IsKnown(engagementEvent.Kind)) return Invalid<bool>($"Unknown event kind '{engagementEvent.Kind}'.");
            if (string.IsNullOrWhiteSpace(engagementEvent.PlatformEventId)) return Invalid<bool>("platformEventId is required.");
            if (string.IsNullOrWhiteSpace(engagementEvent.PlatformAccountId)) return Invalid<bool>("platformAccountId is required.");
            if (engagementEvent.OccurredAt == default || engagementEvent.ReceivedAt == default)
                return Invalid<bool>("occurredAt and receivedAt are required.");
            if (engagementEvent.Kind.StartsWith("comment_", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(engagementEvent.PlatformCommentId))
                return Invalid<bool>("A comment event needs platformCommentId.");
            if (engagementEvent.Kind.StartsWith("reaction_", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(engagementEvent.ReactionType))
                return Invalid<bool>("A reaction event needs reactionType.");

            var accountId = await _db.Accounts.AsNoTracking()
                .Where(a => a.Platform == engagementEvent.Platform && a.PlatformAccountId == engagementEvent.PlatformAccountId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (accountId is null)
                return NotFound<bool>($"No connected {engagementEvent.Platform} account matches the event's account.");

            var received = SocialTime.Normalize(engagementEvent.ReceivedAt);
            var occurred = SocialTime.Normalize(engagementEvent.OccurredAt);
            var capabilities = SocialPlatformCapabilities.For(engagementEvent.Platform);
            DateTime? textExpires = capabilities.CommentTextRetention is { } textRetention && !string.IsNullOrEmpty(engagementEvent.Text)
                ? received + textRetention
                : null;
            DateTime? authorExpires = capabilities.CommenterProfileRetention is { } profileRetention && !string.IsNullOrEmpty(engagementEvent.AuthorDisplayName)
                ? received + profileRetention
                : null;
            var id = engagementEvent.Id == Guid.Empty ? Guid.NewGuid() : engagementEvent.Id;

            var inserted = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO geek_social.engagement_events (
                    id, account_id, platform, platform_event_id, kind, platform_post_id, platform_comment_id,
                    parent_platform_comment_id, author_platform_id, author_display_name, text, reaction_type,
                    occurred_at, received_at, text_expires_at, author_expires_at)
                VALUES (
                    {id}, {accountId.Value}, {engagementEvent.Platform}, {engagementEvent.PlatformEventId}, {engagementEvent.Kind},
                    {engagementEvent.PlatformPostId}, {engagementEvent.PlatformCommentId}, {engagementEvent.ParentPlatformCommentId},
                    {engagementEvent.AuthorPlatformId}, {engagementEvent.AuthorDisplayName}, {engagementEvent.Text}, {engagementEvent.ReactionType},
                    {occurred}, {received}, {textExpires}, {authorExpires})
                ON CONFLICT (platform, platform_event_id) DO NOTHING
                """, ct).ConfigureAwait(false);

            return Result<bool>.Success(inserted == 1);
        }, ct);

    public Task<Result<EngagementEvent>> GetEventAsync(Guid eventId, CancellationToken ct = default) =>
        GuardAsync("get an event", async () =>
        {
            using var conn = await OpenAsync(ct).ConfigureAwait(false);
            var row = await conn.QuerySingleOrDefaultAsync<EngagementEvent>(new CommandDefinition(
                $"{EventSelect} WHERE e.id = @eventId", new { eventId, now = Now() }, cancellationToken: ct)).ConfigureAwait(false);
            return row is null ? NotFound<EngagementEvent>("No such event.") : Result<EngagementEvent>.Success(row);
        }, ct);

    // ----- Reply drafts -----

    public Task<Result<ReplyDraft>> CreateDraftAsync(CreateReplyDraftCommand command, CancellationToken ct = default) =>
        GuardAsync("create a draft", async () =>
        {
            if (command is null) return Invalid<ReplyDraft>("A draft is required.");
            if (command.EngagementEventId == Guid.Empty) return Invalid<ReplyDraft>("engagementEventId is required.");
            if (!ReplyCategories.IsKnown(command.Category)) return Invalid<ReplyDraft>($"Unknown category '{command.Category}'.");
            if (string.IsNullOrWhiteSpace(command.DraftText) && string.IsNullOrWhiteSpace(command.NeedsHumanReason))
                return Invalid<ReplyDraft>("A draft needs text, or the reason a person must answer.");

            var source = await _db.Events.AsNoTracking()
                .Where(e => e.Id == command.EngagementEventId)
                .Select(e => new { e.AccountId, e.Platform })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (source is null) return NotFound<ReplyDraft>("No such event.");

            var row = new ReplyDraftRow
            {
                Id = Guid.NewGuid(),
                AccountId = source.AccountId,
                EngagementEventId = command.EngagementEventId,
                Platform = source.Platform,
                Category = command.Category,
                DraftText = command.DraftText ?? string.Empty,
                NeedsHumanReason = string.IsNullOrWhiteSpace(command.NeedsHumanReason) ? null : command.NeedsHumanReason.Trim(),
                Status = ReplyDraftStatuses.Pending,
                CreatedAt = SocialTime.Normalize(_time.GetUtcNow().UtcDateTime),
            };
            _db.Drafts.Add(row);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<ReplyDraft>.Success(ToDraft(row));
        }, ct);

    public Task<Result<ReplyDraft>> GetDraftAsync(Guid draftId, CancellationToken ct = default) =>
        GuardAsync("get a draft", async () =>
        {
            var draft = await ReadDraftAsync(draftId, ct).ConfigureAwait(false);
            return draft is null ? NotFound<ReplyDraft>("No such draft.") : Result<ReplyDraft>.Success(draft);
        }, ct);

    public Task<Result<IReadOnlyList<ReplyDraft>>> ListDraftsAsync(string? status, Guid? accountId, int limit, CancellationToken ct = default) =>
        GuardAsync("list drafts", async () =>
        {
            if (status is not null && !ReplyDraftStatuses.IsKnown(status))
                return Invalid<IReadOnlyList<ReplyDraft>>($"Unknown status '{status}'.");

            using var conn = await OpenAsync(ct).ConfigureAwait(false);
            var rows = await conn.QueryAsync<ReplyDraft>(new CommandDefinition(
                $"""
                SELECT {DraftColumns} FROM geek_social.reply_drafts
                WHERE (@status::text IS NULL OR status = @status)
                  AND (@accountId::uuid IS NULL OR account_id = @accountId)
                ORDER BY created_at DESC
                LIMIT @limit
                """,
                new { status, accountId, limit = ClampLimit(limit) }, cancellationToken: ct)).ConfigureAwait(false);
            return Result<IReadOnlyList<ReplyDraft>>.Success(rows.ToList());
        }, ct);

    public Task<Result<ReplyDraft>> EditDraftAsync(Guid draftId, EditReplyDraftCommand command, CancellationToken ct = default) =>
        GuardAsync("edit a draft", async () =>
        {
            if (command is null || string.IsNullOrWhiteSpace(command.DraftText)) return Invalid<ReplyDraft>("draftText is required.");

            var text = command.DraftText;
            var changed = await _db.Drafts
                .Where(d => d.Id == draftId && d.Status == ReplyDraftStatuses.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.DraftText, text), ct).ConfigureAwait(false);

            if (changed == 0)
            {
                var current = await ReadDraftAsync(draftId, ct).ConfigureAwait(false);
                return current is null
                    ? NotFound<ReplyDraft>("No such draft.")
                    : Conflict<ReplyDraft>($"The draft is {current.Status} and can no longer be edited.");
            }

            var edited = await ReadDraftAsync(draftId, ct).ConfigureAwait(false);
            return edited is null ? NotFound<ReplyDraft>("No such draft.") : Result<ReplyDraft>.Success(edited);
        }, ct);

    public Task<Result<ReplyDraft>> TransitionDraftAsync(Guid draftId, TransitionReplyDraftCommand command, CancellationToken ct = default) =>
        GuardAsync("move a draft", async () =>
        {
            if (command is null) return Invalid<ReplyDraft>("A transition is required.");
            if (!ReplyDraftStatuses.IsKnown(command.From) || !ReplyDraftStatuses.IsKnown(command.To))
                return Invalid<ReplyDraft>("from and to must be draft statuses.");
            if (!ReplyDraftStatuses.CanTransition(command.From, command.To))
                return Invalid<ReplyDraft>($"A draft cannot move from {command.From} to {command.To}.");
            if (command.To == ReplyDraftStatuses.Sent && string.IsNullOrWhiteSpace(command.SentPlatformCommentId))
                return Invalid<ReplyDraft>("A sent draft needs sentPlatformCommentId, the id the platform returned.");
            if (command.To != ReplyDraftStatuses.Sent && command.SentPlatformCommentId is not null)
                return Invalid<ReplyDraft>("sentPlatformCommentId belongs only to a sent draft.");
            if (command.To == ReplyDraftStatuses.Failed && !SocialFailureCodes.IsKnown(command.FailureCode))
                return Invalid<ReplyDraft>("A failed draft needs a known failureCode.");
            if (command.To != ReplyDraftStatuses.Failed && command.FailureCode is not null)
                return Invalid<ReplyDraft>("failureCode belongs only to a failed draft.");
            if (command.At == default) return Invalid<ReplyDraft>("at is required.");

            var at = SocialTime.Normalize(command.At);
            var to = command.To;
            var sentId = command.SentPlatformCommentId;
            var failure = command.FailureCode;
            DateTime? decidedAt = to is ReplyDraftStatuses.Approved or ReplyDraftStatuses.Rejected ? at : null;
            DateTime? startedAt = to == ReplyDraftStatuses.Sending ? at : null;

            var query = _db.Drafts.Where(d => d.Id == draftId && d.Status == command.From);
            if (to == ReplyDraftStatuses.Sending)
            {
                // A send never begins from an account that is paused or must be reconnected.
                query = query.Where(d => _db.Accounts.Any(a =>
                    a.Id == d.AccountId && !a.SendsDisabled && a.Status == SocialAccountStatuses.Connected));
            }

            var changed = await query.ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, to)
                .SetProperty(d => d.SentPlatformCommentId, sentId)
                .SetProperty(d => d.FailureCode, failure)
                .SetProperty(d => d.DecidedAt, d => decidedAt ?? d.DecidedAt)
                .SetProperty(d => d.SendingStartedAt, d => startedAt ?? d.SendingStartedAt), ct).ConfigureAwait(false);

            if (changed == 0)
            {
                var current = await ReadDraftAsync(draftId, ct).ConfigureAwait(false);
                if (current is null) return NotFound<ReplyDraft>("No such draft.");
                if (current.Status == command.From && to == ReplyDraftStatuses.Sending)
                    return Conflict<ReplyDraft>("Sends are paused for this account, or it must be reconnected first.");
                return Conflict<ReplyDraft>($"The draft is {current.Status}, not {command.From}.");
            }

            var moved = await ReadDraftAsync(draftId, ct).ConfigureAwait(false);
            return moved is null ? NotFound<ReplyDraft>("No such draft.") : Result<ReplyDraft>.Success(moved);
        }, ct);

    // ----- Audit -----

    public Task<Result<ActionAudit>> RecordAuditAsync(ActionAudit audit, CancellationToken ct = default) =>
        GuardAsync("record an audit row", async () =>
        {
            if (audit is null) return Invalid<ActionAudit>("An audit row is required.");
            if (!SocialPlatforms.IsKnown(audit.Platform)) return Invalid<ActionAudit>($"Unknown platform '{audit.Platform}'.");
            if (!ActionKinds.All.Contains(audit.Action)) return Invalid<ActionAudit>($"Unknown action '{audit.Action}'.");
            if (!ActionOutcomes.All.Contains(audit.Outcome)) return Invalid<ActionAudit>($"Unknown outcome '{audit.Outcome}'.");
            if (audit.Outcome != ActionOutcomes.Succeeded && !SocialFailureCodes.IsKnown(audit.FailureCode))
                return Invalid<ActionAudit>("A failed or unknown outcome needs a known failureCode.");
            if (audit.OccurredAt == default) return Invalid<ActionAudit>("occurredAt is required.");

            var row = new ActionAuditRow
            {
                Id = audit.Id == Guid.Empty ? Guid.NewGuid() : audit.Id,
                AccountId = audit.AccountId,
                Platform = audit.Platform,
                Action = audit.Action,
                Outcome = audit.Outcome,
                PlatformObjectId = audit.PlatformObjectId,
                ReplyDraftId = audit.ReplyDraftId,
                FailureCode = audit.FailureCode,
                FailureDetail = audit.FailureDetail,
                ActorUserId = audit.ActorUserId,
                OccurredAt = SocialTime.Normalize(audit.OccurredAt),
            };
            _db.Audit.Add(row);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<ActionAudit>.Success(ToAudit(row));
        }, ct);

    public Task<Result<IReadOnlyList<ActionAudit>>> ListAuditAsync(Guid? accountId, int limit, CancellationToken ct = default) =>
        GuardAsync("list audit rows", async () =>
        {
            using var conn = await OpenAsync(ct).ConfigureAwait(false);
            var rows = await conn.QueryAsync<ActionAudit>(new CommandDefinition(
                $"""
                SELECT {AuditColumns} FROM geek_social.action_audit
                WHERE (@accountId::uuid IS NULL OR account_id = @accountId)
                ORDER BY occurred_at DESC
                LIMIT @limit
                """,
                new { accountId, limit = ClampLimit(limit) }, cancellationToken: ct)).ConfigureAwait(false);
            return Result<IReadOnlyList<ActionAudit>>.Success(rows.ToList());
        }, ct);

    // ----- Retention and recovery -----

    public Task<Result<SocialPurgeResult>> PurgeExpiredAsync(DateTime asOf, CancellationToken ct = default) =>
        GuardAsync("purge expired events", async () =>
        {
            if (asOf == default) return Invalid<SocialPurgeResult>("asOf is required.");

            var cutoff = SocialTime.Normalize(asOf);
            var scrubbed = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE geek_social.engagement_events
                SET text = CASE WHEN text_expires_at IS NOT NULL AND text_expires_at <= {cutoff} THEN NULL ELSE text END,
                    author_display_name = CASE WHEN author_expires_at IS NOT NULL AND author_expires_at <= {cutoff} THEN NULL ELSE author_display_name END
                WHERE (text IS NOT NULL AND text_expires_at IS NOT NULL AND text_expires_at <= {cutoff})
                   OR (author_display_name IS NOT NULL AND author_expires_at IS NOT NULL AND author_expires_at <= {cutoff})
                """, ct).ConfigureAwait(false);

            return Result<SocialPurgeResult>.Success(new SocialPurgeResult { EventsScrubbed = scrubbed });
        }, ct);

    public Task<Result<SocialInterruptedSends>> FailInterruptedSendsAsync(DateTime at, CancellationToken ct = default) =>
        GuardAsync("fail interrupted sends", async () =>
        {
            if (at == default) return Invalid<SocialInterruptedSends>("at is required.");

            var cutoff = SocialTime.Normalize(at);
            var candidates = await _db.Drafts.AsNoTracking()
                .Where(d => d.Status == ReplyDraftStatuses.Sending && (d.SendingStartedAt == null || d.SendingStartedAt < cutoff))
                .Select(d => new { d.Id, d.AccountId, d.Platform })
                .ToListAsync(ct).ConfigureAwait(false);

            var failed = new List<Guid>();
            await using var transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            foreach (var candidate in candidates)
            {
                var id = candidate.Id;
                var changed = await _db.Drafts
                    .Where(d => d.Id == id && d.Status == ReplyDraftStatuses.Sending)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(d => d.Status, ReplyDraftStatuses.Failed)
                        .SetProperty(d => d.FailureCode, SocialFailureCodes.OutcomeUnknown), ct).ConfigureAwait(false);
                if (changed != 1) continue;

                _db.Audit.Add(new ActionAuditRow
                {
                    Id = Guid.NewGuid(),
                    AccountId = candidate.AccountId,
                    Platform = candidate.Platform,
                    Action = ActionKinds.Reply,
                    Outcome = ActionOutcomes.OutcomeUnknown,
                    ReplyDraftId = id,
                    FailureCode = SocialFailureCodes.OutcomeUnknown,
                    FailureDetail = "The service restarted while this reply was being sent. Check the platform before sending again; it is never resent automatically.",
                    OccurredAt = cutoff,
                });
                failed.Add(id);
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return Result<SocialInterruptedSends>.Success(new SocialInterruptedSends { DraftIds = failed });
        }, ct);

    // ----- Reads and helpers -----

    private async Task<SocialAccount?> ReadAccountAsync(Guid accountId, CancellationToken ct)
    {
        using var conn = await OpenAsync(ct).ConfigureAwait(false);
        var row = await conn.QuerySingleOrDefaultAsync<AccountReadRow>(new CommandDefinition(
            $"SELECT {AccountColumns} FROM geek_social.social_accounts WHERE id = @accountId",
            new { accountId }, cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToModel();
    }

    private async Task<ReplyDraft?> ReadDraftAsync(Guid draftId, CancellationToken ct)
    {
        using var conn = await OpenAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<ReplyDraft>(new CommandDefinition(
            $"SELECT {DraftColumns} FROM geek_social.reply_drafts WHERE id = @draftId",
            new { draftId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task<IDbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = _connections.CreateConnection();
        try
        {
            if (connection is NpgsqlConnection npgsql)
                await npgsql.OpenAsync(ct).ConfigureAwait(false);
            else
                connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private static int ClampLimit(int limit) => limit <= 0 ? DefaultListLimit : Math.Min(limit, MaxListLimit);

    /// <summary>The token's shape, or null when it is acceptable: AES-GCM output has a 12-byte nonce and a 16-byte tag.</summary>
    private static string? TokenProblem(SocialEncryptedToken? token)
    {
        if (token is null || token.Cipher.Length == 0) return "A token is required.";
        if (token.Iv.Length != 12) return "The token's nonce must be 12 bytes.";
        if (token.Tag.Length != 16) return "The token's tag must be 16 bytes.";
        return null;
    }

    private static void ApplyToken(SocialAccountRow row, SocialEncryptedToken token)
    {
        row.TokenCipher = token.Cipher;
        row.TokenIv = token.Iv;
        row.TokenTag = token.Tag;
        row.TokenExpiresAt = token.ExpiresAt is { } expires ? SocialTime.Normalize(expires) : null;
    }

    private static Result<T> Invalid<T>(string message) => Result<T>.Failure(message, SocialFailureCodes.InvalidRequest);

    private static Result<T> NotFound<T>(string message) => Result<T>.Failure(message, SocialFailureCodes.NotFound);

    private static Result<T> Conflict<T>(string message) => Result<T>.Failure(message, SocialFailureCodes.Conflict);

    /// <summary>
    /// Runs one store operation and turns what the database can throw into a failed Result. A lost race
    /// and a unique-key refusal are conflicts the caller reads; anything else is logged by operation
    /// name, with the exception and never a row's content, and answered as an unavailable store.
    /// </summary>
    private async Task<Result<T>> GuardAsync<T>(string operation, Func<Task<Result<T>>> body, CancellationToken ct)
    {
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict<T>("The record changed while it was being saved. Read it again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict<T>("The store already holds that record.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GeekSocial store failed while trying to {Operation}.", operation);
            return Result<T>.Failure("The social store failed.", SocialFailureCodes.StoreUnavailable);
        }
    }

    private static SocialAccount ToAccount(SocialAccountRow row) => new()
    {
        Id = row.Id,
        Platform = row.Platform,
        PlatformAccountId = row.PlatformAccountId,
        DisplayName = row.DisplayName,
        Status = row.Status,
        AutoSendCategories = row.AutoSendCategories.ToList(),
        SendsDisabled = row.SendsDisabled,
        TokenExpiresAt = row.TokenExpiresAt,
        ConnectedAt = row.ConnectedAt,
    };

    private static SocialPost ToPost(SocialPostRow row) => new()
    {
        Id = row.Id,
        AccountId = row.AccountId,
        Platform = row.Platform,
        PlatformPostId = row.PlatformPostId,
        Kind = row.Kind,
        Text = row.Text,
        LinkUrl = row.LinkUrl,
        ScheduledFor = row.ScheduledFor,
        PublishedAt = row.PublishedAt,
        CreatedAt = row.CreatedAt,
    };

    private static ReplyDraft ToDraft(ReplyDraftRow row) => new()
    {
        Id = row.Id,
        AccountId = row.AccountId,
        EngagementEventId = row.EngagementEventId,
        Platform = row.Platform,
        Category = row.Category,
        DraftText = row.DraftText,
        NeedsHumanReason = row.NeedsHumanReason,
        Status = row.Status,
        SentPlatformCommentId = row.SentPlatformCommentId,
        FailureCode = row.FailureCode,
        CreatedAt = row.CreatedAt,
        DecidedAt = row.DecidedAt,
        SendingStartedAt = row.SendingStartedAt,
    };

    private static ActionAudit ToAudit(ActionAuditRow row) => new()
    {
        Id = row.Id,
        AccountId = row.AccountId,
        Platform = row.Platform,
        Action = row.Action,
        Outcome = row.Outcome,
        PlatformObjectId = row.PlatformObjectId,
        ReplyDraftId = row.ReplyDraftId,
        FailureCode = row.FailureCode,
        FailureDetail = row.FailureDetail,
        ActorUserId = row.ActorUserId,
        OccurredAt = row.OccurredAt,
    };

    /// <summary>An account as Dapper reads it: Postgres gives text[] as string[].</summary>
    private sealed class AccountReadRow
    {
        public Guid Id { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string PlatformAccountId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string[] AutoSendCategories { get; set; } = Array.Empty<string>();
        public bool SendsDisabled { get; set; }
        public DateTime? TokenExpiresAt { get; set; }
        public DateTime ConnectedAt { get; set; }

        public SocialAccount ToModel() => new()
        {
            Id = Id,
            Platform = Platform,
            PlatformAccountId = PlatformAccountId,
            DisplayName = DisplayName,
            Status = Status,
            AutoSendCategories = AutoSendCategories.ToList(),
            SendsDisabled = SendsDisabled,
            TokenExpiresAt = TokenExpiresAt,
            ConnectedAt = ConnectedAt,
        };
    }

    private sealed class CredentialReadRow
    {
        public Guid Id { get; set; }
        public byte[] TokenCipher { get; set; } = Array.Empty<byte>();
        public byte[] TokenIv { get; set; } = Array.Empty<byte>();
        public byte[] TokenTag { get; set; } = Array.Empty<byte>();
        public DateTime? TokenExpiresAt { get; set; }
    }
}
