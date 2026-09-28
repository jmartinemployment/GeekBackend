using GeekAPI.Services.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A topic is context plus keyword, split on a colon (Jeff, 2026-09-28). Everything that scored or
/// matched on "the keyword" used the whole string, so the SEO report asked whether the lede
/// contained the context and the keyword verbatim -- which no readable sentence does.
/// </summary>
public class GccTargetKeywordTests
{
    [Theory]
    [InlineData("Accounts Payable: Automated Data Entry & Processing", "Automated Data Entry & Processing")]
    [InlineData("Marketing: Intelligent Lead Capture", "Intelligent Lead Capture")]
    [InlineData("  Accounting:   Automated Accounts Payable  ", "Automated Accounts Payable")]
    public void The_keyword_is_the_half_after_the_colon(string topic, string expected)
    {
        Assert.Equal(expected, GccTargetKeyword.FromTopic(topic));
    }

    [Theory]
    [InlineData("Automated Data Entry & Processing")]
    [InlineData("Accounts Payable Automation")]
    public void A_topic_with_no_colon_is_the_keyword(string topic)
    {
        Assert.Equal(topic, GccTargetKeyword.FromTopic(topic));
    }

    [Fact]
    public void A_one_word_tail_is_not_a_keyword()
    {
        // "Marketing: AI" is a topic ending in a colon, not a one-word keyword, and scoring against
        // "AI" would pass on any draft that used the letters anywhere.
        Assert.Equal("Marketing: AI", GccTargetKeyword.FromTopic("Marketing: AI"));
    }

    [Fact]
    public void A_trailing_colon_leaves_the_topic_alone()
    {
        Assert.Equal("Accounts Payable:", GccTargetKeyword.FromTopic("Accounts Payable:"));
    }

    [Fact]
    public void The_last_colon_wins_when_there_are_several()
    {
        Assert.Equal("Automated Data Entry", GccTargetKeyword.FromTopic("Accounting: AP: Automated Data Entry"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_in_nothing_out(string? topic)
    {
        Assert.Equal("", GccTargetKeyword.FromTopic(topic));
    }
}
