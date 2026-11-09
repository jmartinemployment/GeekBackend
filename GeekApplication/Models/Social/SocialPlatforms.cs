namespace GeekApplication.Models.Social;

// The vocabulary the GeekSocial contracts share. Each value below is a row of
// docs/PLATFORM-CAPABILITIES.md, which cites the platform page it came from. A platform that cannot
// do something says so here, and the contract methods answer not_supported without making the call.

/// <summary>
/// Where the agent publishes and reads. The string is the persisted value and the keyed-DI key of
/// the platform's implementations.
/// </summary>
public static class SocialPlatforms
{
    public const string Facebook = "facebook";
    public const string Instagram = "instagram";
    public const string LinkedInOrganization = "linkedin_organization";
    public const string LinkedInMember = "linkedin_member";

    public static readonly IReadOnlyList<string> All =
        new[] { Facebook, Instagram, LinkedInOrganization, LinkedInMember };

    public static bool IsKnown(string? platform) => platform is not null && All.Contains(platform);
}

/// <summary>What a post is made of. Only the kinds the capability matrix has verified are listed.</summary>
public static class SocialPostKinds
{
    /// <summary>A post that carries a link: a Page feed link, or a LinkedIn article post.</summary>
    public const string Link = "link";

    /// <summary>A post that is one image with a caption. Instagram has no link or text-only posts.</summary>
    public const string Image = "image";

    public static readonly IReadOnlyList<string> All = new[] { Link, Image };
}

/// <summary>Reaction names as the platforms spell them. Facebook accepts only <see cref="Like"/> from a Page.</summary>
public static class SocialReactionTypes
{
    public const string Like = "LIKE";
    public const string Praise = "PRAISE";
    public const string Empathy = "EMPATHY";
    public const string Interest = "INTEREST";
    public const string Appreciation = "APPRECIATION";
    public const string Entertainment = "ENTERTAINMENT";
}

/// <summary>What a reaction or a reply is aimed at.</summary>
public static class SocialTargetKinds
{
    public const string Post = "post";
    public const string Comment = "comment";

    public static readonly IReadOnlyList<string> All = new[] { Post, Comment };
}

/// <summary>
/// The stable reasons a contract call fails, carried in <see cref="Result{T}.ErrorCode"/>. A caller
/// branches on the code and shows the message to an operator; neither ever contains a token or a
/// comment body.
/// </summary>
public static class SocialFailureCodes
{
    /// <summary>The platform has no such action, or the matrix has it BLOCKED. Nothing was sent.</summary>
    public const string NotSupported = "not_supported";

    /// <summary>The request breaks a rule checked before any call. Nothing was sent.</summary>
    public const string InvalidRequest = "invalid_request";

    /// <summary>The platform refused the token: expired, revoked, or missing a permission. Reconnect the account.</summary>
    public const string TokenInvalid = "token_invalid";

    /// <summary>The platform throttled the call. Nothing was applied; later sends wait out a cool-down.</summary>
    public const string RateLimited = "rate_limited";

    /// <summary>The account may not do this: a role or permission the token does not carry.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>The post, comment or account is gone on the platform.</summary>
    public const string NotFound = "not_found";

    /// <summary>The platform answered with an error it gave a reason for. Nothing was applied.</summary>
    public const string PlatformRejected = "platform_rejected";

    /// <summary>The platform could not be reached or answered 5xx before accepting the call.</summary>
    public const string PlatformUnavailable = "platform_unavailable";

    /// <summary>
    /// The call may have reached the platform and its answer was lost, so it may have been applied.
    /// Never resend; the operator checks on the platform.
    /// </summary>
    public const string OutcomeUnknown = "outcome_unknown";

    /// <summary>A webhook did not carry a valid signature for its raw body. Nothing was stored.</summary>
    public const string InvalidSignature = "invalid_signature";

    /// <summary>The store refused the write because of what it already holds: a reply already exists for that comment, or the draft is no longer in the state the caller read.</summary>
    public const string Conflict = "conflict";

    /// <summary>The store could not be reached or did not complete the call.</summary>
    public const string StoreUnavailable = "store_unavailable";

    /// <summary>The key that protects stored tokens is missing or unusable, so no token can be stored or read.</summary>
    public const string EncryptionUnavailable = "encryption_unavailable";

    public static readonly IReadOnlyList<string> All = new[]
    {
        NotSupported, InvalidRequest, TokenInvalid, RateLimited, Forbidden, NotFound, PlatformRejected,
        PlatformUnavailable, OutcomeUnknown, InvalidSignature, Conflict, StoreUnavailable, EncryptionUnavailable,
    };

    public static bool IsKnown(string? code) => code is not null && All.Contains(code);
}

/// <summary>
/// What one platform can do for this agent, as the capability matrix records it. These are the
/// platform's documented abilities; a call that fails on a live account is still reported as the
/// platform's own failure.
/// </summary>
public sealed class SocialPlatformCapabilities
{
    public required string Platform { get; init; }

    /// <summary>The post kinds this platform publishes. A kind not listed is rejected before any call.</summary>
    public required IReadOnlyList<string> PublishKinds { get; init; }

    /// <summary>A link post needs a title (LinkedIn's article content has no URL scraping).</summary>
    public bool LinkPostNeedsTitle { get; init; }

    /// <summary>A post needs a public image URL (Instagram publishes from a hosted file).</summary>
    public bool PostNeedsImageUrl { get; init; }

    /// <summary>How far ahead a post may be scheduled, or null when the platform has no documented scheduling.</summary>
    public TimeSpan? SchedulingMinLeadTime { get; init; }
    public TimeSpan? SchedulingMaxLeadTime { get; init; }

    public bool CanReadComments { get; init; }
    public bool CanReplyToComments { get; init; }

    /// <summary>The reaction types the account may create. Empty means the agent cannot react here.</summary>
    public required IReadOnlyList<string> ReactionTypes { get; init; }

    /// <summary>The platform pushes comment and reaction events to a webhook.</summary>
    public bool DeliversWebhooks { get; init; }

    /// <summary>How long other people's comment text may be kept, or null when no platform limit is documented.</summary>
    public TimeSpan? CommentTextRetention { get; init; }

    /// <summary>How long a commenter's profile data may be cached, or null when no platform limit is documented.</summary>
    public TimeSpan? CommenterProfileRetention { get; init; }

    public bool CanSchedulePosts => SchedulingMinLeadTime is not null && SchedulingMaxLeadTime is not null;

    public bool CanReact => ReactionTypes.Count > 0;

    private static readonly IReadOnlyList<string> LinkedInReactions = new[]
    {
        SocialReactionTypes.Like,
        SocialReactionTypes.Praise,
        SocialReactionTypes.Empathy,
        SocialReactionTypes.Interest,
        SocialReactionTypes.Appreciation,
        SocialReactionTypes.Entertainment,
    };

    private static readonly Dictionary<string, SocialPlatformCapabilities> ByPlatform =
        new()
        {
            // PLATFORM-CAPABILITIES.md 2a. Page reactions are LIKE only. Whether a reply to a comment
            // is created as the Page (open item M1) is settled by one live reply before Phase 4.
            [SocialPlatforms.Facebook] = new()
            {
                Platform = SocialPlatforms.Facebook,
                PublishKinds = new[] { SocialPostKinds.Link },
                SchedulingMinLeadTime = TimeSpan.FromMinutes(10),
                SchedulingMaxLeadTime = TimeSpan.FromDays(30),
                CanReadComments = true,
                CanReplyToComments = true,
                ReactionTypes = new[] { SocialReactionTypes.Like },
                DeliversWebhooks = true,
            },

            // PLATFORM-CAPABILITIES.md 2d. Instagram publishes one hosted image, video, reel,
            // carousel or story; there is no link or text-only post. No reaction to a comment is documented.
            [SocialPlatforms.Instagram] = new()
            {
                Platform = SocialPlatforms.Instagram,
                PublishKinds = new[] { SocialPostKinds.Image },
                PostNeedsImageUrl = true,
                CanReadComments = true,
                CanReplyToComments = true,
                ReactionTypes = Array.Empty<string>(),
                DeliversWebhooks = true,
            },

            // PLATFORM-CAPABILITIES.md 2b. Other members' comments may be kept 48 hours and a
            // commenter's profile data cached 24 hours (section 5).
            [SocialPlatforms.LinkedInOrganization] = new()
            {
                Platform = SocialPlatforms.LinkedInOrganization,
                PublishKinds = new[] { SocialPostKinds.Link },
                LinkPostNeedsTitle = true,
                CanReadComments = true,
                CanReplyToComments = true,
                ReactionTypes = LinkedInReactions,
                DeliversWebhooks = true,
                CommentTextRetention = TimeSpan.FromHours(48),
                CommenterProfileRetention = TimeSpan.FromHours(24),
            },

            // PLATFORM-CAPABILITIES.md 2c. r_member_social is closed to new developers, so a member
            // profile is publish-only: no comments to read, nothing to reply to or react to.
            [SocialPlatforms.LinkedInMember] = new()
            {
                Platform = SocialPlatforms.LinkedInMember,
                PublishKinds = new[] { SocialPostKinds.Link },
                LinkPostNeedsTitle = true,
                ReactionTypes = Array.Empty<string>(),
            },
        };

    public static bool TryGet(string? platform, out SocialPlatformCapabilities capabilities)
    {
        if (platform is not null && ByPlatform.TryGetValue(platform, out var found))
        {
            capabilities = found;
            return true;
        }

        capabilities = null!;
        return false;
    }

    /// <summary>For a platform the caller already holds as a known value; an unknown one is a bug and throws.</summary>
    public static SocialPlatformCapabilities For(string platform) =>
        TryGet(platform, out var found)
            ? found
            : throw new ArgumentException($"Unknown social platform '{platform}'.", nameof(platform));

    /// <summary>True when this platform lets the account create this reaction type.</summary>
    public bool AllowsReaction(string? reactionType) =>
        reactionType is not null && ReactionTypes.Contains(reactionType);
}
