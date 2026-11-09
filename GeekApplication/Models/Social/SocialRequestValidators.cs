namespace GeekApplication.Models.Social;

// The checks every platform implementation runs before it makes a call. A request that fails one is
// answered here, so nothing is sent and the caller gets a reason it can show. Each rule is a row of
// the capability matrix as SocialPlatformCapabilities holds it; none of them is a guess at what a
// platform would do with the request.

public static class SocialPostRequestValidator
{
    /// <summary>Checks a publish request for one platform. <paramref name="utcNow"/> anchors the scheduling window.</summary>
    public static Result Validate(string platform, SocialPostRequest request, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!SocialPlatformCapabilities.TryGet(platform, out var capabilities))
            return Result.Failure($"Unknown social platform '{platform}'.", SocialFailureCodes.InvalidRequest);

        if (!SocialPostKinds.All.Contains(request.Kind))
            return Result.Failure($"Unknown post kind '{request.Kind}'.", SocialFailureCodes.InvalidRequest);

        if (!capabilities.PublishKinds.Contains(request.Kind))
            return Result.Failure(
                $"{platform} does not publish '{request.Kind}' posts. It publishes: {string.Join(", ", capabilities.PublishKinds)}.",
                SocialFailureCodes.NotSupported);

        if (request.Kind == SocialPostKinds.Link)
        {
            if (!IsHttpsUrl(request.LinkUrl))
                return Result.Failure("A link post needs an absolute https link.", SocialFailureCodes.InvalidRequest);

            if (capabilities.LinkPostNeedsTitle && string.IsNullOrWhiteSpace(request.LinkTitle))
                return Result.Failure($"{platform} needs a title on a link post; it does not read one from the page.", SocialFailureCodes.InvalidRequest);
        }

        if (request.Kind == SocialPostKinds.Image || capabilities.PostNeedsImageUrl)
        {
            if (!IsHttpsUrl(request.ImageUrl))
                return Result.Failure($"{platform} needs a publicly reachable https image address.", SocialFailureCodes.InvalidRequest);
        }
        else if (!string.IsNullOrWhiteSpace(request.ImageUrl) && !IsHttpsUrl(request.ImageUrl))
        {
            return Result.Failure("The image address must be an absolute https address.", SocialFailureCodes.InvalidRequest);
        }

        if (request.ScheduledFor is { } scheduledFor)
        {
            if (!capabilities.CanSchedulePosts)
                return Result.Failure($"{platform} has no documented way to schedule a post.", SocialFailureCodes.NotSupported);

            if (scheduledFor.Kind != DateTimeKind.Utc)
                return Result.Failure("A scheduled time must be in UTC.", SocialFailureCodes.InvalidRequest);

            var lead = scheduledFor - utcNow;
            if (lead < capabilities.SchedulingMinLeadTime || lead > capabilities.SchedulingMaxLeadTime)
                return Result.Failure(
                    $"{platform} schedules a post between {Describe(capabilities.SchedulingMinLeadTime!.Value)} and {Describe(capabilities.SchedulingMaxLeadTime!.Value)} ahead.",
                    SocialFailureCodes.InvalidRequest);
        }

        return Result.Success();
    }

    private static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string Describe(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{span.TotalDays:0} days" : $"{span.TotalMinutes:0} minutes";
}

public static class SocialReplyRequestValidator
{
    public static Result Validate(string platform, SocialReplyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!SocialPlatformCapabilities.TryGet(platform, out var capabilities))
            return Result.Failure($"Unknown social platform '{platform}'.", SocialFailureCodes.InvalidRequest);

        if (!capabilities.CanReplyToComments)
            return Result.Failure($"The agent cannot reply to comments on {platform}.", SocialFailureCodes.NotSupported);

        if (string.IsNullOrWhiteSpace(request.PlatformPostId) || string.IsNullOrWhiteSpace(request.ParentPlatformCommentId))
            return Result.Failure("A reply names the post and the comment it answers.", SocialFailureCodes.InvalidRequest);

        if (string.IsNullOrWhiteSpace(request.Text))
            return Result.Failure("A reply needs text.", SocialFailureCodes.InvalidRequest);

        return Result.Success();
    }
}

public static class SocialReactionRequestValidator
{
    public static Result Validate(string platform, SocialReactionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!SocialPlatformCapabilities.TryGet(platform, out var capabilities))
            return Result.Failure($"Unknown social platform '{platform}'.", SocialFailureCodes.InvalidRequest);

        if (!capabilities.CanReact)
            return Result.Failure($"The agent cannot react on {platform}.", SocialFailureCodes.NotSupported);

        if (!SocialTargetKinds.All.Contains(request.TargetKind))
            return Result.Failure($"Unknown reaction target '{request.TargetKind}'.", SocialFailureCodes.InvalidRequest);

        if (string.IsNullOrWhiteSpace(request.PlatformTargetId))
            return Result.Failure("A reaction names what it is aimed at.", SocialFailureCodes.InvalidRequest);

        if (!capabilities.AllowsReaction(request.ReactionType))
            return Result.Failure(
                $"{platform} does not accept the reaction '{request.ReactionType}'. It accepts: {string.Join(", ", capabilities.ReactionTypes)}.",
                SocialFailureCodes.NotSupported);

        return Result.Success();
    }
}
