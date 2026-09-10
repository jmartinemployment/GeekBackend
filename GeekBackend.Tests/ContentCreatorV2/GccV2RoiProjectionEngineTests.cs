using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2RoiProjectionEngineTests
{
    [Fact]
    public void Expected_scenario_matches_published_transparent_formulas()
    {
        using var input = JsonDocument.Parse("""
            {
              "contractVersion": "roiBusinessCalculatorInput.v1",
              "lookbackDays": 90,
              "assumptions": {
                "workflowVolume": 120,
                "baselineMinutes": 90,
                "assistedMinutes": 25,
                "adoptionRate": 0.7,
                "successfulUseRate": 0.85,
                "loadedHourlyCost": 85,
                "redeploymentFactor": 0.6,
                "externalSpend": 48000,
                "replaceableShare": 0.35,
                "totalCostOfOwnership": 36000,
                "attributableGrossMargin": 0.55
              }
            }
            """);
        var output = GccV2RoiProjectionEngine.Execute(input.RootElement);
        Assert.Equal(GccV2RoiProjectionEngine.ArtifactType,
            output.GetProperty("artifactType").GetString());
        Assert.Equal(GccV2RoiProjectionEngine.FormulaVersion,
            output.GetProperty("formulaVersion").GetString());

        var expected = output.GetProperty("scenarios").EnumerateArray()
            .Single(x => x.GetProperty("scenario").GetString() == "expected");
        var minutesSaved = 90d - 25d;
        var savedHours = 120d * minutesSaved / 60d * 0.7 * 0.85;
        var productivity = savedHours * 85d * 0.6;
        var external = 48000d * 0.35 * 0.7;
        var gross = productivity + external;
        var net = gross - 36000d;
        Assert.Equal(savedHours, expected.GetProperty("savedHours").GetDouble(), 5);
        Assert.Equal(productivity, expected.GetProperty("productivityValue").GetDouble(), 5);
        Assert.Equal(external, expected.GetProperty("externalCostAvoided").GetDouble(), 5);
        Assert.Equal(net, expected.GetProperty("netBenefit").GetDouble(), 5);
        Assert.Equal(net / 36000d * 100d, expected.GetProperty("roiPercent").GetDouble(), 5);
    }

    [Fact]
    public void Reconciliation_compares_capacity_without_inventing_cash()
    {
        using var input = JsonDocument.Parse("""{"lookbackDays":90,"assumptions":{}}""");
        using var observed = JsonDocument.Parse("""
            {
              "generatedCount": 48,
              "acceptedCount": 31,
              "publishedCount": 22,
              "rejectedCount": 9,
              "cancelledCount": 0,
              "reviewMinutes": 410,
              "source": "telemetry"
            }
            """);
        var output = GccV2RoiProjectionEngine.Execute(input.RootElement, observed.RootElement);
        var reconciliation = output.GetProperty("reconciliation");
        Assert.Equal(31d / 48d, reconciliation.GetProperty("observedAcceptanceRate").GetDouble(), 5);
        Assert.Contains("not cash",
            reconciliation.GetProperty("notes")[0].GetString()!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("modeled",
            output.GetProperty("evidenceLabels").GetProperty("projections").GetString());
    }
}
