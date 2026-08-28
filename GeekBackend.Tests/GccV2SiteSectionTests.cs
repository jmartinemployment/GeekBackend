using GeekAPI.Services.ContentCreatorV2;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests;

public class GccV2SiteSectionTests
{
    [Fact]
    public void ParseSiteSection_round_trips_related_pages()
    {
        var json = """
            {
              "siteAnalysisProfileId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "gapTopic": "AI consulting",
              "gapSectionPath": "Services",
              "relatedPages": [
                {
                  "url": "https://example.com/services",
                  "title": "Services",
                  "headings": [{ "level": 1, "text": "Services" }],
                  "excerpt": ""
                }
              ],
              "topicalNeighbors": ["About", "Contact"]
            }
            """;

        var section = GccV2SiteSection.ParseSiteSection(json);
        Assert.NotNull(section);
        Assert.Equal("AI consulting", section!.GapTopic);
        Assert.Single(section.RelatedPages);
        Assert.Equal("https://example.com/services", section.RelatedPages[0].Url);
    }

    [Fact]
    public void ValidateSiteSectionGate_requires_profile_id()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GccV2SiteSection.ValidateSiteSectionGate(null, null));
        Assert.Contains("site analysis required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSiteSectionGate_requires_related_pages_when_section_present()
    {
        var section = new SiteSectionContextDto(
            Guid.NewGuid(),
            "topic",
            null,
            [],
            ["Neighbor"]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GccV2SiteSection.ValidateSiteSectionGate(Guid.NewGuid(), section));
        Assert.Contains("relatedPages", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildSectionContext_returns_null_without_neighbors()
    {
        var payload = new SiteAnalysisStoredPayload(
            [new ContentGapDto("1", "AI", "Services", "missing")],
            [new RelatedPageDto("https://example.com/s", "Services", [new HeadingDto(1, "Services")], "")],
            []);

        Assert.Null(GccV2SiteSection.TryBuildSectionContext(Guid.NewGuid(), payload, "AI"));
    }

    [Fact]
    public void TryBuildSectionContext_builds_section_for_gap_topic()
    {
        var id = Guid.NewGuid();
        var payload = new SiteAnalysisStoredPayload(
            [new ContentGapDto("1", "AI consulting", "Services", "missing")],
            [new RelatedPageDto("https://example.com/s", "Services", [new HeadingDto(1, "Services")], "")],
            ["About"]);

        var section = GccV2SiteSection.TryBuildSectionContext(id, payload, "AI consulting");
        Assert.NotNull(section);
        Assert.Equal(id, section!.SiteAnalysisId);
        Assert.Single(section.RelatedPages);
        Assert.NotNull(section.InformationGain);
    }

    [Theory]
    [InlineData("https://example.com/tools/foo", true)]
    [InlineData("/tools/foo", true)]
    [InlineData("https://example.com/blog/post", false)]
    public void HrefLooksLikeOnSiteToolPage_detects_tools_path(string href, bool expected) =>
        Assert.Equal(expected, GccV2SiteSection.HrefLooksLikeOnSiteToolPage(href));
}
