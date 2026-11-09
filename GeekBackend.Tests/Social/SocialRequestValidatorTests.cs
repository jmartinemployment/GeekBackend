using GeekApplication.Models.Social;
using GeekApplication.Results;
using Xunit;

namespace GeekBackend.Tests.Social;

public class SocialRequestValidatorTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private static SocialPostRequest Link(string? title = "A title") => new()
    {
        Kind = SocialPostKinds.Link,
        Text = "Read this.",
        LinkUrl = "https://geekatyourspot.com/blog/example",
        LinkTitle = title,
    };

    private static SocialPostRequest Image() => new()
    {
        Kind = SocialPostKinds.Image,
        Text = "A caption.",
        ImageUrl = "https://geekatyourspot.com/images/example.jpg",
    };

    private static void AssertFails(Result result, string code)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    // --- publish ---------------------------------------------------------------------------

    [Fact]
    public void A_link_post_with_a_link_is_valid_on_facebook()
    {
        Assert.True(SocialPostRequestValidator.Validate(SocialPlatforms.Facebook, Link(title: null), Now).IsSuccess);
    }

    [Theory]
    [InlineData(SocialPlatforms.LinkedInOrganization)]
    [InlineData(SocialPlatforms.LinkedInMember)]
    public void A_LinkedIn_link_post_needs_a_title(string platform)
    {
        AssertFails(SocialPostRequestValidator.Validate(platform, Link(title: " "), Now), SocialFailureCodes.InvalidRequest);
        Assert.True(SocialPostRequestValidator.Validate(platform, Link(), Now).IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/relative/path")]
    [InlineData("http://geekatyourspot.com/insecure")]
    public void A_link_post_needs_an_absolute_https_link(string? url)
    {
        var request = Link();
        request.LinkUrl = url;

        AssertFails(SocialPostRequestValidator.Validate(SocialPlatforms.Facebook, request, Now), SocialFailureCodes.InvalidRequest);
    }

    [Fact]
    public void Instagram_has_no_link_posts()
    {
        var result = SocialPostRequestValidator.Validate(SocialPlatforms.Instagram, Link(), Now);

        AssertFails(result, SocialFailureCodes.NotSupported);
        Assert.Contains("image", result.Error);
    }

    [Fact]
    public void Instagram_needs_a_public_https_image()
    {
        Assert.True(SocialPostRequestValidator.Validate(SocialPlatforms.Instagram, Image(), Now).IsSuccess);

        var noImage = Image();
        noImage.ImageUrl = null;
        AssertFails(SocialPostRequestValidator.Validate(SocialPlatforms.Instagram, noImage, Now), SocialFailureCodes.InvalidRequest);

        var insecure = Image();
        insecure.ImageUrl = "http://geekatyourspot.com/images/example.jpg";
        AssertFails(SocialPostRequestValidator.Validate(SocialPlatforms.Instagram, insecure, Now), SocialFailureCodes.InvalidRequest);
    }

    [Fact]
    public void An_image_post_is_not_supported_where_the_matrix_has_not_verified_it()
    {
        AssertFails(SocialPostRequestValidator.Validate(SocialPlatforms.Facebook, Image(), Now), SocialFailureCodes.NotSupported);
        AssertFails(SocialPostRequestValidator.Validate(SocialPlatforms.LinkedInOrganization, Image(), Now), SocialFailureCodes.NotSupported);
    }

    [Fact]
    public void A_link_post_may_carry_an_optional_thumbnail_but_it_must_be_https()
    {
        var request = Link();
        request.ImageUrl = "http://geekatyourspot.com/thumb.png";

        AssertFails(SocialPostRequestValidator.Validate(SocialPlatforms.LinkedInOrganization, request, Now), SocialFailureCodes.InvalidRequest);

        request.ImageUrl = "https://geekatyourspot.com/thumb.png";
        Assert.True(SocialPostRequestValidator.Validate(SocialPlatforms.LinkedInOrganization, request, Now).IsSuccess);
    }

    [Fact]
    public void An_unknown_platform_or_kind_is_invalid()
    {
        AssertFails(SocialPostRequestValidator.Validate("myspace", Link(), Now), SocialFailureCodes.InvalidRequest);

        var request = Link();
        request.Kind = "story";
        AssertFails(SocialPostRequestValidator.Validate(SocialPlatforms.Facebook, request, Now), SocialFailureCodes.InvalidRequest);
    }

    [Fact]
    public void Facebook_schedules_between_ten_minutes_and_thirty_days_in_UTC()
    {
        var request = Link();

        request.ScheduledFor = Now.AddMinutes(9);
        AssertFails(SocialPostRequestValidator.Validate(SocialPlatforms.Facebook, request, Now), SocialFailureCodes.InvalidRequest);

        request.ScheduledFor = Now.AddMinutes(10);
        Assert.True(SocialPostRequestValidator.Validate(SocialPlatforms.Facebook, request, Now).IsSuccess);

        request.ScheduledFor = Now.AddDays(30);
        Assert.True(SocialPostRequestValidator.Validate(SocialPlatforms.Facebook, request, Now).IsSuccess);

        request.ScheduledFor = Now.AddDays(30).AddMinutes(1);
        AssertFails(SocialPostRequestValidator.Validate(SocialPlatforms.Facebook, request, Now), SocialFailureCodes.InvalidRequest);

        request.ScheduledFor = DateTime.SpecifyKind(Now.AddHours(2), DateTimeKind.Unspecified);
        AssertFails(SocialPostRequestValidator.Validate(SocialPlatforms.Facebook, request, Now), SocialFailureCodes.InvalidRequest);
    }

    [Fact]
    public void A_platform_without_scheduling_refuses_a_scheduled_post()
    {
        var request = Link();
        request.ScheduledFor = Now.AddDays(1);

        AssertFails(SocialPostRequestValidator.Validate(SocialPlatforms.LinkedInOrganization, request, Now), SocialFailureCodes.NotSupported);
    }

    [Fact]
    public void A_null_request_throws()
    {
        Assert.Throws<ArgumentNullException>(() => SocialPostRequestValidator.Validate(SocialPlatforms.Facebook, null!, Now));
    }

    // --- reply -----------------------------------------------------------------------------

    [Theory]
    [InlineData(SocialPlatforms.Facebook)]
    [InlineData(SocialPlatforms.Instagram)]
    [InlineData(SocialPlatforms.LinkedInOrganization)]
    public void A_reply_is_valid_where_the_agent_can_reply(string platform)
    {
        var request = new SocialReplyRequest { PlatformPostId = "p1", ParentPlatformCommentId = "c1", Text = "Thank you." };

        Assert.True(SocialReplyRequestValidator.Validate(platform, request).IsSuccess);
    }

    [Fact]
    public void A_LinkedIn_member_cannot_reply()
    {
        var request = new SocialReplyRequest { PlatformPostId = "p1", ParentPlatformCommentId = "c1", Text = "Thank you." };

        AssertFails(SocialReplyRequestValidator.Validate(SocialPlatforms.LinkedInMember, request), SocialFailureCodes.NotSupported);
    }

    [Theory]
    [InlineData("", "c1", "text")]
    [InlineData("p1", " ", "text")]
    [InlineData("p1", "c1", "  ")]
    public void A_reply_needs_a_post_a_comment_and_text(string post, string comment, string text)
    {
        var request = new SocialReplyRequest { PlatformPostId = post, ParentPlatformCommentId = comment, Text = text };

        AssertFails(SocialReplyRequestValidator.Validate(SocialPlatforms.Facebook, request), SocialFailureCodes.InvalidRequest);
    }

    // --- reaction --------------------------------------------------------------------------

    private static SocialReactionRequest Reaction(string type) => new()
    {
        TargetKind = SocialTargetKinds.Comment,
        PlatformTargetId = "c1",
        ReactionType = type,
    };

    [Fact]
    public void Facebook_accepts_LIKE_and_refuses_every_other_reaction()
    {
        Assert.True(SocialReactionRequestValidator.Validate(SocialPlatforms.Facebook, Reaction(SocialReactionTypes.Like)).IsSuccess);

        var result = SocialReactionRequestValidator.Validate(SocialPlatforms.Facebook, Reaction(SocialReactionTypes.Praise));
        AssertFails(result, SocialFailureCodes.NotSupported);
        Assert.Contains("LIKE", result.Error);
    }

    [Fact]
    public void LinkedIn_organization_accepts_its_six_reactions_and_refuses_MAYBE()
    {
        foreach (var type in SocialPlatformCapabilities.For(SocialPlatforms.LinkedInOrganization).ReactionTypes)
            Assert.True(SocialReactionRequestValidator.Validate(SocialPlatforms.LinkedInOrganization, Reaction(type)).IsSuccess, type);

        AssertFails(SocialReactionRequestValidator.Validate(SocialPlatforms.LinkedInOrganization, Reaction("MAYBE")), SocialFailureCodes.NotSupported);
    }

    [Theory]
    [InlineData(SocialPlatforms.Instagram)]
    [InlineData(SocialPlatforms.LinkedInMember)]
    public void A_platform_with_no_reactions_refuses_every_one(string platform)
    {
        AssertFails(SocialReactionRequestValidator.Validate(platform, Reaction(SocialReactionTypes.Like)), SocialFailureCodes.NotSupported);
    }

    [Fact]
    public void A_reaction_needs_a_known_target_kind_and_a_target_id()
    {
        var badKind = Reaction(SocialReactionTypes.Like);
        badKind.TargetKind = "profile";
        AssertFails(SocialReactionRequestValidator.Validate(SocialPlatforms.Facebook, badKind), SocialFailureCodes.InvalidRequest);

        var noTarget = Reaction(SocialReactionTypes.Like);
        noTarget.PlatformTargetId = "";
        AssertFails(SocialReactionRequestValidator.Validate(SocialPlatforms.Facebook, noTarget), SocialFailureCodes.InvalidRequest);
    }
}
