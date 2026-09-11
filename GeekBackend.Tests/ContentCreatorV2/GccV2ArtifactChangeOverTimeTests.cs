using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2ArtifactChangeOverTimeTests
{
    [Fact]
    public void SubjectKeyFromInput_prefers_page_url()
    {
        var key = GccV2ArtifactChangeOverTime.SubjectKeyFromInput(
            """{"pageUrl":"https://Example.com/Docs/","document":{"bodyMarkdown":"x"}}""");
        Assert.Equal("https://example.com/docs", key);
    }

    [Fact]
    public void SubjectKeyFromInput_hashes_body_when_no_url()
    {
        var a = GccV2ArtifactChangeOverTime.SubjectKeyFromInput(
            """{"document":{"bodyMarkdown":"# Same page\n\nEvidence."}}""");
        var b = GccV2ArtifactChangeOverTime.SubjectKeyFromInput(
            """{"document":{"bodyMarkdown":"# Same page\n\nEvidence."}}""");
        var c = GccV2ArtifactChangeOverTime.SubjectKeyFromInput(
            """{"document":{"bodyMarkdown":"# Different"}}""");
        Assert.StartsWith("body:", a);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void TryReadScoreSnapshot_reads_overall_and_dimensions()
    {
        var ok = GccV2ArtifactChangeOverTime.TryReadScoreSnapshot(
            """
            {
              "overallScore": 82,
              "dimensions": [
                { "dimension": "headingHierarchy", "score": 70 },
                { "dimension": "technicalCrawlabilityPerformance", "score": null }
              ]
            }
            """,
            out var overall,
            out var dimensions);

        Assert.True(ok);
        Assert.Equal(82, overall);
        Assert.Equal(70, dimensions["headingHierarchy"]);
        Assert.Null(dimensions["technicalCrawlabilityPerformance"]);
    }

    [Fact]
    public void Compare_reports_overall_and_dimension_deltas()
    {
        var result = GccV2ArtifactChangeOverTime.Compare(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DateTimeOffset.Parse("2026-09-10T12:00:00Z"),
            "https://example.com",
            84,
            new Dictionary<string, double?> { ["headingHierarchy"] = 80 },
            78,
            new Dictionary<string, double?> { ["headingHierarchy"] = 70 });

        Assert.True(result.Available);
        Assert.Equal(6, result.OverallDelta);
        Assert.Equal(10, result.Dimensions[0].Delta);
        Assert.Contains("up", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_unavailable_without_prior()
    {
        var result = GccV2ArtifactChangeOverTime.Compare(
            null, null, "https://example.com", 82, [], null, []);
        Assert.False(result.Available);
        Assert.Contains("No prior", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
