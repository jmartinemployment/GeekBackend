using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.Meta;

/// <summary>
/// The Listener's Facebook Page half: it proves the endpoint is ours to Meta, checks the signature
/// of every delivery against its raw body, turns the Page <c>feed</c> changes into
/// <see cref="EngagementEvent"/>s, and reads a post's comments for gap recovery. It never interprets
/// a comment, never calls Claude and never calls a Meta write endpoint.
/// </summary>
/// <remarks>
/// The change fields are the ones Meta's Page <c>feed</c> reference tabulates (docs/PLATFORM-CAPABILITIES.md
/// 2a): a comment carries comment_id, parent_id, post_id, from, message and created_time, a reaction
/// carries reaction_type. Whether a development-mode app receives them is open item M3, settled by one
/// live comment, and the fixtures in the tests are written from the reference and not from a live delivery.
/// </remarks>
public sealed class MetaEngagementReader : ISocialEngagementReader
{
    public const string SignatureHeader = "X-Hub-Signature-256";

    private const string SignaturePrefix = "sha256=";
    private const int MaxChallengeLength = 256;
    private const int CommentPageSize = 50;

    private readonly MetaOptions _options;
    private readonly MetaGraphClient _graph;
    private readonly TimeProvider _clock;

    public MetaEngagementReader(MetaOptions options, MetaGraphClient graph, TimeProvider clock)
    {
        _options = options;
        _graph = graph;
        _clock = clock;
    }

    public string Platform => SocialPlatforms.Facebook;

    public Result<SocialHandshakeResponse> AnswerHandshake(SocialHandshakeRequest request)
    {
        if (_options.VerifyToken is null)
            return Result<SocialHandshakeResponse>.Failure(
                $"{MetaOptions.VerifyTokenVariable} is not set, so the webhook handshake cannot be answered.",
                SocialFailureCodes.NotConfigured);

        var query = request.Query;
        if (!query.TryGetValue("hub.mode", out var mode) || mode != "subscribe")
            return Result<SocialHandshakeResponse>.Failure("hub.mode must be 'subscribe'.", SocialFailureCodes.InvalidRequest);

        if (!query.TryGetValue("hub.verify_token", out var presented) || !SameSecret(presented, _options.VerifyToken))
            return Result<SocialHandshakeResponse>.Failure("hub.verify_token does not match.", SocialFailureCodes.Forbidden);

        if (!query.TryGetValue("hub.challenge", out var challenge)
            || string.IsNullOrEmpty(challenge)
            || challenge.Length > MaxChallengeLength
            || challenge.Any(char.IsControl))
            return Result<SocialHandshakeResponse>.Failure("hub.challenge is missing or not a plain string.", SocialFailureCodes.InvalidRequest);

        return Result<SocialHandshakeResponse>.Success(new SocialHandshakeResponse { ContentType = "text/plain", Body = challenge });
    }

    public Result<IReadOnlyList<EngagementEvent>> ParseWebhook(SocialWebhookRequest request)
    {
        var signature = CheckSignature(request);
        if (!signature.IsSuccess)
            return Result<IReadOnlyList<EngagementEvent>>.Failure(signature.Error!, signature.ErrorCode!);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(request.RawBody);
        }
        catch (JsonException)
        {
            return Result<IReadOnlyList<EngagementEvent>>.Failure("The delivery body is not JSON.", SocialFailureCodes.InvalidRequest);
        }

        using (document)
        {
            var root = document.RootElement;
            var objectName = StringOf(root, "object");
            if (objectName != "page")
                return Result<IReadOnlyList<EngagementEvent>>.Failure(
                    $"Meta delivered an '{objectName ?? "unnamed"}' notification and only Page notifications are handled.",
                    SocialFailureCodes.NotSupported);

            if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return Result<IReadOnlyList<EngagementEvent>>.Failure("The delivery has no entry list.", SocialFailureCodes.InvalidRequest);

            var received = _clock.GetUtcNow().UtcDateTime;
            var events = new List<EngagementEvent>();
            foreach (var entry in entries.EnumerateArray())
            {
                var pageId = StringOf(entry, "id");
                if (string.IsNullOrWhiteSpace(pageId))
                    return Result<IReadOnlyList<EngagementEvent>>.Failure("An entry has no Page id.", SocialFailureCodes.InvalidRequest);

                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                    continue;

                var entryTime = LongOf(entry, "time");
                foreach (var change in changes.EnumerateArray())
                {
                    if (StringOf(change, "field") != "feed" || !change.TryGetProperty("value", out var value))
                        continue;

                    var parsed = ParseChange(pageId, entryTime, value, received);
                    if (!parsed.IsSuccess)
                        return Result<IReadOnlyList<EngagementEvent>>.Failure(parsed.Error!, parsed.ErrorCode!);

                    // A change that is not a comment or a reaction (the Page's own posts and photos
                    // arrive in the same feed) is not engagement and stores nothing.
                    if (parsed.Value is not null)
                        events.Add(parsed.Value);
                }
            }

            return Result<IReadOnlyList<EngagementEvent>>.Success(events);
        }
    }

    public async Task<Result<SocialCommentPage>> ListCommentsAsync(
        SocialAccount account, SocialToken token, string platformPostId, string? cursor, CancellationToken ct = default)
    {
        if (account.Platform != SocialPlatforms.Facebook)
            return Result<SocialCommentPage>.Failure("This reader serves Facebook Pages only.", SocialFailureCodes.InvalidRequest);
        if (string.IsNullOrWhiteSpace(platformPostId))
            return Result<SocialCommentPage>.Failure("A post id is required.", SocialFailureCodes.InvalidRequest);

        var query = new List<KeyValuePair<string, string>>
        {
            new("filter", "stream"),
            new("order", "chronological"),
            new("limit", CommentPageSize.ToString(CultureInfo.InvariantCulture)),
            new("fields", "id,message,created_time,from{id,name},parent{id}"),
        };
        if (!string.IsNullOrEmpty(cursor))
            query.Add(new("after", cursor));

        var answer = await _graph.GetAsync($"{Uri.EscapeDataString(platformPostId)}/comments", token, query, ct).ConfigureAwait(false);
        if (!answer.IsSuccess)
            return Result<SocialCommentPage>.Failure(answer.Error!, answer.ErrorCode!);

        var page = new SocialCommentPage();
        if (answer.Value.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = StringOf(item, "id");
                if (string.IsNullOrWhiteSpace(id)
                    || !DateTimeOffset.TryParse(StringOf(item, "created_time"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var createdAt))
                    return Result<SocialCommentPage>.Failure(
                        "Meta returned a comment without an id or a readable time.", SocialFailureCodes.PlatformRejected);

                page.Comments.Add(new SocialComment
                {
                    PlatformCommentId = id,
                    ParentPlatformCommentId = StringOf(Child(item, "parent"), "id"),
                    AuthorPlatformId = StringOf(Child(item, "from"), "id"),
                    AuthorDisplayName = StringOf(Child(item, "from"), "name"),
                    Text = StringOf(item, "message") ?? string.Empty,
                    CreatedAt = createdAt.UtcDateTime,
                });
            }
        }

        if (answer.Value.TryGetProperty("paging", out var paging)
            && paging.TryGetProperty("next", out _)
            && paging.TryGetProperty("cursors", out var cursors))
        {
            page.NextCursor = StringOf(cursors, "after");
        }

        return Result<SocialCommentPage>.Success(page);
    }

    /// <summary>Checks X-Hub-Signature-256 as sha256= and the hex HMAC-SHA256 of the raw body keyed by the app secret.</summary>
    private Result<bool> CheckSignature(SocialWebhookRequest request)
    {
        if (_options.AppSecret is null)
            return Result<bool>.Failure(
                $"{MetaOptions.AppSecretVariable} is not set, so no delivery can be verified and none is accepted.",
                SocialFailureCodes.NotConfigured);

        if (!request.Headers.TryGetValue(SignatureHeader, out var header)
            || header is null
            || !header.StartsWith(SignaturePrefix, StringComparison.Ordinal))
            return Result<bool>.Failure("The delivery has no sha256 signature.", SocialFailureCodes.InvalidSignature);

        byte[] presented;
        try
        {
            presented = Convert.FromHexString(header[SignaturePrefix.Length..]);
        }
        catch (FormatException)
        {
            return Result<bool>.Failure("The delivery's signature is not hex.", SocialFailureCodes.InvalidSignature);
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.AppSecret), request.RawBody);
        return CryptographicOperations.FixedTimeEquals(presented, expected)
            ? Result<bool>.Success(true)
            : Result<bool>.Failure("The delivery's signature does not match its body.", SocialFailureCodes.InvalidSignature);
    }

    /// <summary>One feed change as an event, or a successful null for a change that is not engagement.</summary>
    private static Result<EngagementEvent?> ParseChange(string pageId, long? entryTime, JsonElement value, DateTime received)
    {
        var item = StringOf(value, "item");
        if (item is not ("comment" or "reaction"))
            return Result<EngagementEvent?>.Success(null);

        var verb = StringOf(value, "verb");
        var postId = StringOf(value, "post_id");
        var parentId = StringOf(value, "parent_id");
        var fromId = StringOf(Child(value, "from"), "id");
        var fromName = StringOf(Child(value, "from"), "name");
        var createdTime = LongOf(value, "created_time") ?? entryTime;
        if (createdTime is null)
            return Result<EngagementEvent?>.Failure($"A {item} change has no time.", SocialFailureCodes.InvalidRequest);

        var occurred = DateTimeOffset.FromUnixTimeSeconds(createdTime.Value).UtcDateTime;

        // A reply's parent_id is the comment it answers; a top-level comment's parent_id is the post.
        var parentComment = !string.IsNullOrEmpty(parentId) && parentId != postId ? parentId : null;

        if (item == "comment")
        {
            var commentId = StringOf(value, "comment_id");
            if (string.IsNullOrWhiteSpace(commentId))
                return Result<EngagementEvent?>.Failure("A comment change has no comment_id.", SocialFailureCodes.InvalidRequest);

            var kind = verb switch
            {
                "add" => EngagementKinds.CommentCreated,
                "edit" or "edited" => EngagementKinds.CommentEdited,
                "delete" or "remove" => EngagementKinds.CommentDeleted,
                "hide" => EngagementKinds.CommentHidden,
                "unhide" => EngagementKinds.CommentUnhidden,
                _ => null,
            };
            if (kind is null)
                return Result<EngagementEvent?>.Failure(
                    $"A comment change carries the verb '{verb ?? "none"}', which is not one Meta documents.", SocialFailureCodes.InvalidRequest);

            // A comment is created once, so its created event keys on the comment alone and a redelivery
            // cannot make a second one. Every other change to it can repeat, so those add the delivery time.
            var eventId = kind == EngagementKinds.CommentCreated
                ? $"fb:comment:{commentId}:created"
                : $"fb:comment:{commentId}:{verb}:{entryTime ?? createdTime}";

            return Result<EngagementEvent?>.Success(new EngagementEvent
            {
                Platform = SocialPlatforms.Facebook,
                PlatformEventId = eventId,
                PlatformAccountId = pageId,
                Kind = kind,
                PlatformPostId = postId,
                PlatformCommentId = commentId,
                ParentPlatformCommentId = parentComment,
                AuthorPlatformId = fromId,
                AuthorDisplayName = fromName,
                Text = StringOf(value, "message"),
                OccurredAt = occurred,
                ReceivedAt = received,
            });
        }

        var reactionType = StringOf(value, "reaction_type");
        if (string.IsNullOrWhiteSpace(reactionType))
            return Result<EngagementEvent?>.Failure("A reaction change has no reaction_type.", SocialFailureCodes.InvalidRequest);

        // A reaction whose type was changed arrives as an edit and leaves the person holding the new one.
        var reactionKind = verb switch
        {
            "add" or "edit" => EngagementKinds.ReactionAdded,
            "remove" => EngagementKinds.ReactionRemoved,
            _ => null,
        };
        if (reactionKind is null)
            return Result<EngagementEvent?>.Failure(
                $"A reaction change carries the verb '{verb ?? "none"}', which is not one Meta documents.", SocialFailureCodes.InvalidRequest);

        var reactedCommentId = StringOf(value, "comment_id") ?? parentComment;
        var target = reactedCommentId ?? postId ?? parentId;
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(fromId))
            return Result<EngagementEvent?>.Failure("A reaction change names no target or no person.", SocialFailureCodes.InvalidRequest);

        return Result<EngagementEvent?>.Success(new EngagementEvent
        {
            Platform = SocialPlatforms.Facebook,
            PlatformEventId = $"fb:reaction:{verb}:{fromId}:{target}:{createdTime}",
            PlatformAccountId = pageId,
            Kind = reactionKind,
            PlatformPostId = postId,
            PlatformCommentId = reactedCommentId,
            AuthorPlatformId = fromId,
            AuthorDisplayName = fromName,
            ReactionType = reactionType.ToUpperInvariant(),
            OccurredAt = occurred,
            ReceivedAt = received,
        });
    }

    private static bool SameSecret(string presented, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));

    /// <summary>The named child, or an undefined element when there is none, which <see cref="StringOf"/> reads as absent.</summary>
    private static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child) ? child : default;

    private static string? StringOf(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A number Meta may send as a JSON number or as a string.</summary>
    private static long? LongOf(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}
