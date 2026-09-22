using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Cross-language camelCase goldens shared with Geek-Crawler-Rag diagnostic artifacts.
/// Python regenerates and round-trips these; C# asserts durable shape + consumer parsers.
/// </summary>
public sealed class GccV2DiagnosticArtifactContractTests
{
    private static readonly string FixtureDir = Path.Combine(
        AppContext.BaseDirectory, "ContentCreatorV2", "Fixtures");

    private static readonly JsonDocumentOptions DocOpts = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
    };

    [Theory]
    [InlineData("readinessScore.v1", "overallScore", "dimensions")]
    [InlineData("factDensityReport.v1", "overallScore", "claims")]
    [InlineData("entityMap.v1", "entities", "relationships")]
    [InlineData("schemaMarkup.v1", "jsonLd", "validation")]
    public void Golden_fixture_exposes_required_camelCase_shape(
        string artifactType, string requiredA, string requiredB)
    {
        using var document = LoadGolden(artifactType);
        var root = document.RootElement;
        Assert.Equal(artifactType, root.GetProperty("artifactType").GetString());
        Assert.True(root.TryGetProperty(requiredA, out _), $"missing {requiredA}");
        Assert.True(root.TryGetProperty(requiredB, out _), $"missing {requiredB}");
        Assert.True(root.TryGetProperty("provenance", out _), "missing provenance");
        Assert.True(root.TryGetProperty("warnings", out var warnings)
            && warnings.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public void Readiness_golden_is_readable_by_change_over_time_score_snapshot()
    {
        var json = File.ReadAllText(Path.Combine(FixtureDir, "readinessScore.v1.golden.json"));
        Assert.True(GccV2ArtifactChangeOverTime.TryReadScoreSnapshot(
            json, out var overall, out var dimensions));
        Assert.Equal(76.4, overall);
        Assert.Contains("headingHierarchy", dimensions.Keys);
        Assert.Contains("factDensity", dimensions.Keys);
        Assert.Equal(7, dimensions.Count);
    }

    [Fact]
    public void Schema_markup_golden_feeds_quality_metrics_validation_counts()
    {
        var json = File.ReadAllText(Path.Combine(FixtureDir, "schemaMarkup.v1.golden.json"));
        var sample = GccV2ArtifactQualityMetrics.FromPayload(json, validationState: null);
        Assert.True(sample.SchemaValidTotal > 0);
        Assert.Equal(sample.SchemaValidTotal, sample.SchemaValidHits);
    }

    [Fact]
    public void Schema_markup_golden_marks_all_visible_content_findings_valid()
    {
        using var document = LoadGolden("schemaMarkup.v1");
        var validation = document.RootElement.GetProperty("validation");
        Assert.True(validation.GetArrayLength() > 0);
        foreach (var finding in validation.EnumerateArray())
        {
            Assert.True(finding.TryGetProperty("code", out var code));
            Assert.True(finding.TryGetProperty("valid", out var valid));
            Assert.True(finding.TryGetProperty("schemaType", out _));
            Assert.True(finding.TryGetProperty("detail", out _));
            if (code.GetString() is "jsonSyntax" or "schemaShape" or "visibleContent")
                Assert.True(valid.GetBoolean());
        }
    }

    private static JsonDocument LoadGolden(string artifactType)
    {
        var path = Path.Combine(FixtureDir, $"{artifactType}.golden.json");
        Assert.True(File.Exists(path), $"Missing golden fixture: {path}");
        return JsonDocument.Parse(File.ReadAllText(path), DocOpts);
    }
}
