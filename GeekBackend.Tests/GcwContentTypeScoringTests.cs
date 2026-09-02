using GeekAPI.Services.ContentCreatorV2.Geo;
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

    [Fact]
    public void Geo_analyzer_skips_faq_check_for_tool_pages()
    {
        const string json = """
            {"lede":"Tool guide intro.","sections":[{"heading":"Overview","paragraphs":[{"$type":"text","runs":[{"text":"A long enough standalone paragraph about the platform with concrete details and implementation notes for teams evaluating options in the market today."}]}]}]}
            """;
        var report = GccV2GeoAnalyzer.Analyze(json, "mailchimp", "tool");
        Assert.DoesNotContain(report.Checks, c => c.Id == "faq-or-direct-answers");
    }
}
