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

    [Fact]
    public void A_subtopic_that_lists_tools_is_not_something_the_draft_must_cover()
    {
        // The 2026-10-07 run was told to cover "Top 5 Automated Approval Workflow Tools:" and refused
        // for writing a section like it. The tools are entered in the brief form, not taken from the
        // site's own list.
        var block = GccMustMention.Format(
            "Automated Approval Workflows",
            "https://geek.test/use-cases/accounting",
            ["Invoice capture", "Top 5 Automated Approval Workflow Tools:", "Approval routing", "7 Tools to Consider"]);

        Assert.Equal(["Invoice capture", "Approval routing"], GccMustMention.Subtopics(block));
        Assert.DoesNotContain("Top 5", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Tools to Consider", block, StringComparison.Ordinal);
    }

    [Fact]
    public void When_every_subtopic_lists_tools_the_block_has_no_subtopics_line_at_all()
    {
        var block = GccMustMention.Format("Heading", "https://geek.test", ["Top 5 Automated Approval Workflow Tools:"]);

        Assert.Empty(GccMustMention.Subtopics(block));
        Assert.DoesNotContain("all of which this piece must cover", block, StringComparison.Ordinal);
        Assert.Contains("THIS SITE ALREADY COVERS THIS TOPIC", block, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Top 5 Automated Approval Workflow Tools:", true)]
    [InlineData("Best Accounts Payable Tools", true)]
    [InlineData("7 Tools to Consider", true)]
    [InlineData("How AI Tools Simplify Your Accounts Payable Process", false)]
    [InlineData("Choosing the Right Tools", true)]
    [InlineData("Approval routing", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_heading_that_enumerates_tools_is_told_apart_from_prose_about_using_them(string? heading, bool enumerates)
    {
        Assert.Equal(enumerates, GeekAPI.Services.ContentCreator.Guardrail.GccToolsSectionGuard.EnumeratesTools(heading));
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
