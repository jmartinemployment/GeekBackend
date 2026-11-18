using GeekAPI.Services.Social.LanguageModel;
using GeekApplication.Models.Social;

namespace GeekBackend.Tests.Social;

/// <summary>
/// Which platforms may send comment text to which AI provider. Nothing is cleared unless the operator
/// names the pair, a clearance for one provider clears nothing for another, and a name that is not a
/// pair, or the retired Claude-only setting, stops the service from starting instead of clearing
/// nothing quietly or the wrong company.
/// </summary>
public class SocialAiClearanceTests
{
    private static SocialAiClearance With(string? value, string? retired = null) =>
        SocialAiClearance.From(name => name switch
        {
            SocialAiClearance.Variable => value,
            SocialAiClearance.RetiredVariable => retired,
            _ => null,
        });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ,")]
    public void Unset_or_empty_clears_no_platform_for_any_provider(string? value)
    {
        var clearance = With(value);

        foreach (var provider in SocialModelProviders.All)
        {
            Assert.Empty(clearance.ClearedFor(provider));
            Assert.All(SocialPlatforms.All, platform => Assert.False(clearance.IsCleared(provider, platform)));
        }
    }

    [Fact]
    public void Only_the_named_pairs_are_cleared()
    {
        var clearance = With($" anthropic:{SocialPlatforms.Facebook} ,anthropic:{SocialPlatforms.Instagram}, openai : {SocialPlatforms.Facebook}");

        Assert.True(clearance.IsCleared(SocialModelProviders.Anthropic, SocialPlatforms.Facebook));
        Assert.True(clearance.IsCleared(SocialModelProviders.Anthropic, SocialPlatforms.Instagram));
        Assert.True(clearance.IsCleared(SocialModelProviders.OpenAi, SocialPlatforms.Facebook));
        Assert.False(clearance.IsCleared(SocialModelProviders.OpenAi, SocialPlatforms.Instagram));
        Assert.False(clearance.IsCleared(SocialModelProviders.Anthropic, SocialPlatforms.LinkedInOrganization));
        Assert.Equal(new[] { SocialPlatforms.Facebook, SocialPlatforms.Instagram }, clearance.ClearedFor(SocialModelProviders.Anthropic));
        Assert.Equal(new[] { SocialPlatforms.Facebook }, clearance.ClearedFor(SocialModelProviders.OpenAi));
    }

    [Fact]
    public void A_switch_to_a_provider_with_no_clearance_clears_nothing()
    {
        var clearance = With($"anthropic:{SocialPlatforms.Facebook}");

        foreach (var provider in SocialModelProviders.All.Where(p => p != SocialModelProviders.Anthropic))
        {
            Assert.False(clearance.IsCleared(provider, SocialPlatforms.Facebook));
            Assert.Empty(clearance.ClearedFor(provider));
        }
    }

    [Fact]
    public void No_provider_clears_nothing()
    {
        var clearance = With($"anthropic:{SocialPlatforms.Facebook}");

        Assert.False(clearance.IsCleared(null, SocialPlatforms.Facebook));
        Assert.Empty(clearance.ClearedFor(null));
        Assert.False(clearance.IsCleared(SocialModelProviders.Anthropic, null));
    }

    [Theory]
    [InlineData("facebook")]
    [InlineData("anthropic:facebok")]
    [InlineData("Anthropic:facebook")]
    [InlineData("claude:facebook")]
    [InlineData("anthropic:facebook:extra")]
    [InlineData("anthropic:facebook,linkedin")]
    public void An_entry_that_is_not_a_provider_and_platform_pair_throws_and_names_the_setting(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => With(value));

        Assert.Contains(SocialAiClearance.Variable, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("anthropic:facebook")]
    public void The_retired_claude_only_setting_throws_and_names_its_replacement(string? value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => With(value, retired: SocialPlatforms.Facebook));

        Assert.Contains(SocialAiClearance.RetiredVariable, ex.Message, StringComparison.Ordinal);
        Assert.Contains(SocialAiClearance.Variable, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hold_reason_names_the_provider_and_no_setting()
    {
        var reason = SocialAiClearance.HoldReason(SocialModelProviders.OpenAi, SocialPlatforms.Facebook);

        Assert.Contains("OpenAI", reason, StringComparison.Ordinal);
        Assert.Contains(SocialPlatforms.Facebook, reason, StringComparison.Ordinal);
        Assert.DoesNotContain("GEEK_SOCIAL", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("GEEK_SOCIAL", SocialAiClearance.HoldReason(null, SocialPlatforms.Facebook), StringComparison.Ordinal);
    }
}
