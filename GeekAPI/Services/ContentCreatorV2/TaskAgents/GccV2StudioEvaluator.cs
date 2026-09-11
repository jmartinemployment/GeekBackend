using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

/// <summary>Deterministic Studio evaluation for dry-run / test suites / publish gates (no LLM).</summary>
internal static class GccV2StudioEvaluator
{
    public sealed record CaseResult(
        string Id,
        string Name,
        bool Valid,
        IReadOnlyList<string> ValidationErrors,
        IReadOnlyList<string> MissingTokens,
        string? RenderedInstructions,
        string Message);

    public sealed record SuiteResult(
        bool Valid,
        int MinTestCases,
        int CaseCount,
        int PassedCount,
        IReadOnlyList<CaseResult> Cases,
        string Message);

    public static CaseResult EvaluateCase(
        string caseId,
        string caseName,
        JsonElement input,
        string inputSchemaJson,
        string instructionsTemplate,
        string agentName,
        string outcome,
        string? exampleOutput,
        string? evaluationPrompt)
    {
        var validationErrors = GccV2TaskInputSchemaValidator.Validate(input, inputSchemaJson);
        var render = GccV2StudioTemplateRenderer.Render(
            instructionsTemplate, agentName, outcome, input);
        var missingExample = string.IsNullOrWhiteSpace(exampleOutput);
        var missingEvaluation = string.IsNullOrWhiteSpace(evaluationPrompt);
        var valid = validationErrors.Count == 0
            && render.MissingTokens.Count == 0
            && !missingExample
            && !missingEvaluation;
        var message = valid
            ? $"Case '{caseName}' passed."
            : validationErrors.Count > 0
                ? $"Case '{caseName}' failed schema: {string.Join(" ", validationErrors)}"
                : render.MissingTokens.Count > 0
                    ? $"Case '{caseName}' has unresolved tokens: {string.Join(", ", render.MissingTokens)}."
                    : missingExample
                        ? $"Case '{caseName}' blocked: example output is required."
                        : $"Case '{caseName}' blocked: evaluation prompt is required.";
        return new CaseResult(
            caseId,
            caseName,
            valid,
            validationErrors,
            render.MissingTokens,
            render.RenderedInstructions,
            message);
    }

    public static SuiteResult EvaluateSuite(
        IReadOnlyList<(string Id, string Name, JsonElement Input)> cases,
        int minTestCases,
        string inputSchemaJson,
        string instructionsTemplate,
        string agentName,
        string outcome,
        string? exampleOutput,
        string? evaluationPrompt)
    {
        var results = cases
            .Select(item => EvaluateCase(
                item.Id,
                item.Name,
                item.Input,
                inputSchemaJson,
                instructionsTemplate,
                agentName,
                outcome,
                exampleOutput,
                evaluationPrompt))
            .ToList();
        var passed = results.Count(x => x.Valid);
        var enough = results.Count >= minTestCases;
        var valid = enough && passed == results.Count && results.Count > 0;
        var message = valid
            ? $"Test suite passed ({passed}/{results.Count} cases; min {minTestCases})."
            : !enough
                ? $"Test suite needs at least {minTestCases} case(s); found {results.Count}."
                : results.Count == 0
                    ? $"Add at least {minTestCases} test case(s) before publish."
                    : $"Test suite failed ({passed}/{results.Count} passed).";
        return new SuiteResult(valid, minTestCases, results.Count, passed, results, message);
    }

    public static int ReadMinTestCases(string? evaluationThresholdsJson, int fallback = 1)
    {
        if (string.IsNullOrWhiteSpace(evaluationThresholdsJson)) return fallback;
        try
        {
            using var document = JsonDocument.Parse(evaluationThresholdsJson);
            if (document.RootElement.TryGetProperty("minTestCases", out var min)
                && min.ValueKind == JsonValueKind.Number
                && min.TryGetInt32(out var value))
                return Math.Clamp(value, 1, 20);
        }
        catch (JsonException)
        {
            // fall through
        }
        return fallback;
    }

    public static List<(string Id, string Name, JsonElement Input)> ReadTestCases(JsonElement workflow)
    {
        var cases = new List<(string, string, JsonElement)>();
        if (!workflow.TryGetProperty("testCases", out var array)
            || array.ValueKind != JsonValueKind.Array)
            return cases;
        var index = 0;
        foreach (var entry in array.EnumerateArray())
        {
            index += 1;
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var id = entry.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() ?? $"case-{index}"
                : $"case-{index}";
            var name = entry.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString() ?? $"Case {index}"
                : $"Case {index}";
            if (!entry.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object)
                input = JsonSerializer.SerializeToElement(new Dictionary<string, string>());
            else
                input = input.Clone();
            cases.Add((id, name, input));
        }
        return cases;
    }
}
