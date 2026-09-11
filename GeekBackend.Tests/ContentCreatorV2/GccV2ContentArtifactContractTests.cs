using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Cross-language camelCase goldens shared with Geek-Crawler-Rag content artifacts.
/// </summary>
public sealed class GccV2ContentArtifactContractTests
{
    private static readonly string FixtureDir = Path.Combine(
        AppContext.BaseDirectory, "ContentCreatorV2", "Fixtures");

    private static readonly JsonDocumentOptions DocOpts = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
    };

    [Fact]
    public void Claim_ledger_golden_exposes_required_camelCase_shape()
    {
        using var document = LoadGolden("claimLedger.v1");
        var root = document.RootElement;
        Assert.Equal("claimLedger.v1", root.GetProperty("artifactType").GetString());
        Assert.True(root.TryGetProperty("claims", out var claims) && claims.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("contradictionPairs", out _));
        Assert.True(root.TryGetProperty("provenance", out var provenance));
        Assert.True(root.TryGetProperty("warnings", out var warnings)
            && warnings.ValueKind == JsonValueKind.Array);

        var supported = 0;
        foreach (var claim in claims.EnumerateArray())
        {
            Assert.True(claim.TryGetProperty("claimId", out _));
            Assert.True(claim.TryGetProperty("verificationStatus", out var status));
            Assert.True(claim.TryGetProperty("evidenceIds", out var evidenceIds)
                && evidenceIds.ValueKind == JsonValueKind.Array);
            if (string.Equals(status.GetString(), "supported", StringComparison.OrdinalIgnoreCase))
            {
                supported += 1;
                Assert.True(evidenceIds.GetArrayLength() > 0);
            }
        }

        Assert.True(supported > 0);
        Assert.True(provenance.TryGetProperty("evidence", out var evidence)
            && evidence.GetArrayLength() > 0);
        foreach (var row in evidence.EnumerateArray())
        {
            Assert.True(row.TryGetProperty("quote", out var quote)
                && quote.ValueKind == JsonValueKind.String
                && quote.GetString()!.Length > 0);
            Assert.True(row.TryGetProperty("startChar", out var start)
                && start.ValueKind == JsonValueKind.Number);
            Assert.True(row.TryGetProperty("endChar", out var end)
                && end.ValueKind == JsonValueKind.Number);
            Assert.True(end.GetInt32() >= start.GetInt32());
        }
    }

    [Fact]
    public void Claim_ledger_golden_feeds_groundedness_quality_metrics()
    {
        var json = File.ReadAllText(Path.Combine(FixtureDir, "claimLedger.v1.golden.json"));
        using var document = JsonDocument.Parse(json, DocOpts);
        var supported = document.RootElement.GetProperty("claims").EnumerateArray()
            .Count(claim => string.Equals(
                claim.GetProperty("verificationStatus").GetString(),
                "supported",
                StringComparison.OrdinalIgnoreCase));
        var totalClaims = document.RootElement.GetProperty("claims").GetArrayLength();

        var sample = GccV2ArtifactQualityMetrics.FromPayload(json, validationState: null);
        Assert.Equal(totalClaims, sample.GroundedTotal);
        Assert.Equal(supported, sample.GroundedHits);
        Assert.True(sample.GroundedHits > 0);
    }

    [Fact]
    public void Faq_set_golden_exposes_required_camelCase_shape()
    {
        using var document = LoadGolden("faqSet.v1");
        var root = document.RootElement;
        Assert.Equal("faqSet.v1", root.GetProperty("artifactType").GetString());
        Assert.True(root.TryGetProperty("pairs", out var pairs) && pairs.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("provenance", out var provenance));
        Assert.True(root.TryGetProperty("warnings", out var warnings)
            && warnings.ValueKind == JsonValueKind.Array);

        var supported = 0;
        foreach (var pair in pairs.EnumerateArray())
        {
            Assert.True(pair.TryGetProperty("question", out _));
            Assert.True(pair.TryGetProperty("answer", out _));
            Assert.True(pair.TryGetProperty("verificationStatus", out var status));
            Assert.True(pair.TryGetProperty("citations", out var citations)
                && citations.ValueKind == JsonValueKind.Array);
            if (string.Equals(status.GetString(), "supported", StringComparison.OrdinalIgnoreCase))
            {
                supported += 1;
                Assert.True(citations.GetArrayLength() > 0);
            }
        }

        Assert.True(supported > 0);
        Assert.True(provenance.TryGetProperty("evidence", out var evidence)
            && evidence.GetArrayLength() > 0);
        foreach (var row in evidence.EnumerateArray())
        {
            Assert.True(row.TryGetProperty("quote", out var quote)
                && quote.ValueKind == JsonValueKind.String
                && quote.GetString()!.Length > 0);
            Assert.True(row.TryGetProperty("startChar", out var start)
                && start.ValueKind == JsonValueKind.Number);
            Assert.True(row.TryGetProperty("endChar", out var end)
                && end.ValueKind == JsonValueKind.Number);
            Assert.True(end.GetInt32() >= start.GetInt32());
        }
    }

    [Fact]
    public void Faq_set_golden_feeds_groundedness_quality_metrics()
    {
        var json = File.ReadAllText(Path.Combine(FixtureDir, "faqSet.v1.golden.json"));
        using var document = JsonDocument.Parse(json, DocOpts);
        var supported = document.RootElement.GetProperty("pairs").EnumerateArray()
            .Count(pair => string.Equals(
                pair.GetProperty("verificationStatus").GetString(),
                "supported",
                StringComparison.OrdinalIgnoreCase));
        var totalPairs = document.RootElement.GetProperty("pairs").GetArrayLength();

        var sample = GccV2ArtifactQualityMetrics.FromPayload(json, validationState: null);
        Assert.Equal(totalPairs, sample.GroundedTotal);
        Assert.Equal(supported, sample.GroundedHits);
        Assert.True(sample.GroundedHits > 0);
    }

    [Fact]
    public void Comparison_brief_golden_exposes_required_camelCase_shape()
    {
        using var document = LoadGolden("comparisonBrief.v1");
        var root = document.RootElement;
        Assert.Equal("comparisonBrief.v1", root.GetProperty("artifactType").GetString());
        Assert.Equal("Subject Analyzer", root.GetProperty("subjectName").GetString());
        Assert.Equal("Competitor Inc.", root.GetProperty("competitorName").GetString());
        Assert.True(root.TryGetProperty("criteria", out var criteria) && criteria.GetArrayLength() == 4);
        Assert.True(root.TryGetProperty("recommendedVerdict", out var verdict)
            && verdict.TryGetProperty("disclaimer", out var disclaimer)
            && disclaimer.ValueKind == JsonValueKind.String
            && disclaimer.GetString()!.Length > 0);
        Assert.True(root.TryGetProperty("provenance", out var provenance)
            && provenance.TryGetProperty("sources", out var sources)
            && sources.ValueKind == JsonValueKind.Array
            && sources.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("warnings", out var warnings)
            && warnings.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public void Competitive_response_golden_exposes_required_camelCase_shape()
    {
        using var document = LoadGolden("competitiveResponse.v1");
        var root = document.RootElement;
        Assert.Equal("competitiveResponse.v1", root.GetProperty("artifactType").GetString());
        Assert.True(root.TryGetProperty("selectedMode", out var mode)
            && mode.ValueKind == JsonValueKind.String
            && mode.GetString()!.Length > 0);
        Assert.True(root.TryGetProperty("outlineSections", out var sections)
            && sections.ValueKind == JsonValueKind.Array
            && sections.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("provenance", out var provenance)
            && provenance.TryGetProperty("sources", out var sources)
            && sources.ValueKind == JsonValueKind.Array
            && sources.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("warnings", out _));
    }

    [Fact]
    public void Pillar_outline_golden_exposes_required_camelCase_shape()
    {
        using var document = LoadGolden("pillarOutline.v1");
        var root = document.RootElement;
        Assert.Equal("pillarOutline.v1", root.GetProperty("artifactType").GetString());
        Assert.Equal("AI content readiness", root.GetProperty("topic").GetString());
        Assert.True(root.TryGetProperty("sections", out var sections)
            && sections.ValueKind == JsonValueKind.Array
            && sections.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("supportingContentPlan", out var plan)
            && plan.ValueKind == JsonValueKind.Array
            && plan.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("warnings", out _));
    }

    [Fact]
    public void Pillar_article_golden_exposes_required_camelCase_shape_and_groundedness()
    {
        using var document = LoadGolden("pillarArticle.v1");
        var root = document.RootElement;
        Assert.Equal("pillarArticle.v1", root.GetProperty("artifactType").GetString());
        Assert.True(root.TryGetProperty("markdown", out var markdown)
            && markdown.GetString()!.StartsWith("# ", StringComparison.Ordinal));
        Assert.True(root.TryGetProperty("sections", out var sections)
            && sections.ValueKind == JsonValueKind.Array
            && sections.GetArrayLength() > 0);
        Assert.Contains(sections.EnumerateArray(), section =>
            section.TryGetProperty("grounded", out var grounded) && grounded.GetBoolean());
        Assert.True(root.TryGetProperty("supportingContentPlan", out var plan)
            && plan.ValueKind == JsonValueKind.Array
            && plan.GetArrayLength() > 0);

        var json = File.ReadAllText(Path.Combine(FixtureDir, "pillarArticle.v1.golden.json"));
        var sample = GccV2ArtifactQualityMetrics.FromPayload(json, validationState: null);
        Assert.True(sample.GroundedTotal > 0);
        Assert.True(sample.GroundedHits > 0);
    }

    private static JsonDocument LoadGolden(string artifactType)
    {
        var path = Path.Combine(FixtureDir, $"{artifactType}.golden.json");
        Assert.True(File.Exists(path), $"Missing golden fixture: {path}");
        return JsonDocument.Parse(File.ReadAllText(path), DocOpts);
    }
}
