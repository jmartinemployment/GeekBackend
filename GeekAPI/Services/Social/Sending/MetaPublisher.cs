using System.Globalization;
using System.Text.Json;
using GeekAPI.Services.Social.Meta;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.Sending;

/// <summary>
/// Publishes a link post to a Facebook Page: <c>POST /{page-id}/feed</c> with <c>message</c> and
/// <c>link</c>, or scheduled with <c>published=false</c> and <c>scheduled_publish_time</c>
/// (docs/PLATFORM-CAPABILITIES.md 2a). One attempt; an answer that does not prove Meta refused it is
/// outcome_unknown. Only <see cref="SocialSender"/> calls it.
/// </summary>
/// <remarks>
/// Facebook reads a link's title, description and picture from the page itself and no longer takes
/// them from the caller, so a request carrying any of them is refused rather than sent without them.
/// </remarks>
public sealed class MetaPublisher : ISocialPublisher
{
    private readonly MetaGraphClient _graph;
    private readonly TimeProvider _clock;

    public MetaPublisher(MetaGraphClient graph, TimeProvider clock)
    {
        _graph = graph;
        _clock = clock;
    }

    public string Platform => SocialPlatforms.Facebook;

    public async Task<Result<SocialPostResult>> PublishAsync(
        SocialAccount account, SocialToken token, SocialPostRequest request, CancellationToken ct = default)
    {
        if (account.Platform != SocialPlatforms.Facebook)
            return Result<SocialPostResult>.Failure("This publisher serves Facebook Pages only.", SocialFailureCodes.InvalidRequest);

        var valid = SocialPostRequestValidator.Validate(SocialPlatforms.Facebook, request, _clock.GetUtcNow().UtcDateTime);
        if (!valid.IsSuccess)
            return Result<SocialPostResult>.Failure(valid.Error!, valid.ErrorCode!);

        if (!string.IsNullOrWhiteSpace(request.LinkTitle)
            || !string.IsNullOrWhiteSpace(request.LinkDescription)
            || !string.IsNullOrWhiteSpace(request.ImageUrl))
            return Result<SocialPostResult>.Failure(
                "Facebook reads a link's title, description and picture from the linked page; leave them empty.",
                SocialFailureCodes.NotSupported);

        if (string.IsNullOrWhiteSpace(request.Text))
            return Result<SocialPostResult>.Failure("A post needs text.", SocialFailureCodes.InvalidRequest);

        var form = new List<KeyValuePair<string, string>>
        {
            new("message", request.Text),
            new("link", request.LinkUrl!),
        };
        if (request.ScheduledFor is { } scheduledFor)
        {
            form.Add(new("published", "false"));
            form.Add(new("scheduled_publish_time",
                new DateTimeOffset(scheduledFor, TimeSpan.Zero).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        }

        var answer = await _graph.PostWriteAsync($"{Uri.EscapeDataString(account.PlatformAccountId)}/feed", token, form, ct).ConfigureAwait(false);
        if (!answer.IsSuccess)
            return Result<SocialPostResult>.Failure(answer.Error!, answer.ErrorCode!);

        var id = MetaAnswers.IdOf(answer.Value);
        if (id is null)
            return Result<SocialPostResult>.Failure(
                "Meta accepted the post and returned no id. It may be live: check the Page.", SocialFailureCodes.OutcomeUnknown);

        return Result<SocialPostResult>.Success(new SocialPostResult
        {
            PlatformPostId = id,
            Scheduled = request.ScheduledFor is not null,
        });
    }
}

/// <summary>Reading the small answers Meta's write calls return.</summary>
internal static class MetaAnswers
{
    /// <summary>The <c>id</c> Meta returned, or null when the answer has none.</summary>
    public static string? IdOf(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.Object
        && answer.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(id.GetString())
            ? id.GetString()
            : null;

    /// <summary>True when Meta answered <c>{"success": true}</c>.</summary>
    public static bool Succeeded(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.Object
        && answer.TryGetProperty("success", out var success)
        && success.ValueKind == JsonValueKind.True;
}
