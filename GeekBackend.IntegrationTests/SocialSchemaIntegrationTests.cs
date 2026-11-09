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

    private static SocialEncryptedToken Token() => new() { Cipher = new byte[] { 1, 2, 3, 4 }, Iv = new byte[12], Tag = new byte[16], ExpiresAt = T0.AddDays(60) };

    private static async Task<SocialAccount> ConnectAsync(SocialRepository repo, string platform = SocialPlatforms.LinkedInOrganization)
    {
        var connected = await repo.ConnectAccountAsync(new ConnectSocialAccountCommand
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
        Assert.Empty(account.AutoSendCategories);
        Assert.False(account.SendsDisabled);

        // Connecting the same platform account again replaces its token and keeps its row.
        var again = await repo.ConnectAccountAsync(new ConnectSocialAccountCommand
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

        var credential = await repo.GetCredentialAsync(account.Id);
        Assert.True(credential.IsSuccess, credential.Error);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, credential.Value!.Token.Cipher);
        Assert.Equal(12, credential.Value.Token.Iv.Length);
        Assert.Equal(16, credential.Value.Token.Tag.Length);
        Assert.Equal(T0.AddDays(60), credential.Value.Token.ExpiresAt);

        var settings = await repo.UpdateSettingsAsync(account.Id, new UpdateSocialAccountSettingsCommand
        {
            AutoSendCategories = new List<string> { ReplyCategories.Praise, ReplyCategories.Question },
            SendsDisabled = true,
        });
        Assert.True(settings.IsSuccess, settings.Error);
        Assert.Equal(new[] { ReplyCategories.Praise, ReplyCategories.Question }, settings.Value!.AutoSendCategories);
        Assert.True(settings.Value.SendsDisabled);

        var listed = await repo.ListAccountsAsync();
        Assert.True(listed.IsSuccess, listed.Error);
        Assert.Contains(listed.Value!, a => a.Id == account.Id && a.SendsDisabled);

        // The database refuses what the code refuses, for a writer that is not this code.
        Assert.Equal("23514", await SqlStateOfAsync(
            "UPDATE geek_social.social_accounts SET auto_send_categories = ARRAY['complaint'] WHERE id = @id", ("id", account.Id)));
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

        var fresh = await repo.GetEventAsync(evt.Id);
        Assert.True(fresh.IsSuccess, fresh.Error);
        Assert.Equal("Hello there", fresh.Value!.Text);
        Assert.Equal("Jane Commenter", fresh.Value.AuthorDisplayName);
        Assert.Equal(T0.AddHours(48), fresh.Value.TextExpiresAt);
        Assert.Equal(T0.AddHours(24), fresh.Value.AuthorExpiresAt);
        Assert.Equal(account.PlatformAccountId, fresh.Value.PlatformAccountId);

        // Past 24 hours the commenter's name is no longer returned, though the text still is.
        clock.Now = T0.AddHours(25);
        var dayOld = await repo.GetEventAsync(evt.Id);
        Assert.Equal("Hello there", dayOld.Value!.Text);
        Assert.Null(dayOld.Value.AuthorDisplayName);

        // Past 48 hours the text is no longer returned, before the purge has touched the row.
        clock.Now = T0.AddHours(49);
        var twoDaysOld = await repo.GetEventAsync(evt.Id);
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

        var created = await repo.CreateDraftAsync(new CreateReplyDraftCommand { EngagementEventId = evt.Id, Category = ReplyCategories.Question, DraftText = "Thanks for asking." });
        Assert.True(created.IsSuccess, created.Error);
        var draft = created.Value!;
        Assert.Equal(ReplyDraftStatuses.Pending, draft.Status);
        Assert.Equal(account.Id, draft.AccountId);
        Assert.Equal(account.Platform, draft.Platform);

        // A second draft for the same comment is refused while the first is live.
        var second = await repo.CreateDraftAsync(new CreateReplyDraftCommand { EngagementEventId = evt.Id, Category = ReplyCategories.Question, DraftText = "Another." });
        Assert.Equal(SocialFailureCodes.Conflict, second.ErrorCode);

        var edited = await repo.EditDraftAsync(draft.Id, new EditReplyDraftCommand { DraftText = "Thanks for asking, Jane." });
        Assert.True(edited.IsSuccess, edited.Error);
        Assert.Equal("Thanks for asking, Jane.", edited.Value!.DraftText);

        var racers = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => NewRepository(clock).TransitionDraftAsync(draft.Id, new TransitionReplyDraftCommand
        {
            From = ReplyDraftStatuses.Pending,
            To = ReplyDraftStatuses.Approved,
            At = T0,
        })));
        Assert.Equal(1, racers.Count(r => r.IsSuccess));
        Assert.All(racers.Where(r => !r.IsSuccess), r => Assert.Equal(SocialFailureCodes.Conflict, r.ErrorCode));

        // An approved draft is no longer editable.
        var lateEdit = await repo.EditDraftAsync(draft.Id, new EditReplyDraftCommand { DraftText = "Too late." });
        Assert.Equal(SocialFailureCodes.Conflict, lateEdit.ErrorCode);

        // A paused account does not begin a send, and the draft stays approved.
        await repo.UpdateSettingsAsync(account.Id, new UpdateSocialAccountSettingsCommand { SendsDisabled = true });
        var paused = await repo.TransitionDraftAsync(draft.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Approved, To = ReplyDraftStatuses.Sending, At = T0 });
        Assert.Equal(SocialFailureCodes.Conflict, paused.ErrorCode);
        Assert.Equal(ReplyDraftStatuses.Approved, (await repo.GetDraftAsync(draft.Id)).Value!.Status);

        // Nor does an account that must be reconnected.
        await repo.UpdateSettingsAsync(account.Id, new UpdateSocialAccountSettingsCommand { SendsDisabled = false });
        await repo.MarkReconnectRequiredAsync(account.Id);
        var needsReconnect = await repo.TransitionDraftAsync(draft.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Approved, To = ReplyDraftStatuses.Sending, At = T0 });
        Assert.Equal(SocialFailureCodes.Conflict, needsReconnect.ErrorCode);

        // Reconnected, the send begins, and finishing it records the id the platform returned.
        await repo.StoreTokenAsync(account.Id, new StoreSocialTokenCommand { Token = Token() });
        var sending = await repo.TransitionDraftAsync(draft.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Approved, To = ReplyDraftStatuses.Sending, At = T0.AddMinutes(1) });
        Assert.True(sending.IsSuccess, sending.Error);
        Assert.Equal(T0.AddMinutes(1), sending.Value!.SendingStartedAt);
        var sent = await repo.TransitionDraftAsync(draft.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Sending, To = ReplyDraftStatuses.Sent, SentPlatformCommentId = "platform-comment-9", At = T0.AddMinutes(2) });
        Assert.True(sent.IsSuccess, sent.Error);
        Assert.Equal("platform-comment-9", sent.Value!.SentPlatformCommentId);

        // A sent comment stays answered.
        var again = await repo.CreateDraftAsync(new CreateReplyDraftCommand { EngagementEventId = evt.Id, Category = ReplyCategories.Question, DraftText = "Again." });
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
        var first = (await repo.CreateDraftAsync(new CreateReplyDraftCommand { EngagementEventId = rejectedEvent.Id, Category = ReplyCategories.Praise, DraftText = "Thank you." })).Value!;
        var rejected = await repo.TransitionDraftAsync(first.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Pending, To = ReplyDraftStatuses.Rejected, At = T0 });
        Assert.True(rejected.IsSuccess, rejected.Error);
        Assert.Equal(T0, rejected.Value!.DecidedAt);
        var replacement = await repo.CreateDraftAsync(new CreateReplyDraftCommand { EngagementEventId = rejectedEvent.Id, Category = ReplyCategories.Praise, DraftText = "Thanks so much." });
        Assert.True(replacement.IsSuccess, replacement.Error);

        var interruptedEvent = CommentOn(account);
        Assert.True((await repo.RecordEventAsync(interruptedEvent)).IsSuccess);
        var draft = (await repo.CreateDraftAsync(new CreateReplyDraftCommand { EngagementEventId = interruptedEvent.Id, Category = ReplyCategories.Question, DraftText = "Here is the answer." })).Value!;
        Assert.True((await repo.TransitionDraftAsync(draft.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Pending, To = ReplyDraftStatuses.Approved, At = T0 })).IsSuccess);
        Assert.True((await repo.TransitionDraftAsync(draft.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Approved, To = ReplyDraftStatuses.Sending, At = T0 })).IsSuccess);

        var recovered = await repo.FailInterruptedSendsAsync(T0.AddMinutes(10));
        Assert.True(recovered.IsSuccess, recovered.Error);
        Assert.Contains(draft.Id, recovered.Value!.DraftIds);

        var failed = (await repo.GetDraftAsync(draft.Id)).Value!;
        Assert.Equal(ReplyDraftStatuses.Failed, failed.Status);
        Assert.Equal(SocialFailureCodes.OutcomeUnknown, failed.FailureCode);

        var audit = await repo.ListAuditAsync(account.Id, 50);
        Assert.True(audit.IsSuccess, audit.Error);
        var row = Assert.Single(audit.Value!, a => a.ReplyDraftId == draft.Id);
        Assert.Equal(ActionOutcomes.OutcomeUnknown, row.Outcome);
        Assert.Equal(SocialFailureCodes.OutcomeUnknown, row.FailureCode);

        // The platform may have posted it, so the comment is still held and nothing resends it.
        var blocked = await repo.CreateDraftAsync(new CreateReplyDraftCommand { EngagementEventId = interruptedEvent.Id, Category = ReplyCategories.Question, DraftText = "Again." });
        Assert.Equal(SocialFailureCodes.Conflict, blocked.ErrorCode);

        // A second recovery finds nothing left to fail.
        var none = await repo.FailInterruptedSendsAsync(T0.AddMinutes(20));
        Assert.DoesNotContain(draft.Id, none.Value!.DraftIds);

        var post = await repo.RecordPostAsync(new RecordSocialPostCommand { AccountId = account.Id, PlatformPostId = $"post-{Guid.NewGuid():N}", Kind = SocialPostKinds.Link, Text = "New article", LinkUrl = "https://geekatyourspot.com/blog/x", PublishedAt = T0 });
        Assert.True(post.IsSuccess, post.Error);
        var posts = await repo.ListPostsAsync(account.Id, 10);
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
        var untriaged = await repo.ListEventsAsync(TriageOutcomes.Untriaged, account.Id, 50);
        Assert.True(untriaged.IsSuccess, untriaged.Error);
        Assert.Contains(untriaged.Value!, e => e.Id == comment.Id && e.TriageOutcome is null);
        Assert.DoesNotContain(untriaged.Value!, e => e.Id == reaction.Id);

        // Only a comment is triaged.
        var notAComment = await repo.RecordTriageOutcomeAsync(reaction.Id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.NoReplyNeeded, At = T0 });
        Assert.Equal(SocialFailureCodes.InvalidRequest, notAComment.ErrorCode);

        // A failure keeps its code, shows in the operator's list, and an operator's re-run replaces it.
        var failed = await repo.RecordTriageOutcomeAsync(comment.Id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.Failed, FailureCode = SocialFailureCodes.PlatformUnavailable, At = T0 });
        Assert.True(failed.IsSuccess, failed.Error);
        Assert.Equal(SocialFailureCodes.PlatformUnavailable, failed.Value!.TriageFailureCode);
        Assert.Equal(T0, failed.Value.TriagedAt);
        var failedList = await repo.ListEventsAsync(TriageOutcomes.Failed, account.Id, 50);
        Assert.Contains(failedList.Value!, e => e.Id == comment.Id);
        Assert.DoesNotContain((await repo.ListEventsAsync(TriageOutcomes.Untriaged, account.Id, 50)).Value!, e => e.Id == comment.Id);

        var rerun = await repo.RecordTriageOutcomeAsync(comment.Id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.ReplyNeeded, At = T0.AddMinutes(1) });
        Assert.True(rerun.IsSuccess, rerun.Error);
        Assert.Null(rerun.Value!.TriageFailureCode);

        // A decided event is never decided twice, however the second caller got there.
        var twice = await repo.RecordTriageOutcomeAsync(comment.Id, new RecordTriageOutcomeCommand { Outcome = TriageOutcomes.NoReplyNeeded, At = T0.AddMinutes(2) });
        Assert.Equal(SocialFailureCodes.Conflict, twice.ErrorCode);
        Assert.Equal(TriageOutcomes.ReplyNeeded, (await repo.GetEventAsync(comment.Id)).Value!.TriageOutcome);

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

        var refused = await repo.CreateDraftAsync(new CreateReplyDraftCommand
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
        var read = (await repo.GetDraftAsync(refused.Value.Id)).Value!;
        Assert.Equal(new[] { ReviewRules.EmDash, ReviewRules.Smb }, read.ReviewRefusal);
        Assert.Contains((await repo.ListDraftsAsync(ReplyDraftStatuses.Rejected, account.Id, 50)).Value!, d => d.Id == refused.Value.Id && d.ReviewRefusal is { Count: 2 });

        // A refused draft holds nothing: a new draft for the same comment can be made, and nothing moves the refused one.
        var replacement = await repo.CreateDraftAsync(new CreateReplyDraftCommand { EngagementEventId = comment.Id, Category = ReplyCategories.Question, DraftText = "Thanks for the question." });
        Assert.True(replacement.IsSuccess, replacement.Error);
        Assert.Null(replacement.Value!.ReviewRefusal);
        var moved = await repo.TransitionDraftAsync(refused.Value.Id, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Rejected, To = ReplyDraftStatuses.Approved, At = T0 });
        Assert.Equal(SocialFailureCodes.InvalidRequest, moved.ErrorCode);

        // The database refuses a rule it does not know, and a refused draft that is not rejected.
        Assert.Equal("23514", await SqlStateOfAsync(
            "UPDATE geek_social.reply_drafts SET review_refusal = ARRAY['swearing'] WHERE id = @id", ("id", refused.Value.Id)));
        Assert.Equal("23514", await SqlStateOfAsync(
            "UPDATE geek_social.reply_drafts SET status = 'pending' WHERE id = @id", ("id", refused.Value.Id)));
    }
}
