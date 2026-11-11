using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.Sending;

/// <summary>
/// The Sender: the only component that changes anything on a platform (publish, reply, react). It
/// takes an account, a token the caller already holds and one request, hands it to the platform's
/// writer once, and writes one action_audit row for the attempt, whatever its outcome. It never decides
/// whether to send, never edits text and never retries: a failure is returned with its code and the
/// caller records it.
/// </summary>
/// <remarks>
/// A platform that has no writer registered answers not_configured and nothing is sent or audited,
/// because no call was attempted. An audit row that cannot be stored does not undo what the platform
/// already did: the platform's result is returned, and the missing row is logged as an error.
/// </remarks>
public sealed class SocialSender
{
    private readonly IServiceProvider _services;
    private readonly ISocialStore _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<SocialSender> _logger;

    public SocialSender(IServiceProvider services, ISocialStore store, TimeProvider clock, ILogger<SocialSender> logger)
    {
        _services = services;
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<SocialReplyResult>> ReplyAsync(
        SocialAccount account, SocialToken token, SocialReplyRequest request, Guid draftId, string? actorUserId, CancellationToken ct = default)
    {
        var writer = _services.GetKeyedService<ISocialEngagementWriter>(account.Platform);
        if (writer is null)
            return NoWriter<SocialReplyResult>(account, ActionKinds.Reply);

        var result = await writer.ReplyAsync(account, token, request, ct).ConfigureAwait(false);
        await AuditAsync(account, ActionKinds.Reply, result.IsSuccess, result.ErrorCode, result.Error,
            result.IsSuccess ? result.Value!.PlatformCommentId : null, draftId, actorUserId, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<Result<SocialPostResult>> PublishAsync(
        SocialAccount account, SocialToken token, SocialPostRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var publisher = _services.GetKeyedService<ISocialPublisher>(account.Platform);
        if (publisher is null)
            return NoWriter<SocialPostResult>(account, ActionKinds.Publish);

        var result = await publisher.PublishAsync(account, token, request, ct).ConfigureAwait(false);
        await AuditAsync(account, ActionKinds.Publish, result.IsSuccess, result.ErrorCode, result.Error,
            result.IsSuccess ? result.Value!.PlatformPostId : null, null, actorUserId, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<Result<SocialReactionResult>> ReactAsync(
        SocialAccount account, SocialToken token, SocialReactionRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var writer = _services.GetKeyedService<ISocialEngagementWriter>(account.Platform);
        if (writer is null)
            return NoWriter<SocialReactionResult>(account, ActionKinds.React);

        var result = await writer.ReactAsync(account, token, request, ct).ConfigureAwait(false);
        await AuditAsync(account, ActionKinds.React, result.IsSuccess, result.ErrorCode, result.Error,
            result.IsSuccess ? result.Value!.PlatformReactionId ?? request.PlatformTargetId : null, null, actorUserId, ct).ConfigureAwait(false);
        return result;
    }

    private Result<T> NoWriter<T>(SocialAccount account, string action)
    {
        _logger.LogError("No {Action} writer is registered for {Platform}; nothing was sent for account {AccountId}.",
            action, account.Platform, account.Id);
        return Result<T>.Failure($"GeekSocial cannot {action} on {account.Platform} yet.", SocialFailureCodes.NotConfigured);
    }

    private async Task AuditAsync(
        SocialAccount account, string action, bool succeeded, string? code, string? error,
        string? platformObjectId, Guid? draftId, string? actorUserId, CancellationToken ct)
    {
        var failureCode = succeeded ? null : SocialFailureCodes.IsKnown(code) ? code : SocialFailureCodes.PlatformRejected;
        var outcome = succeeded
            ? ActionOutcomes.Succeeded
            : failureCode == SocialFailureCodes.OutcomeUnknown ? ActionOutcomes.OutcomeUnknown : ActionOutcomes.Failed;

        if (!succeeded)
        {
            // The writers' messages name status codes and Meta trace ids, never a body, a token or a person.
            _logger.LogError("{Action} on {Platform} for account {AccountId} failed: {Code}. {Error}",
                action, account.Platform, account.Id, failureCode, error);
        }

        var stored = await _store.RecordAuditAsync(new ActionAudit
        {
            AccountId = account.Id,
            Platform = account.Platform,
            Action = action,
            Outcome = outcome,
            PlatformObjectId = platformObjectId,
            ReplyDraftId = draftId,
            FailureCode = failureCode,
            FailureDetail = succeeded ? null : Shorten(error),
            ActorUserId = actorUserId,
            OccurredAt = _clock.GetUtcNow().UtcDateTime,
        }, ct).ConfigureAwait(false);

        if (!stored.IsSuccess)
        {
            _logger.LogError(
                "The audit row for {Action} on account {AccountId} (outcome {Outcome}) could not be stored: {Code}. The platform call stands.",
                action, account.Id, outcome, stored.ErrorCode);
        }
    }

    private static string? Shorten(string? text) =>
        text is null ? null : text.Length <= 500 ? text : text[..500];
}
