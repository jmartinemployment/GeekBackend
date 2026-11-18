extern alias GeekRepo;

using GeekApplication.Models.Social;
using GeekRepo::GeekRepository.Data;
using GeekRepo::GeekRepository.Infrastructure;
using GeekRepo::GeekRepository.Repositories.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GeekBackend.IntegrationTests;

/// <summary>
/// The geek_social script, the EF mapping and the Dapper reads against a real Postgres: the script
/// applies, an event is stored once however often it is delivered, a retention deadline hides text
/// before the purge runs and the purge removes it, a comment is answered once, two callers racing
/// for a draft have one winner, a send never starts from a paused account, and a send a restart
/// interrupted is failed with an unknown outcome and audited. Runs only with TEST_DATABASE_URL set;
/// GeekBackend.Tests covers the vocabularies, the column names and the refusals on every run.
/// </summary>
public sealed class SocialSchemaIntegrationTests
{
    private static readonly DateTime T0 = new(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc);

    private sealed class MovableClock : TimeProvider
    {
        public DateTime Now { get; set; } = T0;

        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private static SocialRepository NewRepository(MovableClock clock)
    {
        var url = IntegrationTestConfig.DatabaseUrl!;
        var options = new DbContextOptionsBuilder<SocialDbContext>().UseNpgsql(url).Options;
        return new SocialRepository(new SocialDbContext(options), new NpgsqlConnectionFactory(url), clock, NullLogger<SocialRepository>.Instance);
    }

    private static readonly Guid OwnerOne = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid OwnerTwo = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly Lazy<Guid> TenantOne = new(() => EnsureTenant(OwnerOne));
    private static readonly Lazy<Guid> TenantTwo = new(() => EnsureTenant(OwnerTwo));

    /// <summary>The tenant every test that does not care about tenants acts for. Made once, with a fixed owner.</summary>
    private static Guid Tenant => TenantOne.Value;

    /// <summary>A second tenant, for the tests that check one cannot reach the other.</summary>
    private static Guid OtherTenant => TenantTwo.Value;

    /// <summary>The tenant of this owner, made if it does not exist yet; a failure is a failed test, never a guess.</summary>
    private static Guid EnsureTenant(Guid owner)
    {
        var repo = NewRepository(new MovableClock());
        var found = repo.GetTenantByOwnerAsync(owner).GetAwaiter().GetResult();
        if (found.IsSuccess) return found.Value!.Id;
        Assert.Equal(SocialFailureCodes.NotFound, found.ErrorCode);

        var created = repo.CreateTenantAsync(new CreateSocialTenantCommand { OwnerUserId = owner, Name = "Integration test tenant" }).GetAwaiter().GetResult();
        Assert.True(created.IsSuccess, created.Error);
        return created.Value!.Id;
    }

    private static SocialEncryptedToken Token() => new() { Cipher = new byte[] { 1, 2, 3, 4 }, Iv = new byte[12], Tag = new byte[16], ExpiresAt = T0.AddDays(60) };

    private static async Task<SocialAccount> ConnectAsync(SocialRepository repo, string platform = SocialPlatforms.LinkedInOrganization)
    {
        var connected = await repo.ConnectAccountAsync(Tenant, new ConnectSocialAccountCommand
        {
            Platform = platform,
            PlatformAccountId = $"test-{Guid.NewGuid():N}",
            DisplayName = "Integration test account",
            Token = Token(),
            ConnectedAt = T0,
        });
        Assert.True(connected.IsSuccess, connected.Error);
        return connected.Value!;
    }

    private static EngagementEvent CommentOn(SocialAccount account, string text = "Hello there") => new()
    {
        Id = Guid.NewGuid(),
        Platform = account.Platform,
        PlatformEventId = $"event-{Guid.NewGuid():N}",
        PlatformAccountId = account.PlatformAccountId,
        Kind = EngagementKinds.CommentCreated,
        PlatformPostId = "post-1",
        PlatformCommentId = $"comment-{Guid.NewGuid():N}",
        AuthorPlatformId = "author-1",
        AuthorDisplayName = "Jane Commenter",
        Text = text,
        OccurredAt = T0,
        ReceivedAt = T0,
    };

    private static async Task<long> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(IntegrationTestConfig.DatabaseUrl);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> SqlStateOfAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(IntegrationTestConfig.DatabaseUrl);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        try
        {
            await cmd.ExecuteNonQueryAsync();
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.SqlState;
        }
    }

    [Fact]
    public async Task The_schema_applies_and_an_account_with_its_token_round_trips()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var repo = NewRepository(new MovableClock());

        var account = await ConnectAsync(repo);
        Assert.Equal(SocialAccountStatuses.Connected, account.Status);
        Assert.False(account.SendsDisabled);

        // Connecting the same platform account again replaces its token and keeps its row.
        var again = await repo.ConnectAccountAsync(Tenant, new ConnectSocialAccountCommand
        {
            Platform = account.Platform,
            PlatformAccountId = account.PlatformAccountId,
            DisplayName = "Renamed",
            Token = Token(),
            ConnectedAt = T0.AddDays(1),
        });
        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(account.Id, again.Value!.Id);
        Assert.Equal("Renamed", again.Value.DisplayName);

        var credential = await repo.GetCredentialAsync(Tenant, account.Id);
        Assert.True(credential.IsSuccess, credential.Error);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, credential.Value!.Token.Cipher);
        Assert.Equal(12, credential.Value.Token.Iv.Length);
        Assert.Equal(16, credential.Value.Token.Tag.Length);
        Assert.Equal(T0.AddDays(60), credential.Value.Token.ExpiresAt);

        var settings = await repo.UpdateSettingsAsync(Tenant, account.Id, new UpdateSocialAccountSettingsCommand
        {
            SendsDisabled = true,
        });
        Assert.True(settings.IsSuccess, settings.Error);
        Assert.True(settings.Value!.SendsDisabled);

        var listed = await repo.ListAccountsAsync(Tenant);
        Assert.True(listed.IsSuccess, listed.Error);
        Assert.Contains(listed.Value!, a => a.Id == account.Id && a.SendsDisabled);

        // The database refuses what the code refuses, for a writer that is not this code. Auto-send has no column
        // to write to since 0072 (undefined_column).
        Assert.Equal("42703", await SqlStateOfAsync(
            "UPDATE geek_social.social_accounts SET auto_send_categories = ARRAY['praise'] WHERE id = @id", ("id", account.Id)));
        Assert.Equal("23514", await SqlStateOfAsync(
            "UPDATE geek_social.social_accounts SET platform = 'myspace' WHERE id = @id", ("id", account.Id)));
        Assert.Equal("23505", await SqlStateOfAsync(
            "INSERT INTO geek_social.social_accounts (id, platform, platform_account_id, display_name, token_cipher, token_iv, token_tag, connected_at) " +
            "VALUES (@id, @p, @a, 'dup', @c, @i, @t, now())",
            ("id", Guid.NewGuid()), ("p", account.Platform), ("a", account.PlatformAccountId),
            ("c", new byte[] { 1 }), ("i", new byte[12]), ("t", new byte[16])));
    }

    [Fact]
    public async Task An_event_is_stored_once_and_its_text_is_hidden_at_its_deadline_and_removed_by_the_purge()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var clock = new MovableClock();
        var repo = NewRepository(clock);
        var account = await ConnectAsync(repo, SocialPlatforms.LinkedInOrganization);
        var evt = CommentOn(account);

        var first = await repo.RecordEventAsync(evt);
        var redelivered = await repo.RecordEventAsync(new EngagementEvent
        {
            Id = Guid.NewGuid(),
            Platform = evt.Platform,
            PlatformEventId = evt.PlatformEventId,
            PlatformAccountId = evt.PlatformAccountId,
            Kind = evt.Kind,
            PlatformCommentId = evt.PlatformCommentId,
            Text = evt.Text,
            AuthorDisplayName = evt.AuthorDisplayName,
            OccurredAt = evt.OccurredAt,
            ReceivedAt = evt.ReceivedAt,
        });
        Assert.True(first.IsSuccess, first.Error);
        Assert.True(first.Value);
        Assert.True(redelivered.IsSuccess, redelivered.Error);
        Assert.False(redelivered.Value);
        Assert.Equal(1, await ScalarAsync(
            "SELECT count(*) FROM geek_social.engagement_events WHERE platform = @p AND platform_event_id = @e",
            ("p", evt.Platform), ("e", evt.PlatformEventId)));

        // Twenty deliveries at once still leave one row.
        var burst = CommentOn(account);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => NewRepository(clock).RecordEventAsync(new EngagementEvent
        {
            Id = Guid.NewGuid(),
            Platform = burst.Platform,
            PlatformEventId = burst.PlatformEventId,
            PlatformAccountId = burst.PlatformAccountId,
            Kind = burst.Kind,
            PlatformCommentId = burst.PlatformCommentId,
            Text = burst.Text,
            OccurredAt = burst.OccurredAt,
            ReceivedAt = burst.ReceivedAt,
        })));
        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Equal(1, results.Count(r => r.Value));
        Assert.Equal(1, await ScalarAsync(
            "SELECT count(*) FROM geek_social.engagement_events WHERE platform_event_id = @e", ("e", burst.PlatformEventId)));

        var fresh = await repo.GetEventAsync(Tenant, evt.Id);
        Assert.True(fresh.IsSuccess, fresh.Error);
        Assert.Equal("Hello there", fresh.Value!.Text);
        Assert.Equal("Jane Commenter", fresh.Value.AuthorDisplayName);
        Assert.Equal(T0.AddHours(48), fresh.Value.TextExpiresAt);
        Assert.Equal(T0.AddHours(24), fresh.Value.AuthorExpiresAt);
        Assert.Equal(account.PlatformAccountId, fresh.Value.PlatformAccountId);

        // Past 24 hours the commenter's name is no longer returned, though the text still is.
        clock.Now = T0.AddHours(25);
        var dayOld = await repo.GetEventAsync(Tenant, evt.Id);
        Assert.Equal("Hello there", dayOld.Value!.Text);
        Assert.Null(dayOld.Value.AuthorDisplayName);

        // Past 48 hours the text is no longer returned, before the purge has touched the row.
        clock.Now = T0.AddHours(49);
        var twoDaysOld = await repo.GetEventAsync(Tenant, evt.Id);
        Assert.Null(twoDaysOld.Value!.Text);
        Assert.Equal(1, await ScalarAsync("SELECT count(*) FROM geek_social.engagement_events WHERE id = @id AND text IS NOT NULL", ("id", evt.Id)));

        var purged = await repo.PurgeExpiredAsync(clock.Now);
        Assert.True(purged.IsSuccess, purged.Error);
        Assert.True(purged.Value!.EventsScrubbed >= 1);
        Assert.Equal(0, await ScalarAsync(
            "SELECT count(*) FROM geek_social.engagement_events WHERE id = @id AND (text IS NOT NULL OR author_display_name IS NOT NULL)", ("id", evt.Id)));

        // An event for an account that is not connected is refused, not stored.
        var stranger = CommentOn(account);
        stranger.PlatformAccountId = "never-connected";
        var refused = await repo.RecordEventAsync(stranger);
        Assert.False(refused.IsSuccess);
        Assert.Equal(SocialFailureCodes.NotFound, refused.ErrorCode);
    }

    [Fact]
    public async Task A_comment_is_answered_once_and_two_callers_racing_for_a_draft_have_one_winner()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var clock = new MovableClock();
        var repo = NewRepository(clock);
        var account = await ConnectAsync(repo, SocialPlatforms.Facebook);
        var evt = CommentOn(account);
        Assert.True((await repo.RecordEventAsync(evt)).IsSuccess);

        var created = await repo.CreateDraftAsync(Tenant, new CreateReplyDraftCommand { EngagementEventId = evt.Id, Category = ReplyCategories.Question, DraftText = "Thanks for asking." });
        Assert.True(created.IsSuccess, created.Error);
        var draft = created.Value!;
        Assert.Equal(ReplyDraftStatuses.Pending, draft.Status);
        Assert.Equal(account.Id, draft.AccountId);
        Assert.Equal(account.Platform, draft.Platform);

        // A second draft for the same comment is refused while the first is live.
        var second = await repo.CreateDraftAsync(Tenant, new CreateReplyDraftCommand { EngagementEventId = evt.Id, Category = ReplyCategories.Question, DraftText = "Another." });
        Assert.Equal(SocialFailureCodes.Conflict, second.ErrorCode);

        var edited = await repo.EditDraftAsync(Tenant, draft.Id, new EditReplyDraftCommand { DraftText = "Thanks for asking, Jane." });
        Assert.True(edited.IsSuccess, edited.Error);
        Assert.Equal("Thanks for asking, Jane.", edited.Value!.DraftText);

        var racers = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => NewRepository(clock).TransitionDraftAsync(Tenant, draft.Id, new TransitionReplyDraftCommand
        {
            From = ReplyDraftStatuses.Pending,
            To = ReplyDraftStatuses.Approved,
            At = T0,
        })));
        Assert.Equal(1, racers.Count(r => r.IsSuccess));
        Assert.All(racers.Where(r => !r.IsSuccess), r => Assert.Equal(SocialFailureCodes.Conflict, r.ErrorCode));

        // An approved draft is no longer editable.
        var lateEdit = await repo.EditDraftAsync(Tenant, draft.Id, new EditReplyDraftCommand { DraftText = "Too late." });
        Assert.Equal(SocialFailureCodes.Conflict, lateEdit.ErrorCode);

        // A paused account does not begin a send, and the draft stays approved.
        await repo.UpdateSettingsAsync(Tenant, account.Id, new UpdateSocialAccountSettingsCommand { SendsDisabled = true });
        var paused = await repo.TransitionDraftAsync(Tenant, draft.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Approved, To = ReplyDraftStatuses.Sending, At = T0 });
        Assert.Equal(SocialFailureCodes.Conflict, paused.ErrorCode);
        Assert.Equal(ReplyDraftStatuses.Approved, (await repo.GetDraftAsync(Tenant, draft.Id)).Value!.Status);

        // Nor does an account that must be reconnected.
        await repo.UpdateSettingsAsync(Tenant, account.Id, new UpdateSocialAccountSettingsCommand { SendsDisabled = false });
        await repo.MarkReconnectRequiredAsync(Tenant, account.Id);
        var needsReconnect = await repo.TransitionDraftAsync(Tenant, draft.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Approved, To = ReplyDraftStatuses.Sending, At = T0 });
        Assert.Equal(SocialFailureCodes.Conflict, needsReconnect.ErrorCode);

        // Reconnected, the send begins, and finishing it records the id the platform returned.
        await repo.StoreTokenAsync(Tenant, account.Id, new StoreSocialTokenCommand { Token = Token() });
        var sending = await repo.TransitionDraftAsync(Tenant, draft.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Approved, To = ReplyDraftStatuses.Sending, At = T0.AddMinutes(1) });
        Assert.True(sending.IsSuccess, sending.Error);
        Assert.Equal(T0.AddMinutes(1), sending.Value!.SendingStartedAt);
        var sent = await repo.TransitionDraftAsync(Tenant, draft.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Sending, To = ReplyDraftStatuses.Sent, SentPlatformCommentId = "platform-comment-9", At = T0.AddMinutes(2) });
        Assert.True(sent.IsSuccess, sent.Error);
        Assert.Equal("platform-comment-9", sent.Value!.SentPlatformCommentId);

        // A sent comment stays answered.
        var again = await repo.CreateDraftAsync(Tenant, new CreateReplyDraftCommand { EngagementEventId = evt.Id, Category = ReplyCategories.Question, DraftText = "Again." });
        Assert.Equal(SocialFailureCodes.Conflict, again.ErrorCode);

        // The database refuses a sent draft with no platform id, for a writer that is not this code.
        Assert.Equal("23514", await SqlStateOfAsync(
            "UPDATE geek_social.reply_drafts SET sent_platform_comment_id = NULL WHERE id = @id", ("id", draft.Id)));
    }

    [Fact]
    public async Task A_rejected_draft_frees_the_comment_and_a_send_a_restart_interrupted_is_failed_audited_and_still_holds_it()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var clock = new MovableClock();
        var repo = NewRepository(clock);
        var account = await ConnectAsync(repo, SocialPlatforms.Facebook);

        var rejectedEvent = CommentOn(account);
        Assert.True((await repo.RecordEventAsync(rejectedEvent)).IsSuccess);
        var first = (await repo.CreateDraftAsync(Tenant, new CreateReplyDraftCommand { EngagementEventId = rejectedEvent.Id, Category = ReplyCategories.Praise, DraftText = "Thank you." })).Value!;
        var rejected = await repo.TransitionDraftAsync(Tenant, first.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Pending, To = ReplyDraftStatuses.Rejected, At = T0 });
        Assert.True(rejected.IsSuccess, rejected.Error);
        Assert.Equal(T0, rejected.Value!.DecidedAt);
        var replacement = await repo.CreateDraftAsync(Tenant, new CreateReplyDraftCommand { EngagementEventId = rejectedEvent.Id, Category = ReplyCategories.Praise, DraftText = "Thanks so much." });
        Assert.True(replacement.IsSuccess, replacement.Error);

        var interruptedEvent = CommentOn(account);
        Assert.True((await repo.RecordEventAsync(interruptedEvent)).IsSuccess);
        var draft = (await repo.CreateDraftAsync(Tenant, new CreateReplyDraftCommand { EngagementEventId = interruptedEvent.Id, Category = ReplyCategories.Question, DraftText = "Here is the answer." })).Value!;
        Assert.True((await repo.TransitionDraftAsync(Tenant, draft.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Pending, To = ReplyDraftStatuses.Approved, At = T0 })).IsSuccess);
        Assert.True((await repo.TransitionDraftAsync(Tenant, draft.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Approved, To = ReplyDraftStatuses.Sending, At = T0 })).IsSuccess);

        var recovered = await repo.FailInterruptedSendsAsync(T0.AddMinutes(10));
        Assert.True(recovered.IsSuccess, recovered.Error);
        Assert.Contains(draft.Id, recovered.Value!.DraftIds);

        var failed = (await repo.GetDraftAsync(Tenant, draft.Id)).Value!;
        Assert.Equal(ReplyDraftStatuses.Failed, failed.Status);
        Assert.Equal(SocialFailureCodes.OutcomeUnknown, failed.FailureCode);

        var audit = await repo.ListAuditAsync(Tenant, account.Id, 50);
        Assert.True(audit.IsSuccess, audit.Error);
        var row = Assert.Single(audit.Value!, a => a.ReplyDraftId == draft.Id);
        Assert.Equal(ActionOutcomes.OutcomeUnknown, row.Outcome);
        Assert.Equal(SocialFailureCodes.OutcomeUnknown, row.FailureCode);

        // The platform may have posted it, so the comment is still held and nothing resends it.
        var blocked = await repo.CreateDraftAsync(Tenant, new CreateReplyDraftCommand { EngagementEventId = interruptedEvent.Id, Category = ReplyCategories.Question, DraftText = "Again." });
        Assert.Equal(SocialFailureCodes.Conflict, blocked.ErrorCode);

        // A second recovery finds nothing left to fail.
        var none = await repo.FailInterruptedSendsAsync(T0.AddMinutes(20));
        Assert.DoesNotContain(draft.Id, none.Value!.DraftIds);

        var post = await repo.RecordPostAsync(Tenant, new RecordSocialPostCommand { AccountId = account.Id, PlatformPostId = $"post-{Guid.NewGuid():N}", Kind = SocialPostKinds.Link, Text = "New article", LinkUrl = "https://geekatyourspot.com/blog/x", PublishedAt = T0 });
        Assert.True(post.IsSuccess, post.Error);
        var posts = await repo.ListPostsAsync(Tenant, account.Id, 10);
        Assert.Contains(posts.Value!, p => p.Id == post.Value!.Id && p.LinkUrl == "https://geekatyourspot.com/blog/x");
    }

    [Fact]
    public async Task Triage_decides_a_comment_once_and_recovery_lists_only_the_comments_nobody_has_decided()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var clock = new MovableClock();
        var repo = NewRepository(clock);
        var account = await ConnectAsync(repo);

        var comment = CommentOn(account);
        Assert.True((await repo.RecordEventAsync(comment)).IsSuccess);
        var reaction = CommentOn(account);
        reaction.Kind = EngagementKinds.ReactionAdded;
        reaction.PlatformCommentId = null;
        reaction.ReactionType = SocialReactionTypes.Like;
        reaction.Text = null;
        Assert.True((await repo.RecordEventAsync(reaction)).IsSuccess);

        // Recovery after a restart finds the comment, oldest first, and not the reaction.
        var untriaged = await repo.ListEventsAsync(Tenant, TriageOutcomes.Untriaged, account.Id, 50);
        Assert.True(untriaged.IsSuccess, untriaged.Error);
        Assert.Contains(untriaged.Value!, e => e.Id == comment.Id && e.TriageOutcome is null);
        Assert.DoesNotContain(untriaged.Value!, e => e.Id == reaction.Id);

        // Only a comment is triaged.
        var notAComment = await repo.RecordTriageOutcomeAsync(Tenant, reaction.Id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.NoReplyNeeded, At = T0 });
        Assert.Equal(SocialFailureCodes.InvalidRequest, notAComment.ErrorCode);

        // A failure keeps its code, shows in the operator's list, and an operator's re-run replaces it.
        var failed = await repo.RecordTriageOutcomeAsync(Tenant, comment.Id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.Failed, FailureCode = SocialFailureCodes.PlatformUnavailable, At = T0 });
        Assert.True(failed.IsSuccess, failed.Error);
        Assert.Equal(SocialFailureCodes.PlatformUnavailable, failed.Value!.TriageFailureCode);
        Assert.Equal(T0, failed.Value.TriagedAt);
        var failedList = await repo.ListEventsAsync(Tenant, TriageOutcomes.Failed, account.Id, 50);
        Assert.Contains(failedList.Value!, e => e.Id == comment.Id);
        Assert.DoesNotContain((await repo.ListEventsAsync(Tenant, TriageOutcomes.Untriaged, account.Id, 50)).Value!, e => e.Id == comment.Id);

        var rerun = await repo.RecordTriageOutcomeAsync(Tenant, comment.Id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.ReplyNeeded, At = T0.AddMinutes(1) });
        Assert.True(rerun.IsSuccess, rerun.Error);
        Assert.Null(rerun.Value!.TriageFailureCode);

        // A decided event is never decided twice, however the second caller got there.
        var twice = await repo.RecordTriageOutcomeAsync(Tenant, comment.Id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.NoReplyNeeded, At = T0.AddMinutes(2) });
        Assert.Equal(SocialFailureCodes.Conflict, twice.ErrorCode);
        Assert.Equal(TriageOutcomes.ReplyNeeded, (await repo.GetEventAsync(Tenant, comment.Id)).Value!.TriageOutcome);

        // The database refuses a failure code on an event that did not fail, and an outcome on a reaction.
        var other = CommentOn(account);
        Assert.True((await repo.RecordEventAsync(other)).IsSuccess);
        Assert.Equal("23514", await SqlStateOfAsync(
            "UPDATE geek_social.engagement_events SET triage_failure_code = 'store_unavailable' WHERE id = @id", ("id", other.Id)));
        Assert.Equal("23514", await SqlStateOfAsync(
            "UPDATE geek_social.engagement_events SET triage_outcome = 'no_reply_needed', triaged_at = now() WHERE id = @id", ("id", reaction.Id)));
    }

    [Fact]
    public async Task A_draft_the_reviewer_refused_is_kept_rejected_with_its_rules_and_frees_the_comment()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var clock = new MovableClock();
        var repo = NewRepository(clock);
        var account = await ConnectAsync(repo, SocialPlatforms.Facebook);
        var comment = CommentOn(account);
        Assert.True((await repo.RecordEventAsync(comment)).IsSuccess);

        var refused = await repo.CreateDraftAsync(Tenant, new CreateReplyDraftCommand
        {
            EngagementEventId = comment.Id,
            Category = ReplyCategories.Question,
            DraftText = "That is a good point, and I agree.",
            ReviewRefusal = new List<string> { ReviewRules.Smb, ReviewRules.EmDash },
        });
        Assert.True(refused.IsSuccess, refused.Error);
        Assert.Equal(ReplyDraftStatuses.Rejected, refused.Value!.Status);
        Assert.Equal(T0, refused.Value.DecidedAt);

        // Read back through Dapper: text[] arrives as the rules, in order, and a passed draft carries none.
        var read = (await repo.GetDraftAsync(Tenant, refused.Value.Id)).Value!;
        Assert.Equal(new[] { ReviewRules.EmDash, ReviewRules.Smb }, read.ReviewRefusal);
        Assert.Contains((await repo.ListDraftsAsync(Tenant, ReplyDraftStatuses.Rejected, account.Id, 50)).Value!, d => d.Id == refused.Value.Id && d.ReviewRefusal is { Count: 2 });

        // A refused draft holds nothing: a new draft for the same comment can be made, and nothing moves the refused one.
        var replacement = await repo.CreateDraftAsync(Tenant, new CreateReplyDraftCommand { EngagementEventId = comment.Id, Category = ReplyCategories.Question, DraftText = "Thanks for the question." });
        Assert.True(replacement.IsSuccess, replacement.Error);
        Assert.Null(replacement.Value!.ReviewRefusal);
        var moved = await repo.TransitionDraftAsync(Tenant, refused.Value.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Rejected, To = ReplyDraftStatuses.Approved, At = T0 });
        Assert.Equal(SocialFailureCodes.InvalidRequest, moved.ErrorCode);

        // The database refuses a rule it does not know, and a refused draft that is not rejected.
        Assert.Equal("23514", await SqlStateOfAsync(
            "UPDATE geek_social.reply_drafts SET review_refusal = ARRAY['swearing'] WHERE id = @id", ("id", refused.Value.Id)));
        Assert.Equal("23514", await SqlStateOfAsync(
            "UPDATE geek_social.reply_drafts SET status = 'pending' WHERE id = @id", ("id", refused.Value.Id)));
    }

    [Fact]
    public async Task One_tenant_cannot_read_list_change_or_send_anything_of_another()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var repo = NewRepository(new MovableClock());

        var account = await ConnectAsync(repo);
        var evt = CommentOn(account);
        Assert.True((await repo.RecordEventAsync(evt)).IsSuccess);
        var draft = (await repo.CreateDraftAsync(Tenant, new CreateReplyDraftCommand
            { EngagementEventId = evt.Id, Category = ReplyCategories.Question, DraftText = "Thanks for asking." })).Value!;
        var post = (await repo.RecordPostAsync(Tenant, new RecordSocialPostCommand
            { AccountId = account.Id, PlatformPostId = $"post-{Guid.NewGuid():N}", Kind = SocialPostKinds.Link, Text = "A post", LinkUrl = "https://example.com" })).Value!;
        Assert.True((await repo.RecordAuditAsync(Tenant, new ActionAudit
            { Platform = account.Platform, AccountId = account.Id, Action = ActionKinds.Publish, Outcome = ActionOutcomes.Succeeded, OccurredAt = T0 })).IsSuccess);

        // Every way of reaching the first tenant's rows, asked as the other tenant, answers not found or an empty list.
        Assert.Equal(SocialFailureCodes.NotFound, (await repo.GetAccountAsync(OtherTenant, account.Id)).ErrorCode);
        Assert.Equal(SocialFailureCodes.NotFound, (await repo.GetCredentialAsync(OtherTenant, account.Id)).ErrorCode);
        Assert.Equal(SocialFailureCodes.NotFound, (await repo.StoreTokenAsync(OtherTenant, account.Id, new StoreSocialTokenCommand { Token = Token() })).ErrorCode);
        Assert.Equal(SocialFailureCodes.NotFound, (await repo.MarkReconnectRequiredAsync(OtherTenant, account.Id)).ErrorCode);
        Assert.Equal(SocialFailureCodes.NotFound, (await repo.UpdateSettingsAsync(OtherTenant, account.Id, new UpdateSocialAccountSettingsCommand { SendsDisabled = true })).ErrorCode);
        Assert.DoesNotContain((await repo.ListAccountsAsync(OtherTenant)).Value!, a => a.Id == account.Id);

        Assert.Equal(SocialFailureCodes.NotFound, (await repo.GetEventAsync(OtherTenant, evt.Id)).ErrorCode);
        Assert.DoesNotContain((await repo.ListEventsAsync(OtherTenant, TriageOutcomes.Untriaged, null, 200)).Value!, e => e.Id == evt.Id);
        Assert.Equal(SocialFailureCodes.NotFound, (await repo.RecordTriageOutcomeAsync(OtherTenant, evt.Id,
            new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.NoReplyNeeded, At = T0 })).ErrorCode);

        Assert.Equal(SocialFailureCodes.NotFound, (await repo.GetDraftAsync(OtherTenant, draft.Id)).ErrorCode);
        Assert.DoesNotContain((await repo.ListDraftsAsync(OtherTenant, null, null, 200)).Value!, d => d.Id == draft.Id);
        Assert.Equal(SocialFailureCodes.NotFound, (await repo.EditDraftAsync(OtherTenant, draft.Id, new EditReplyDraftCommand { DraftText = "Changed." })).ErrorCode);
        Assert.Equal(SocialFailureCodes.NotFound, (await repo.TransitionDraftAsync(OtherTenant, draft.Id,
            new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Pending, To = ReplyDraftStatuses.Approved, At = T0 })).ErrorCode);
        Assert.Equal(SocialFailureCodes.NotFound, (await repo.CreateDraftAsync(OtherTenant, new CreateReplyDraftCommand
            { EngagementEventId = evt.Id, Category = ReplyCategories.Question, DraftText = "Mine now." })).ErrorCode);

        Assert.Empty((await repo.ListPostsAsync(OtherTenant, account.Id, 50)).Value!);
        Assert.Equal(SocialFailureCodes.NotFound, (await repo.RecordPostAsync(OtherTenant, new RecordSocialPostCommand
            { AccountId = account.Id, PlatformPostId = "x", Kind = SocialPostKinds.Link, Text = "t", LinkUrl = "https://example.com" })).ErrorCode);
        Assert.DoesNotContain((await repo.ListAuditAsync(OtherTenant, account.Id, 200)).Value!, a => a.AccountId == account.Id);

        // Nothing the other tenant tried changed the first tenant's draft or account.
        Assert.Equal(ReplyDraftStatuses.Pending, (await repo.GetDraftAsync(Tenant, draft.Id)).Value!.Status);
        Assert.Equal("Thanks for asking.", (await repo.GetDraftAsync(Tenant, draft.Id)).Value!.DraftText);
        Assert.False((await repo.GetAccountAsync(Tenant, account.Id)).Value!.SendsDisabled);
        Assert.Contains((await repo.ListPostsAsync(Tenant, account.Id, 50)).Value!, p => p.Id == post.Id);
    }

    [Fact]
    public async Task An_account_belongs_to_one_tenant_and_another_cannot_take_it_over()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var repo = NewRepository(new MovableClock());
        var account = await ConnectAsync(repo);

        var taken = await repo.ConnectAccountAsync(OtherTenant, new ConnectSocialAccountCommand
        {
            Platform = account.Platform,
            PlatformAccountId = account.PlatformAccountId,
            DisplayName = "Not mine",
            Token = Token(),
            ConnectedAt = T0,
        });

        Assert.Equal(SocialFailureCodes.Conflict, taken.ErrorCode);
        var still = (await repo.GetAccountAsync(Tenant, account.Id)).Value!;
        Assert.Equal(Tenant, still.TenantId);
        Assert.Equal("Integration test account", still.DisplayName);
    }

    [Fact]
    public async Task An_event_is_stored_for_the_tenant_that_owns_the_account_it_names()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var repo = NewRepository(new MovableClock());
        var account = await ConnectAsync(repo);
        var evt = CommentOn(account);
        Assert.True((await repo.RecordEventAsync(evt)).IsSuccess);

        var owner = await repo.GetEventAcrossTenantsAsync(evt.Id);
        Assert.Equal(Tenant, owner.Value!.TenantId);
        Assert.Contains((await repo.ListUntriagedEventsAsync(200)).Value!, e => e.Id == evt.Id && e.TenantId == Tenant);
    }

    [Fact]
    public async Task A_person_has_one_tenant_and_the_ai_terms_keep_the_first_acceptance()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var repo = NewRepository(new MovableClock());
        var owner = Guid.NewGuid();

        Assert.Equal(SocialFailureCodes.NotFound, (await repo.GetTenantByOwnerAsync(owner)).ErrorCode);
        var created = (await repo.CreateTenantAsync(new CreateSocialTenantCommand { OwnerUserId = owner, Name = "Acme" })).Value!;
        Assert.Null(created.AiConsentAcceptedAt);
        Assert.Equal(SocialFailureCodes.Conflict, (await repo.CreateTenantAsync(new CreateSocialTenantCommand { OwnerUserId = owner, Name = "Acme again" })).ErrorCode);

        var first = (await repo.AcceptAiTermsAsync(created.Id, T0)).Value!;
        var second = (await repo.AcceptAiTermsAsync(created.Id, T0.AddDays(3))).Value!;
        Assert.Equal(T0, first.AiConsentAcceptedAt);
        Assert.Equal(T0, second.AiConsentAcceptedAt);
        Assert.Equal(SocialFailureCodes.NotFound, (await repo.AcceptAiTermsAsync(Guid.NewGuid(), T0)).ErrorCode);
    }

    [Fact]
    public async Task A_tenant_status_counts_only_its_own_rows_and_lists_its_failures_and_those_of_no_tenant()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var repo = NewRepository(new MovableClock());

        // Earlier runs left rows in these tenants, so the counts are compared before and after.
        var before = (await repo.GetTenantStatusAsync(Tenant, 50)).Value!;
        var otherBefore = (await repo.GetTenantStatusAsync(OtherTenant, 50)).Value!;

        var account = await ConnectAsync(repo);
        var undecided = CommentOn(account);
        var decided = CommentOn(account);
        Assert.True((await repo.RecordEventAsync(undecided)).IsSuccess);
        Assert.True((await repo.RecordEventAsync(decided)).IsSuccess);
        Assert.True((await repo.RecordTriageOutcomeAsync(Tenant, decided.Id,
            new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.ReplyNeeded, At = T0 })).IsSuccess);
        Assert.True((await repo.CreateDraftAsync(Tenant, new CreateReplyDraftCommand
            { EngagementEventId = decided.Id, Category = ReplyCategories.Question, DraftText = "Thanks for asking." })).IsSuccess);

        var after = (await repo.GetTenantStatusAsync(Tenant, 50)).Value!;
        Assert.Equal(before.Comments.Undecided + 1, after.Comments.Undecided);
        Assert.Equal(before.Comments.ReplyNeeded + 1, after.Comments.ReplyNeeded);
        Assert.Equal(before.Drafts.Pending + 1, after.Drafts.Pending);

        var otherAfter = (await repo.GetTenantStatusAsync(OtherTenant, 50)).Value!;
        Assert.Equal(otherBefore.Comments.Undecided, otherAfter.Comments.Undecided);
        Assert.Equal(otherBefore.Drafts.Pending, otherAfter.Drafts.Pending);

        // Kept failures: dated after every earlier run's, so they are the newest and inside the limit.
        var mark = Guid.NewGuid().ToString("N")[..8];
        var at = DateTime.UtcNow;
        foreach (var (tenant, step) in new[] { ((Guid?)Tenant, $"mine {mark}"), (OtherTenant, $"theirs {mark}"), (null, $"everyone {mark}") })
        {
            var kept = await repo.RecordStepFailureAsync(new RecordSocialStepFailureCommand
                { TenantId = tenant, Step = step, Code = SocialFailureCodes.StoreUnavailable, At = at });
            Assert.True(kept.IsSuccess, kept.Error);
        }

        var steps = (await repo.GetTenantStatusAsync(Tenant, 50)).Value!.Failures.Select(f => f.Step).ToList();
        Assert.Contains($"mine {mark}", steps);
        Assert.Contains($"everyone {mark}", steps);
        Assert.DoesNotContain($"theirs {mark}", steps);
    }

    [Fact]
    public async Task The_purge_deletes_kept_failures_older_than_thirty_days_and_keeps_the_rest()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var repo = NewRepository(new MovableClock());

        var mark = Guid.NewGuid().ToString("N")[..8];
        Assert.True((await repo.RecordStepFailureAsync(new RecordSocialStepFailureCommand
            { TenantId = Tenant, Step = $"old {mark}", Code = SocialFailureCodes.StoreUnavailable, At = T0.AddDays(-31) })).IsSuccess);
        Assert.True((await repo.RecordStepFailureAsync(new RecordSocialStepFailureCommand
            { TenantId = Tenant, Step = $"recent {mark}", Code = SocialFailureCodes.StoreUnavailable, At = T0.AddDays(-29) })).IsSuccess);

        var purged = await repo.PurgeExpiredAsync(T0);

        Assert.True(purged.IsSuccess, purged.Error);
        Assert.True(purged.Value!.FailuresDeleted >= 1);
        Assert.Equal(0, await ScalarAsync("SELECT count(*) FROM geek_social.step_failures WHERE step = @step", ("step", $"old {mark}")));
        Assert.Equal(1, await ScalarAsync("SELECT count(*) FROM geek_social.step_failures WHERE step = @step", ("step", $"recent {mark}")));
    }
}
