using GeekAPI.Services.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The must-mention block is written by the controller and read back by the provenance evidence, so
/// format and parse are one file. Two implementations of the same bullet shape is the drift that
/// produced the failure this fixes.
/// </summary>
public class GccMustMentionTests
{
    [Fact]
    public void Subtopics_survive_the_round_trip()
    {
        var block = GccMustMention.Format(
            "Automated Accounts Payable",
            "https://geek.test/use-cases/accounting",
            ["Invoice capture", "Approval routing", "Payment execution"]);

        Assert.Equal(
            ["Invoice capture", "Approval routing", "Payment execution"],
            GccMustMention.Subtopics(block));
    }

    [Fact]
    public void The_sentence_that_introduces_the_list_is_not_a_subtopic()
    {
        // It is what the model mistook for one: "brief:Subtopics the site already treats under it,
        // all of which this piece must cover".
        var block = GccMustMention.Format("Heading", "https://geek.test", ["Invoice capture"]);

        Assert.DoesNotContain(
            GccMustMention.Subtopics(block),
            s => s.StartsWith("Subtopics the site", StringComparison.Ordinal));
    }

    [Fact]
    public void A_match_with_no_children_still_formats_and_yields_nothing()
    {
        var block = GccMustMention.Format("Heading", "https://geek.test", []);

        Assert.Contains("THIS SITE ALREADY COVERS THIS TOPIC", block, StringComparison.Ordinal);
        Assert.Empty(GccMustMention.Subtopics(block));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some unrelated prose")]
    public void Nothing_to_read_is_no_subtopics_rather_than_a_guess(string? block)
    {
        Assert.Empty(GccMustMention.Subtopics(block));
    }
}
