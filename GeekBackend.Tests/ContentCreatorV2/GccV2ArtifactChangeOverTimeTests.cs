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

    [Fact]
    public void SubjectKeyFromInput_uses_subject_page_url()
    {
        var key = GccV2ArtifactChangeOverTime.SubjectKeyFromInput(
            """
            {
              "subjectPages": [
                { "source": { "url": "https://Brand.Example/Docs/" }, "visibleContent": "# Brand" }
              ]
            }
            """);
        Assert.Equal("https://brand.example/docs", key);
    }

    [Fact]
    public void TryReadFindingSnapshot_reads_prioritized_actions_and_gaps()
    {
        var ok = GccV2ArtifactChangeOverTime.TryReadFindingSnapshot(
            """
            {
              "prioritizedActions": [
                {
                  "actionId": "a1",
                  "priority": "high",
                  "dimension": "proof",
                  "action": "Add verified proof"
                }
              ],
              "contentGap": {
                "gaps": [
                  { "gapId": "g1", "dimension": "pricing", "status": "supportedGap" }
                ]
              }
            }
            """,
            out var findings);

        Assert.True(ok);
        Assert.Equal("high", findings["action:a1"].Priority);
        Assert.Equal("supportedGap", findings["gap:g1"].Priority);
    }

    [Fact]
    public void CompareFindings_reports_added_removed_and_priority_shifts_without_scores()
    {
        var current = new Dictionary<string, GccV2ArtifactChangeOverTime.FindingSnapshot>
        {
            ["action:a1"] = new("high", "Add verified proof"),
            ["action:a2"] = new("medium", "Clarify pricing"),
        };
        var prior = new Dictionary<string, GccV2ArtifactChangeOverTime.FindingSnapshot>
        {
            ["action:a1"] = new("medium", "Add verified proof"),
            ["action:old"] = new("low", "Old FAQ action"),
        };

        var result = GccV2ArtifactChangeOverTime.CompareFindings(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DateTimeOffset.Parse("2026-09-10T12:00:00Z"),
            "https://brand.example/docs",
            current,
            prior);

        Assert.True(result.Available);
        Assert.Null(result.OverallDelta);
        Assert.Null(result.CurrentOverall);
        Assert.Contains(result.Findings, f => f.Change == "added" && f.Key == "action:a2");
        Assert.Contains(result.Findings, f => f.Change == "removed" && f.Key == "action:old");
        Assert.Contains(result.Findings, f =>
            f.Change == "priorityChanged"
            && f.Key == "action:a1"
            && f.CurrentPriority == "high"
            && f.PriorPriority == "medium");
        Assert.Contains("added", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("priority shifted", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overall score", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
