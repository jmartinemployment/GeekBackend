using GeekAPI.Services.ContentCreatorV2.Competitor;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Information Gain must distinguish "not computed" from "computed, nothing found" — treating the
/// first as the second is what let the model assume there was nothing to differentiate against.
/// </summary>
public sealed class GccV2InformationGainTests
{
    private static GccCompetitorExtractionProvenance Prov(string url) =>
        new(url, GccCompetitorExtractionDocument.CrawlTypeCompetitor,
            RunId: "run-1", PageId: "page-1", SectionTitle: null, SourceDigest: null,
            TemporalAnchorUtc: null);

    private static InformationGainNote BaseNote() =>
        new(["https://ours.example/guide: Our guide · Setup"], [], "not computed yet");

    [Fact]
    public void Null_competitor_extraction_leaves_note_unchanged()
    {
        var note = BaseNote();
        var enriched = GccV2InformationGain.Enrich(note, null);

        Assert.NotNull(enriched);
        Assert.Empty(enriched!.CompetitorOpens);
        Assert.Equal("not computed yet", enriched.Summary);
    }

    [Fact]
    public void Gap_map_becomes_competitor_opens()
    {
        var competitor = GccV2CompetitorExtractionService.EmptyDocument() with
        {
            GapMap =
            [
                new GccCompetitorGapMapAsset(
                    "Localised compliance", "shallow", "Publish the deep guide",
                    Prov("https://rival.example/compliance")),
            ],
        };

        var enriched = GccV2InformationGain.Enrich(BaseNote(), competitor);

        Assert.NotNull(enriched);
        Assert.Contains(enriched!.CompetitorOpens, o => o.Contains("Localised compliance"));
        Assert.Contains("open topic", enriched.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rival_coverage_on_our_own_host_is_not_an_open()
    {
        var competitor = GccV2CompetitorExtractionService.EmptyDocument() with
        {
            CoverageMap =
            [
                new GccCompetitorCoverageAsset(
                    "Setup", "deep", [], "https://ours.example/guide",
                    Prov("https://ours.example/guide")),
            ],
        };

        var enriched = GccV2InformationGain.Enrich(BaseNote(), competitor);

        Assert.NotNull(enriched);
        Assert.Empty(enriched!.CompetitorOpens);
    }

    [Fact]
    public void Computed_but_empty_says_so_rather_than_implying_not_run()
    {
        var enriched = GccV2InformationGain.Enrich(
            BaseNote(), GccV2CompetitorExtractionService.EmptyDocument());

        Assert.NotNull(enriched);
        Assert.Empty(enriched!.CompetitorOpens);
        Assert.Contains("found no open topics", enriched.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not computed", enriched.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Crawl_time_note_never_claims_no_gaps_and_never_asks_for_a_serp_upload()
    {
        // The V2 path cannot accept a saved SERP; the old summary told operators to upload one.
        var note = BaseNote();
        var enriched = GccV2InformationGain.Enrich(note, null);

        Assert.NotNull(enriched);
        Assert.DoesNotContain("Upload a saved SERP", enriched!.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
