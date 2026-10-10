using GeekAPI.Services.Gcw;

namespace GeekBackend.Tests;

public sealed class GcwContentTypeScoringTests
{
    [Theory]
    [InlineData("comparison", true, true)]
    [InlineData("guide", true, true)]
    [InlineData("whitepaper", true, false)]
    public void Scoring_profile_matches_content_type(string contentType, bool longForm, bool expectsFaq)
    {
        Assert.Equal(longForm, GcwContentTypeScoring.IsLongForm(contentType));
        Assert.Equal(expectsFaq, GcwContentTypeScoring.ExpectsFaqSection(contentType));
    }

    [Theory]
    [InlineData("email", false, false)]
    [InlineData("social", false, false)]
    [InlineData("pillar", true, true)]
    [InlineData("blog", true, true)]
    [InlineData("tool", true, false)]
    public void Scoring_profile_matches_legacy_types(string contentType, bool longForm, bool expectsFaq)
    {
        Assert.Equal(longForm, GcwContentTypeScoring.IsLongForm(contentType));
        Assert.Equal(expectsFaq, GcwContentTypeScoring.ExpectsFaqSection(contentType));
    }

    [Fact]
    public void Seo_analyzer_skips_length_checks_for_short_form()
    {
        const string json = """
            {"lede":"Short post about CRM tools.","sections":[{"heading":"Takeaway","paragraphs":[{"$type":"text","runs":[{"text":"Keep it brief."}]}]}]}
            """;
        var report = GcwSeoAnalyzer.Analyze(json, "crm tools", "social");
        Assert.DoesNotContain(report.Checks, c => c.Id == "word-count");
        Assert.DoesNotContain(report.Checks, c => c.Id == "section-count");
    }
}
