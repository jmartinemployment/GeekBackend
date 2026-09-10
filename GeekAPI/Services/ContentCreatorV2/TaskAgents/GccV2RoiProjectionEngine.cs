using System.Globalization;
using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

/// <summary>
/// Deterministic ROI projection math for the AI-Based ROI Business Calculation Agent.
/// Formulas match the Content Creator FE model; AI never invents hidden coefficients.
/// </summary>
public static class GccV2RoiProjectionEngine
{
    public const string Engine = "roi-projection";
    public const string ArtifactType = "roiProjection.v1";
    public const string FormulaVersion = "gcc-roi-formulas.v1";
    public const string ContractVersion = "roiBusinessCalculatorInput.v1";

    private static readonly ScenarioFactor[] Scenarios =
    [
        new("conservative", "Conservative", 0.75, 0.85, 0.9),
        new("expected", "Expected", 1, 1, 1),
        new("upside", "Upside", 1.25, 1.1, 1.05),
    ];

    public static JsonElement Execute(JsonElement input, JsonElement? observedTelemetry = null)
    {
        var assumptions = ReadAssumptions(input);
        var lookbackDays = ReadLookbackDays(input);
        var projections = Scenarios.Select(scenario => Project(assumptions, scenario)).ToList();
        var expected = Scenarios.Single(x => x.Id == "expected");
        var reconciliation = Reconcile(assumptions, expected, observedTelemetry, lookbackDays);

        var payload = new
        {
            artifactType = ArtifactType,
            formulaVersion = FormulaVersion,
            contractVersion = ContractVersion,
            assumptions = assumptions.ToDictionary(),
            lookbackDays,
            scenarios = projections,
            reconciliation,
            evidenceLabels = new
            {
                projections = "modeled",
                reconciliation = observedTelemetry is null ? "experimental" : "telemetry-measured",
                cashClaims = "not-asserted",
            },
            warnings = new[]
            {
                "Directional model only — not a quote, guarantee, or audited finance result.",
                "Capacity created is not cash saved. Revenue contribution requires attributable gross margin.",
            },
            provenance = new
            {
                method = FormulaVersion,
                evidence = Array.Empty<object>(),
                citations = Array.Empty<object>(),
            },
        };
        return JsonSerializer.SerializeToElement(payload);
    }

    private static Assumptions ReadAssumptions(JsonElement input)
    {
        var defaults = Assumptions.Defaults;
        if (!input.TryGetProperty("assumptions", out var raw) || raw.ValueKind != JsonValueKind.Object)
            return defaults;
        return new Assumptions(
            ReadNumber(raw, "workflowVolume", defaults.WorkflowVolume),
            ReadNumber(raw, "baselineMinutes", defaults.BaselineMinutes),
            ReadNumber(raw, "assistedMinutes", defaults.AssistedMinutes),
            ClampRate(ReadNumber(raw, "adoptionRate", defaults.AdoptionRate)),
            ClampRate(ReadNumber(raw, "successfulUseRate", defaults.SuccessfulUseRate)),
            ReadNumber(raw, "loadedHourlyCost", defaults.LoadedHourlyCost),
            ReadNumber(raw, "redeploymentFactor", defaults.RedeploymentFactor),
            ReadNumber(raw, "externalSpend", defaults.ExternalSpend),
            ClampRate(ReadNumber(raw, "replaceableShare", defaults.ReplaceableShare)),
            ReadNumber(raw, "totalCostOfOwnership", defaults.TotalCostOfOwnership),
            ClampRate(ReadNumber(raw, "attributableGrossMargin", defaults.AttributableGrossMargin)));
    }

    private static int ReadLookbackDays(JsonElement input)
    {
        if (!input.TryGetProperty("lookbackDays", out var raw)) return 90;
        var value = raw.ValueKind == JsonValueKind.Number ? raw.GetInt32()
            : int.TryParse(raw.GetString(), out var parsed) ? parsed : 90;
        return value is 30 or 90 or 180 or 365 ? value : 90;
    }

    private static double ReadNumber(JsonElement obj, string name, double fallback)
    {
        if (!obj.TryGetProperty(name, out var raw)) return fallback;
        return raw.ValueKind switch
        {
            JsonValueKind.Number => raw.GetDouble(),
            JsonValueKind.String when double.TryParse(
                raw.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                => parsed,
            _ => fallback,
        };
    }

    private static double ClampRate(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static object Project(Assumptions assumptions, ScenarioFactor factors)
    {
        var volume = assumptions.WorkflowVolume * factors.VolumeFactor;
        var minutesSaved = Math.Max(0, assumptions.BaselineMinutes - assumptions.AssistedMinutes);
        var adoption = ClampRate(assumptions.AdoptionRate * factors.AdoptionFactor);
        var success = ClampRate(assumptions.SuccessfulUseRate * factors.SuccessFactor);
        var hours = volume * minutesSaved / 60d * adoption * success;
        var productivity = hours * assumptions.LoadedHourlyCost * assumptions.RedeploymentFactor;
        var external = assumptions.ExternalSpend * assumptions.ReplaceableShare * adoption;
        var gross = productivity + external;
        var net = gross - assumptions.TotalCostOfOwnership;
        double? roiPercent = assumptions.TotalCostOfOwnership > 0
            ? net / assumptions.TotalCostOfOwnership * 100d
            : null;
        double? paybackMonths = gross > 0
            ? assumptions.TotalCostOfOwnership / gross * 12d
            : null;
        double? timeToValueMonths = productivity > 0
            ? assumptions.TotalCostOfOwnership / productivity * 12d
            : null;
        return new
        {
            scenario = factors.Id,
            label = factors.Label,
            savedHours = hours,
            productivityValue = productivity,
            externalCostAvoided = external,
            grossBenefit = gross,
            netBenefit = net,
            roiPercent,
            paybackMonths,
            timeToValueMonths,
        };
    }

    private static object Reconcile(
        Assumptions assumptions,
        ScenarioFactor factors,
        JsonElement? observedTelemetry,
        int lookbackDays)
    {
        var assumedSuccess = ClampRate(assumptions.SuccessfulUseRate * factors.SuccessFactor);
        double? observedAcceptance = null;
        double? observedAvgReview = null;
        double? observedAnnualizedAccepted = null;
        double? observedAnnualizedPublished = null;
        var notes = new List<string>
        {
            "Reconciliation compares modeled assumptions to observed workflow capacity — not cash claims.",
        };

        if (observedTelemetry is { ValueKind: JsonValueKind.Object } observed)
        {
            var generated = ReadNumber(observed, "generatedCount", 0);
            var accepted = ReadNumber(observed, "acceptedCount", 0);
            var published = ReadNumber(observed, "publishedCount", 0);
            var rejected = ReadNumber(observed, "rejectedCount", 0);
            var cancelled = ReadNumber(observed, "cancelledCount", 0);
            var reviewMinutes = ReadNumber(observed, "reviewMinutes", 0);
            if (generated > 0) observedAcceptance = accepted / generated;
            var terminal = accepted + rejected + cancelled;
            if (terminal > 0) observedAvgReview = reviewMinutes / terminal;
            var source = observed.TryGetProperty("source", out var sourceEl)
                ? sourceEl.GetString()
                : null;
            if (!string.Equals(source, "empty", StringComparison.OrdinalIgnoreCase))
            {
                var annualize = 365d / lookbackDays;
                observedAnnualizedAccepted = accepted * annualize;
                observedAnnualizedPublished = published * annualize;
            }
            if (observedAcceptance is { } acceptance)
            {
                var deltaPp = (acceptance - assumedSuccess) * 100d;
                notes.Add(deltaPp >= 0
                    ? $"Observed acceptance is {deltaPp:0.0} pp above the assumed success rate."
                    : $"Observed acceptance is {Math.Abs(deltaPp):0.0} pp below the assumed success rate.");
            }
        }
        else
        {
            notes.Add("No observed telemetry was attached; reconciliation is incomplete.");
        }

        var volume = assumptions.WorkflowVolume * factors.VolumeFactor;
        var adoption = ClampRate(assumptions.AdoptionRate * factors.AdoptionFactor);
        var projectedAnnualAccepted = volume * adoption * assumedSuccess;
        var publishRate = observedAcceptance ?? assumedSuccess;
        return new
        {
            assumedSuccessRate = assumedSuccess,
            observedAcceptanceRate = observedAcceptance,
            successRateDeltaPp = observedAcceptance is { } value
                ? (value - assumedSuccess) * 100d
                : (double?)null,
            assumedAssistedMinutes = assumptions.AssistedMinutes,
            observedAvgReviewMinutes = observedAvgReview,
            assistedMinutesDelta = observedAvgReview is { } avg
                ? avg - assumptions.AssistedMinutes
                : (double?)null,
            projectedAnnualAccepted,
            observedAnnualizedAccepted,
            projectedAnnualPublished = projectedAnnualAccepted * publishRate,
            observedAnnualizedPublished,
            notes,
        };
    }

    private sealed record ScenarioFactor(
        string Id, string Label, double VolumeFactor, double AdoptionFactor, double SuccessFactor);

    private sealed record Assumptions(
        double WorkflowVolume,
        double BaselineMinutes,
        double AssistedMinutes,
        double AdoptionRate,
        double SuccessfulUseRate,
        double LoadedHourlyCost,
        double RedeploymentFactor,
        double ExternalSpend,
        double ReplaceableShare,
        double TotalCostOfOwnership,
        double AttributableGrossMargin)
    {
        public static Assumptions Defaults { get; } = new(
            120, 90, 25, 0.7, 0.85, 85, 0.6, 48000, 0.35, 36000, 0.55);

        public Dictionary<string, double> ToDictionary() => new()
        {
            ["workflowVolume"] = WorkflowVolume,
            ["baselineMinutes"] = BaselineMinutes,
            ["assistedMinutes"] = AssistedMinutes,
            ["adoptionRate"] = AdoptionRate,
            ["successfulUseRate"] = SuccessfulUseRate,
            ["loadedHourlyCost"] = LoadedHourlyCost,
            ["redeploymentFactor"] = RedeploymentFactor,
            ["externalSpend"] = ExternalSpend,
            ["replaceableShare"] = ReplaceableShare,
            ["totalCostOfOwnership"] = TotalCostOfOwnership,
            ["attributableGrossMargin"] = AttributableGrossMargin,
        };
    }
}
