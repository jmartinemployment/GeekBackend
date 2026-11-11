using GeekAPI.Services.Social.LanguageModel;
using GeekApplication.Models.Social;

namespace GeekBackend.Tests.Social;

/// <summary>
/// Which platforms may send comment text to Claude. Nothing is cleared unless Jeff names it, and a name
/// that is not a platform stops the service from starting instead of clearing nothing quietly.
/// </summary>
public class SocialClaudeClearanceTests
{
    private static SocialClaudeClearance With(string? value) =>
        SocialClaudeClearance.From(name => name == SocialClaudeClearance.Variable ? value : null);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ,")]
    public void Unset_or_empty_clears_no_platform(string? value)
    {
        var clearance = With(value);

        Assert.Empty(clearance.Cleared);
        Assert.All(SocialPlatforms.All, platform => Assert.False(clearance.IsCleared(platform)));
    }

    [Fact]
    public void Only_the_named_platforms_are_cleared()
    {
        var clearance = With($" {SocialPlatforms.Facebook} ,{SocialPlatforms.Instagram}");

        Assert.True(clearance.IsCleared(SocialPlatforms.Facebook));
        Assert.True(clearance.IsCleared(SocialPlatforms.Instagram));
        Assert.False(clearance.IsCleared(SocialPlatforms.LinkedInOrganization));
        Assert.False(clearance.IsCleared(null));
        Assert.Equal(new[] { SocialPlatforms.Facebook, SocialPlatforms.Instagram }, clearance.Cleared);
    }

    [Theory]
    [InlineData("facebok")]
    [InlineData("Facebook")]
    [InlineData("facebook,linkedin")]
    public void A_name_that_is_not_a_platform_throws_and_names_the_setting(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => With(value));

        Assert.Contains(SocialClaudeClearance.Variable, ex.Message, StringComparison.Ordinal);
    }
}
