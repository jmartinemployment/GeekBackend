using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2ArtifactQualityMetricsTests
{
    [Fact]
    public void FromPayload_counts_grounded_sections_and_supported_claims()
    {
        var sample = GccV2ArtifactQualityMetrics.FromPayload(
            """
            {
              "sections": [
                { "heading": "A", "grounded": true },
                { "heading": "B", "grounded": false }
              ],
              "claims": [
                { "claimId": "1", "verificationStatus": "supported" },
                { "claimId": "2", "verificationStatus": "unsupported" },
                { "claimId": "3", "verificationStatus": "unverifiable" }
              ]
            }
            """,
            "valid");

        Assert.Equal(2, sample.GroundedHits); // 1 section + 1 claim
        Assert.Equal(5, sample.GroundedTotal); // 2 sections + 3 claims
        Assert.Equal(1, sample.SchemaValidHits);
        Assert.Equal(1, sample.SchemaValidTotal);
    }

    [Fact]
    public void FromPayload_counts_schema_validation_findings()
    {
        var sample = GccV2ArtifactQualityMetrics.FromPayload(
            """
            {
              "validation": [
                { "code": "ok", "valid": true },
                { "code": "missing", "valid": false }
              ]
            }
            """);

        Assert.Equal(1, sample.SchemaValidHits);
        Assert.Equal(2, sample.SchemaValidTotal);
        Assert.Equal(0, sample.GroundedTotal);
    }

    [Fact]
    public void Combine_computes_rates()
    {
        var aggregate = GccV2ArtifactQualityMetrics.Combine([
            new GccV2ArtifactQualityMetrics.Sample(3, 4, 2, 2),
            new GccV2ArtifactQualityMetrics.Sample(1, 1, 0, 1),
        ]);

        Assert.Equal(2, aggregate.RunsSampled);
        Assert.Equal(0.8, aggregate.GroundednessRate);
        Assert.Equal(2.0 / 3.0, aggregate.SchemaValidityRate!.Value, 4);
    }
}
