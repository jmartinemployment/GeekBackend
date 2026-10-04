using GeekAPI.Services.Workflow.Services.PromptBuilders;
using Xunit;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// The tool body's case-study rule states what the extraction holds.
/// </summary>
/// <remarks>
/// It said "there is no case-study data available" on every tool page, while PARTNER DATA in the same
/// prompt carried the extraction's caseStudies -- the writer told to ignore the only customer evidence
/// it had.
/// </remarks>
public class ToolCaseStudyRuleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("""{"caseStudies":[]}""")]
    [InlineData("""{"citables":[]}""")]
    [InlineData("not json")]
    public void No_case_studies_in_the_extraction_is_the_rule_that_there_are_none(string? extraction) =>
        Assert.StartsWith(
            "CRITICAL: there is no case-study data available",
            ContentPromptBuilder.ToolCaseStudyRule(extraction),
            StringComparison.Ordinal);

    [Fact]
    public void Case_studies_in_the_extraction_are_the_only_ones_that_may_be_reported()
    {
        var rule = ContentPromptBuilder.ToolCaseStudyRule(
            """{"caseStudies":[{"clientName":"Acme"},{"clientName":"Globex"}]}""");

        Assert.Contains("the only case studies you may report are the 2 in PARTNER DATA", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("no case-study data", rule, StringComparison.Ordinal);
    }
}
