using GeekAPI.Services.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Topic is a descriptor and a keyword, split on the first colon (Jeff, 2026-10-01).
/// </summary>
public class GccTopicTests
{
    [Fact]
    public void The_descriptor_types_the_keyword()
    {
        var parts = GccTopic.Parse("Accounts Payable: Automated Data Entry & Processing");

        Assert.Equal("Accounts Payable", parts.Descriptor);
        Assert.Equal("Automated Data Entry & Processing", parts.Keyword);
        Assert.Equal("Accounts Payable: Automated Data Entry & Processing", parts.Qualified);
    }

    [Fact]
    public void No_colon_means_the_whole_thing_is_the_keyword()
    {
        // Not a failure and not a guessed descriptor: the topic simply carries no type qualifier.
        var parts = GccTopic.Parse("Invoice Capture Software");

        Assert.Equal("", parts.Descriptor);
        Assert.Equal("Invoice Capture Software", parts.Keyword);
        Assert.Equal("Invoice Capture Software", parts.Qualified);
    }

    [Fact]
    public void Only_the_first_colon_splits()
    {
        var parts = GccTopic.Parse("Accounts Payable: Automation: Data Entry");

        Assert.Equal("Accounts Payable", parts.Descriptor);
        Assert.Equal("Automation: Data Entry", parts.Keyword);
    }

    [Theory]
    [InlineData("Pricing:")]
    [InlineData(": Automated Data Entry")]
    [InlineData(":")]
    public void A_colon_with_nothing_usable_either_side_is_not_a_split(string topic)
    {
        // "Pricing:" is a keyword that happens to end in a colon, not a descriptor with no keyword.
        var parts = GccTopic.Parse(topic);

        Assert.Equal("", parts.Descriptor);
        Assert.Equal(topic.Trim(), parts.Keyword);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_in_is_nothing_out(string? topic)
    {
        var parts = GccTopic.Parse(topic);

        Assert.Equal("", parts.Descriptor);
        Assert.Equal("", parts.Keyword);
    }

    [Fact]
    public void Surrounding_whitespace_is_not_part_of_either_field()
    {
        var parts = GccTopic.Parse("  Accounts Payable :  Automated Data Entry  ");

        Assert.Equal("Accounts Payable", parts.Descriptor);
        Assert.Equal("Automated Data Entry", parts.Keyword);
    }

    [Fact]
    public void KeywordOf_is_the_shorthand_a_problem_frame_wants()
    {
        Assert.Equal(
            "Automated Data Entry & Processing",
            GccTopic.KeywordOf("Accounts Payable: Automated Data Entry & Processing"));
    }
}
