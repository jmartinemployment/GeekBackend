using GeekAPI.Services.Social.Orchestration;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.Listener;

/// <summary>What one resync read and stored. Counts only.</summary>
public sealed class SocialResyncResult
{
    public int PostsRead { get; set; }
    public int CommentsRead { get; set; }
    public int Stored { get; set; }
}

/// <summary>
/// The Listener's gap recovery, run by an operator: it reads the comments under an account's stored
/// posts and stores each one the webhooks did not deliver, as the same event a delivery would have
/// made, so a comment is stored once however it arrived. Each new one wakes the orchestrator. It never
/// interprets a comment and never writes to a platform. There is no timer: AGENTS.md forbids polling.
/// </summary>
public sealed class SocialResync
{
    /// <summary>How many of the newest stored posts are read.</summary>
    internal const int PostsRead = 25;

    /// <summary>How many pages of comments are read under one post. More than this is reported, not cut silently.</summary>
    internal const int PagesPerPost = 20;

    private readonly IServiceProvider _services;
    private readonly ISocialStore _store;
    private readonly ISocialTokenProvider _tokens;
    private readonly SocialWake _wake;
    private readonly TimeProvider _clock;
    private readonly ILogger<SocialResync> _logger;

    public SocialResync(
        IServiceProvider services, ISocialStore store, ISocialTokenProvider tokens, SocialWake wake, TimeProvider clock, ILogger<SocialResync> logger)
    {
        _services = services;
        _store = store;
        _tokens = tokens;
        _wake = wake;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<SocialResyncResult>> ResyncAsync(Guid accountId, CancellationToken ct)
    {
        var account = await _store.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        if (!account.IsSuccess)
            return Fail<SocialResyncResult>("load the account", account.ErrorCode, account.Error, accountId);

        var reader = _services.GetKeyedService<ISocialEngagementReader>(account.Value!.Platform);
        if (reader is null)
            return Fail<SocialResyncResult>("find a reader", SocialFailureCodes.NotSupported,
                $"GeekSocial cannot read {account.Value.Platform} comments yet.", accountId);

        var token = await _tokens.GetTokenAsync(accountId, ct).ConfigureAwait(false);
        if (!token.IsSuccess)
            return Fail<SocialResyncResult>("get the token", token.ErrorCode, token.Error, accountId);

        var posts = await _store.ListPostsAsync(accountId, PostsRead, ct).ConfigureAwait(false);
        if (!posts.IsSuccess)
            return Fail<SocialResyncResult>("list the posts", posts.ErrorCode, posts.Error, accountId);

        var result = new SocialResyncResult();
        foreach (var post in posts.Value!.Where(p => p.PublishedAt is not null))
        {
            result.PostsRead++;
            string? cursor = null;
            for (var page = 0; page < PagesPerPost; page++)
            {
                var read = await reader.ListCommentsAsync(account.Value, token.Value!, post.PlatformPostId, cursor, ct).ConfigureAwait(false);
                if (!read.IsSuccess)
                    return Fail<SocialResyncResult>("read comments", read.ErrorCode,
                        $"{read.Error} {result.Stored} comment(s) were stored before it.", accountId);

                foreach (var comment in read.Value!.Comments)
                {
                    result.CommentsRead++;
                    var ev = ToEvent(account.Value, post.PlatformPostId, comment);
                    var stored = await _store.RecordEventAsync(ev, ct).ConfigureAwait(false);
                    if (!stored.IsSuccess)
                        return Fail<SocialResyncResult>("store a comment", stored.ErrorCode,
                            $"{stored.Error} {result.Stored} comment(s) were stored before it.", accountId);

                    if (stored.Value)
                    {
                        result.Stored++;
                        _wake.Wake(ev.Id);
                    }
                }

                cursor = read.Value.NextCursor;
                if (cursor is null)
                    break;
                if (page == PagesPerPost - 1)
                    _logger.LogWarning(
                        "Resync of account {AccountId} stopped after {Pages} pages of comments on one post; the older ones were not read.",
                        accountId, PagesPerPost);
            }
        }

        _logger.LogInformation("Resync of account {AccountId}: {Posts} post(s), {Read} comment(s) read, {Stored} new.",
            accountId, result.PostsRead, result.CommentsRead, result.Stored);
        return Result<SocialResyncResult>.Success(result);
    }

    /// <summary>The event a webhook delivery of this comment would have stored, with the same de-duplication key.</summary>
    internal EngagementEvent ToEvent(SocialAccount account, string platformPostId, SocialComment comment) => new()
    {
        Id = Guid.NewGuid(),
        Platform = account.Platform,
        PlatformEventId = CreatedEventId(account.Platform, comment.PlatformCommentId),
        PlatformAccountId = account.PlatformAccountId,
        Kind = EngagementKinds.CommentCreated,
        PlatformPostId = platformPostId,
        PlatformCommentId = comment.PlatformCommentId,
        ParentPlatformCommentId = comment.ParentPlatformCommentId,
        AuthorPlatformId = comment.AuthorPlatformId,
        AuthorDisplayName = comment.AuthorDisplayName,
        Text = comment.Text,
        OccurredAt = comment.CreatedAt,
        ReceivedAt = _clock.GetUtcNow().UtcDateTime,
    };

    /// <summary>The key the platform's webhook reader gives a created comment. Only Facebook has a reader today.</summary>
    internal static string CreatedEventId(string platform, string platformCommentId) => platform switch
    {
        SocialPlatforms.Facebook => Meta.MetaEngagementReader.CreatedCommentEventId(platformCommentId),
        _ => throw new ArgumentException($"No resync key is defined for {platform}.", nameof(platform)),
    };

    private Result<T> Fail<T>(string step, string? code, string? error, Guid accountId)
    {
        _logger.LogError("Resync of account {AccountId} could not {Step}: {Code}. {Error}", accountId, step, code, error);
        return Result<T>.Failure(error ?? $"Could not {step}.", code ?? SocialFailureCodes.StoreUnavailable);
    }
}
