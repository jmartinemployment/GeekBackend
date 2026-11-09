using GeekApplication.Models.Social;
using Xunit;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The capability table is the capability matrix (docs/PLATFORM-CAPABILITIES.md) in code. These tests
/// pin the rows a change here would otherwise move without anyone noticing: a BLOCKED row turning
/// into a supported one has to be a deliberate edit of both the matrix and these tests.
/// </summary>
public class SocialPlatformCapabilitiesTests
{
    [Fact]
    public void Every_known_platform_has_capabilities()
    {
        foreach (var platform in SocialPlatforms.All)
        {
            Assert.True(SocialPlatformCapabilities.TryGet(platform, out var capabilities), platform);
            Assert.Equal(platform, capabilities.Platform);
        }
    }

    [Fact]
    public void An_unknown_platform_is_not_found_and_For_throws()
    {
        Assert.False(SocialPlatformCapabilities.TryGet("myspace", out _));
        Assert.False(SocialPlatformCapabilities.TryGet(null, out _));
        Assert.Throws<ArgumentException>(() => SocialPlatformCapabilities.For("myspace"));
    }

    [Fact]
    public void Facebook_reacts_with_like_only()
    {
        var facebook = SocialPlatformCapabilities.For(SocialPlatforms.Facebook);

        Assert.True(facebook.AllowsReaction(SocialReactionTypes.Like));
        Assert.False(facebook.AllowsReaction(SocialReactionTypes.Praise));
        Assert.Equal(new[] { SocialReactionTypes.Like }, facebook.ReactionTypes);
    }

    [Fact]
    public void LinkedIn_organization_reacts_with_six_types_and_rejects_MAYBE()
    {
        var linkedIn = SocialPlatformCapabilities.For(SocialPlatforms.LinkedInOrganization);

        Assert.Equal(6, linkedIn.ReactionTypes.Count);
        Assert.True(linkedIn.AllowsReaction(SocialReactionTypes.Entertainment));
        Assert.False(linkedIn.AllowsReaction("MAYBE"));
        Assert.False(linkedIn.AllowsReaction(null));
    }

    [Fact]
    public void LinkedIn_member_is_publish_only()
    {
        var member = SocialPlatformCapabilities.For(SocialPlatforms.LinkedInMember);

        Assert.False(member.CanReadComments);
        Assert.False(member.CanReplyToComments);
        Assert.False(member.CanReact);
        Assert.False(member.DeliversWebhooks);
        Assert.Equal(new[] { SocialPostKinds.Link }, member.PublishKinds);
    }

    [Fact]
    public void Instagram_publishes_images_only_and_cannot_react()
    {
        var instagram = SocialPlatformCapabilities.For(SocialPlatforms.Instagram);

        Assert.Equal(new[] { SocialPostKinds.Image }, instagram.PublishKinds);
        Assert.True(instagram.PostNeedsImageUrl);
        Assert.False(instagram.CanReact);
        Assert.True(instagram.CanReadComments);
        Assert.True(instagram.CanReplyToComments);
    }

    [Fact]
    public void Only_facebook_schedules_posts_between_ten_minutes_and_thirty_days()
    {
        var facebook = SocialPlatformCapabilities.For(SocialPlatforms.Facebook);

        Assert.True(facebook.CanSchedulePosts);
        Assert.Equal(TimeSpan.FromMinutes(10), facebook.SchedulingMinLeadTime);
        Assert.Equal(TimeSpan.FromDays(30), facebook.SchedulingMaxLeadTime);

        foreach (var platform in SocialPlatforms.All.Where(p => p != SocialPlatforms.Facebook))
            Assert.False(SocialPlatformCapabilities.For(platform).CanSchedulePosts, platform);
    }

    [Fact]
    public void Only_LinkedIn_organization_carries_retention_limits()
    {
        var organization = SocialPlatformCapabilities.For(SocialPlatforms.LinkedInOrganization);

        Assert.Equal(TimeSpan.FromHours(48), organization.CommentTextRetention);
        Assert.Equal(TimeSpan.FromHours(24), organization.CommenterProfileRetention);

        foreach (var platform in SocialPlatforms.All.Where(p => p != SocialPlatforms.LinkedInOrganization))
        {
            var other = SocialPlatformCapabilities.For(platform);
            Assert.Null(other.CommentTextRetention);
            Assert.Null(other.CommenterProfileRetention);
        }
    }

    [Fact]
    public void Every_publish_kind_is_a_known_kind()
    {
        foreach (var platform in SocialPlatforms.All)
            foreach (var kind in SocialPlatformCapabilities.For(platform).PublishKinds)
                Assert.Contains(kind, SocialPostKinds.All);
    }
}
