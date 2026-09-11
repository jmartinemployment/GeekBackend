using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Cross-language roiProjection.v1 golden shared with Content Creator FE formula tests.
/// </summary>
public sealed class GccV2RoiProjectionArtifactContractTests
{
    private static readonly string FixtureDir = Path.Combine(
        AppContext.BaseDirectory, "ContentCreatorV2", "Fixtures");

    private static readonly string FixedInputJson = """
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
        """;

    private static readonly string FixedObservedJson = """
        {
          "generatedCount": 48,
          "acceptedCount": 31,
          "publishedCount": 22,
          "rejectedCount": 9,
          "cancelledCount": 0,
          "reviewMinutes": 410,
          "source": "telemetry"
        }
        """;

    [Fact]
    public void Golden_fixture_exposes_required_camelCase_shape_and_phase7_labels()
    {
        using var document = LoadGolden();
        var root = document.RootElement;
        Assert.Equal("roiProjection.v1", root.GetProperty("artifactType").GetString());
        Assert.Equal("gcc-roi-formulas.v1", root.GetProperty("formulaVersion").GetString());
        Assert.Equal("roiBusinessCalculatorInput.v1", root.GetProperty("contractVersion").GetString());
        Assert.Equal(90, root.GetProperty("lookbackDays").GetInt32());
        Assert.True(root.TryGetProperty("assumptions", out _));
        Assert.True(root.TryGetProperty("scenarios", out var scenarios)
            && scenarios.GetArrayLength() == 3);
        Assert.True(root.TryGetProperty("reconciliation", out var reconciliation));
        Assert.True(root.TryGetProperty("evidenceLabels", out var labels));
        Assert.Equal("modeled", labels.GetProperty("projections").GetString());
        Assert.Equal("telemetry-measured", labels.GetProperty("reconciliation").GetString());
        Assert.Equal("not-asserted", labels.GetProperty("cashClaims").GetString());
        Assert.True(root.TryGetProperty("warnings", out var warnings)
            && warnings.GetArrayLength() > 0);
        Assert.Contains(warnings.EnumerateArray(), w =>
            (w.GetString() ?? "").Contains("Directional model", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(reconciliation.GetProperty("notes").EnumerateArray(), note =>
            (note.GetString() ?? "").Contains("not cash", StringComparison.OrdinalIgnoreCase));
        Assert.True(root.TryGetProperty("provenance", out _));
    }

    [Fact]
    public void Golden_matches_live_engine_output_byte_stable()
    {
        using var input = JsonDocument.Parse(FixedInputJson);
        using var observed = JsonDocument.Parse(FixedObservedJson);
        var live = GccV2RoiProjectionEngine.Execute(input.RootElement, observed.RootElement);
        var liveCanonical = Canonicalize(live.GetRawText());
        var goldenCanonical = Canonicalize(File.ReadAllText(GoldenPath()));
        Assert.Equal(goldenCanonical, liveCanonical);
    }

    [Fact]
    public void Expected_scenario_in_golden_matches_transparent_formulas()
    {
        using var document = LoadGolden();
        var expected = document.RootElement.GetProperty("scenarios").EnumerateArray()
            .Single(x => x.GetProperty("scenario").GetString() == "expected");

        const double volume = 120;
        const double minutesSaved = 90 - 25;
        const double adoption = 0.7;
        const double success = 0.85;
        var savedHours = volume * minutesSaved / 60d * adoption * success;
        var productivity = savedHours * 85d * 0.6;
        var external = 48000d * 0.35 * adoption;
        var gross = productivity + external;
        var net = gross - 36000d;

        Assert.Equal(savedHours, expected.GetProperty("savedHours").GetDouble(), 5);
        Assert.Equal(productivity, expected.GetProperty("productivityValue").GetDouble(), 5);
        Assert.Equal(external, expected.GetProperty("externalCostAvoided").GetDouble(), 5);
        Assert.Equal(gross, expected.GetProperty("grossBenefit").GetDouble(), 5);
        Assert.Equal(net, expected.GetProperty("netBenefit").GetDouble(), 5);
        Assert.Equal(net / 36000d * 100d, expected.GetProperty("roiPercent").GetDouble(), 5);
    }

    /// <summary>
    /// Set REGEN_ROI_GOLDEN=1 to rewrite the fixture from the live engine (dev only).
    /// </summary>
    [Fact]
    public void Regen_golden_when_requested()
    {
        if (Environment.GetEnvironmentVariable("REGEN_ROI_GOLDEN") != "1")
            return;

        using var input = JsonDocument.Parse(FixedInputJson);
        using var observed = JsonDocument.Parse(FixedObservedJson);
        var live = GccV2RoiProjectionEngine.Execute(input.RootElement, observed.RootElement);
        var pretty = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonElement>(live.GetRawText()),
            new JsonSerializerOptions { WriteIndented = true });
        var sourceFixture = FindSourceFixturePath();
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFixture)!);
        File.WriteAllText(sourceFixture, pretty + Environment.NewLine);
        File.WriteAllText(GoldenPath(), pretty + Environment.NewLine);
    }

    private static string GoldenPath() =>
        Path.Combine(FixtureDir, "roiProjection.v1.golden.json");

    private static JsonDocument LoadGolden()
    {
        var path = GoldenPath();
        Assert.True(File.Exists(path), $"Missing golden fixture: {path}. Run with REGEN_ROI_GOLDEN=1.");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string Canonicalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string FindSourceFixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "GeekBackend.Tests",
                "ContentCreatorV2",
                "Fixtures",
                "roiProjection.v1.golden.json");
            if (Directory.Exists(Path.GetDirectoryName(candidate)))
                return candidate;
            dir = dir.Parent;
        }

        return Path.Combine(
            AppContext.BaseDirectory,
            "ContentCreatorV2",
            "Fixtures",
            "roiProjection.v1.golden.json");
    }
}
