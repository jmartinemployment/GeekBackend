using GeekAPI.Services.Social.Meta;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.Sending;

/// <summary>
/// Replies and likes as a Facebook Page: a reply is <c>POST /{comment-id}/comments</c> with
/// <c>message</c>, a like is <c>POST /{object-id}/likes</c> (docs/PLATFORM-CAPABILITIES.md 2a; a Page
/// may only LIKE). One attempt each; an answer that does not prove Meta refused it is outcome_unknown.
/// Only <see cref="SocialSender"/> calls it.
/// </summary>
public sealed class MetaEngagementWriter : ISocialEngagementWriter
{
    private readonly MetaGraphClient _graph;

    public MetaEngagementWriter(MetaGraphClient graph) => _graph = graph;

    public string Platform => SocialPlatforms.Facebook;

    public async Task<Result<SocialReplyResult>> ReplyAsync(
        SocialAccount account, SocialToken token, SocialReplyRequest request, CancellationToken ct = default)
    {
        if (account.Platform != SocialPlatforms.Facebook)
            return Result<SocialReplyResult>.Failure("This writer serves Facebook Pages only.", SocialFailureCodes.InvalidRequest);

        var valid = SocialReplyRequestValidator.Validate(SocialPlatforms.Facebook, request);
        if (!valid.IsSuccess)
            return Result<SocialReplyResult>.Failure(valid.Error!, valid.ErrorCode!);

        var answer = await _graph.PostWriteAsync(
            $"{Uri.EscapeDataString(request.ParentPlatformCommentId)}/comments",
            token,
            new[] { new KeyValuePair<string, string>("message", request.Text) },
            ct).ConfigureAwait(false);
        if (!answer.IsSuccess)
            return Result<SocialReplyResult>.Failure(answer.Error!, answer.ErrorCode!);

        var id = MetaAnswers.IdOf(answer.Value);
        return id is null
            ? Result<SocialReplyResult>.Failure(
                "Meta accepted the reply and returned no id. It may be live: check the Page.", SocialFailureCodes.OutcomeUnknown)
            : Result<SocialReplyResult>.Success(new SocialReplyResult { PlatformCommentId = id });
    }

    public async Task<Result<SocialReactionResult>> ReactAsync(
        SocialAccount account, SocialToken token, SocialReactionRequest request, CancellationToken ct = default)
    {
        if (account.Platform != SocialPlatforms.Facebook)
            return Result<SocialReactionResult>.Failure("This writer serves Facebook Pages only.", SocialFailureCodes.InvalidRequest);

        var valid = SocialReactionRequestValidator.Validate(SocialPlatforms.Facebook, request);
        if (!valid.IsSuccess)
            return Result<SocialReactionResult>.Failure(valid.Error!, valid.ErrorCode!);

        var answer = await _graph.PostWriteAsync(
            $"{Uri.EscapeDataString(request.PlatformTargetId)}/likes",
            token,
            Array.Empty<KeyValuePair<string, string>>(),
            ct).ConfigureAwait(false);
        if (!answer.IsSuccess)
            return Result<SocialReactionResult>.Failure(answer.Error!, answer.ErrorCode!);

        // A like has no id of its own: Meta confirms it with success true and nothing else.
        return MetaAnswers.Succeeded(answer.Value)
            ? Result<SocialReactionResult>.Success(new SocialReactionResult())
            : Result<SocialReactionResult>.Failure(
                "Meta answered the like without confirming it. It may be applied: check the Page.", SocialFailureCodes.OutcomeUnknown);
    }
}
