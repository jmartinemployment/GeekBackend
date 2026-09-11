using System.Text.Json;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Cross-language camelCase goldens shared with Geek-Crawler-Rag intelligence artifacts.
/// </summary>
public sealed class GccV2IntelligenceArtifactContractTests
{
    private static readonly string FixtureDir = Path.Combine(
        AppContext.BaseDirectory, "ContentCreatorV2", "Fixtures");

    private static readonly JsonDocumentOptions DocOpts = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
    };

    [Theory]
    [InlineData("queryPlan.v1", "queries", "methodology")]
    [InlineData("competitorPageAnalysis.v1", "dimensions", "opportunities")]
    [InlineData("contentGapAnalysis.v1", "gaps", "dimensions")]
    [InlineData("readinessComparison.v1", "competitors", "dimensionDeltas")]
    [InlineData("competitorAudit.v1", "pageAnalyses", "prioritizedActions")]
    [InlineData("competitorPositioning.v1", "observations", "perceptionGaps")]
    public void Golden_fixture_exposes_required_camelCase_shape(
        string artifactType, string requiredA, string requiredB)
    {
        using var document = LoadGolden(artifactType);
        var root = document.RootElement;
        Assert.Equal(artifactType, root.GetProperty("artifactType").GetString());
        Assert.True(root.TryGetProperty(requiredA, out _), $"missing {requiredA}");
        Assert.True(root.TryGetProperty(requiredB, out _), $"missing {requiredB}");
        Assert.True(root.TryGetProperty("warnings", out var warnings)
            && warnings.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public void Query_plan_golden_has_scored_queries_and_methodology()
    {
        using var document = LoadGolden("queryPlan.v1");
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("queries", out var queries) && queries.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("methodology", out var methodology)
            && methodology.TryGetProperty("methodologyId", out var id)
            && id.GetString() == "query-priority-heuristic.v1");
        foreach (var query in queries.EnumerateArray())
        {
            Assert.True(query.TryGetProperty("query", out _));
            Assert.True(query.TryGetProperty("priorityScore", out _));
            Assert.True(query.TryGetProperty("provenance", out _));
        }
    }

    [Fact]
    public void Readiness_comparison_golden_exposes_deltas_and_rubric()
    {
        using var document = LoadGolden("readinessComparison.v1");
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("dimensionDeltas", out var deltas)
            && deltas.ValueKind == JsonValueKind.Array);
        Assert.True(root.TryGetProperty("competitors", out var competitors)
            && competitors.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("rubricVersion", out _));
    }

    [Fact]
    public void Competitor_audit_golden_nests_page_and_gap_artifacts()
    {
        using var document = LoadGolden("competitorAudit.v1");
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("pageAnalyses", out var pages)
            && pages.GetArrayLength() > 0);
        Assert.Equal("competitorPageAnalysis.v1",
            pages[0].GetProperty("artifactType").GetString());
        Assert.True(root.TryGetProperty("contentGap", out var gap));
        Assert.Equal("contentGapAnalysis.v1", gap.GetProperty("artifactType").GetString());
        Assert.True(root.TryGetProperty("prioritizedActions", out var actions)
            && actions.GetArrayLength() > 0);
    }

    [Fact]
    public void Content_gap_golden_links_gaps_to_evidence_ids()
    {
        using var document = LoadGolden("contentGapAnalysis.v1");
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("gaps", out var gaps) && gaps.GetArrayLength() > 0);
        foreach (var gap in gaps.EnumerateArray())
        {
            Assert.True(gap.TryGetProperty("gapId", out _));
            Assert.True(gap.TryGetProperty("dimension", out _));
            Assert.True(gap.TryGetProperty("status", out _));
            Assert.True(gap.TryGetProperty("evidenceIds", out var evidenceIds)
                && evidenceIds.ValueKind == JsonValueKind.Array);
        }
    }

    [Fact]
    public void Multi_competitor_positioning_golden_includes_cohort_warning()
    {
        using var document = LoadNamedGolden("competitorPositioning.multi.v1.golden.json");
        var root = document.RootElement;
        Assert.Equal("competitorPositioning.v1", root.GetProperty("artifactType").GetString());
        Assert.True(root.TryGetProperty("warnings", out var warnings));
        Assert.Contains(
            warnings.EnumerateArray().Select(item => item.GetString()),
            warning => warning != null
                && warning.Contains("Positioning map considers 2 competitor pages."));
        Assert.True(root.TryGetProperty("attributeMap", out var attributes)
            && attributes.EnumerateArray().Any(attr =>
                attr.TryGetProperty("competitorId", out var competitorId)
                && competitorId.GetString() == "alt-co"));
    }

    [Fact]
    public void Partial_content_gap_golden_never_asserts_absence()
    {
        using var document = LoadNamedGolden("contentGapAnalysis.partial.v1.golden.json");
        var root = document.RootElement;
        Assert.Equal("contentGapAnalysis.v1", root.GetProperty("artifactType").GetString());
        Assert.True(root.TryGetProperty("gaps", out var gaps) && gaps.GetArrayLength() > 0);
        foreach (var gap in gaps.EnumerateArray())
        {
            Assert.Equal("coverageUnknown", gap.GetProperty("status").GetString());
        }
    }

    [Fact]
    public void Partial_page_analysis_golden_keeps_presence_unknown()
    {
        using var document = LoadNamedGolden("competitorPageAnalysis.partial.v1.golden.json");
        var root = document.RootElement;
        Assert.Equal("competitorPageAnalysis.v1", root.GetProperty("artifactType").GetString());
        Assert.True(root.TryGetProperty("dimensions", out var dimensions)
            && dimensions.GetArrayLength() > 0);
        foreach (var dimension in dimensions.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Null, dimension.GetProperty("present").ValueKind);
        }
        Assert.True(root.TryGetProperty("opportunities", out var opportunities)
            && opportunities.GetArrayLength() == 0);
    }

    private static JsonDocument LoadGolden(string artifactType)
    {
        return LoadNamedGolden($"{artifactType}.golden.json");
    }

    private static JsonDocument LoadNamedGolden(string fileName)
    {
        var path = Path.Combine(FixtureDir, fileName);
        Assert.True(File.Exists(path), $"Missing golden fixture: {path}");
        return JsonDocument.Parse(File.ReadAllText(path), DocOpts);
    }
}
