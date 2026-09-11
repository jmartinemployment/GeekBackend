using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

/// <summary>
/// Deterministic readiness / scorecard deltas vs a prior succeeded run for the same subject.
/// </summary>
internal static class GccV2ArtifactChangeOverTime
{
    public sealed record DimensionDelta(
        string Dimension,
        double? Current,
        double? Prior,
        double? Delta);

    public sealed record ChangeOverTimeResult(
        bool Available,
        Guid? PriorRunId,
        DateTimeOffset? PriorCompletedAtUtc,
        string? SubjectKey,
        double? CurrentOverall,
        double? PriorOverall,
        double? OverallDelta,
        IReadOnlyList<DimensionDelta> Dimensions,
        string Message);

    public static string? SubjectKeyFromInput(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            var root = document.RootElement;
            foreach (var path in new[]
                     {
                         "pageUrl", "url", "subjectUrl", "ownedUrl", "competitorUrl",
                     })
            {
                if (TryReadString(root, path, out var direct) && !string.IsNullOrWhiteSpace(direct))
                    return NormalizeSubject(direct);
            }

            if (root.TryGetProperty("document", out var doc) && doc.ValueKind == JsonValueKind.Object)
            {
                foreach (var path in new[] { "url", "pageUrl", "canonicalUrl" })
                {
                    if (TryReadString(doc, path, out var nested) && !string.IsNullOrWhiteSpace(nested))
                        return NormalizeSubject(nested);
                }
            }

            if (root.TryGetProperty("ownedPage", out var owned) && owned.ValueKind == JsonValueKind.Object
                && TryReadString(owned, "url", out var ownedUrl) && !string.IsNullOrWhiteSpace(ownedUrl))
                return NormalizeSubject(ownedUrl);

            // Same exact input → same subject when no URL is present (re-audit of pasted body).
            if (root.TryGetProperty("document", out var bodyDoc)
                && bodyDoc.ValueKind == JsonValueKind.Object
                && TryReadString(bodyDoc, "bodyMarkdown", out var body)
                && !string.IsNullOrWhiteSpace(body))
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeSubject(body))))
                    .ToLowerInvariant();
                return $"body:{hash[..16]}";
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static bool TryReadScoreSnapshot(
        string? payloadJson,
        out double? overall,
        out Dictionary<string, double?> dimensions)
    {
        overall = null;
        dimensions = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(payloadJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.TryGetProperty("overallScore", out var scoreEl))
                overall = ReadNullableNumber(scoreEl);

            if (root.TryGetProperty("dimensions", out var dims) && dims.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in dims.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    if (!TryReadString(entry, "dimension", out var name) || string.IsNullOrWhiteSpace(name))
                        continue;
                    double? dimScore = null;
                    if (entry.TryGetProperty("score", out var dimScoreEl))
                        dimScore = ReadNullableNumber(dimScoreEl);
                    dimensions[name] = dimScore;
                }
            }

            return overall is not null || dimensions.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static ChangeOverTimeResult Compare(
        Guid? priorRunId,
        DateTimeOffset? priorCompletedAtUtc,
        string? subjectKey,
        double? currentOverall,
        Dictionary<string, double?> currentDimensions,
        double? priorOverall,
        Dictionary<string, double?> priorDimensions)
    {
        if (priorRunId is null)
        {
            return new ChangeOverTimeResult(
                false, null, null, subjectKey, currentOverall, null, null, [],
                "No prior succeeded run for this subject.");
        }

        double? overallDelta = currentOverall is { } cur && priorOverall is { } prior
            ? Math.Round(cur - prior, 2)
            : null;

        var names = currentDimensions.Keys
            .Union(priorDimensions.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var dimDeltas = names.Select(name =>
        {
            currentDimensions.TryGetValue(name, out var cur);
            priorDimensions.TryGetValue(name, out var prior);
            double? delta = cur is { } c && prior is { } p ? Math.Round(c - p, 2) : null;
            return new DimensionDelta(name, cur, prior, delta);
        }).ToList();

        var message = overallDelta is { } delta
            ? delta == 0
                ? "Overall score unchanged since last run."
                : delta > 0
                    ? $"Overall score up {delta.ToString("+0.##;-0.##", CultureInfo.InvariantCulture)} since last run."
                    : $"Overall score down {Math.Abs(delta).ToString("0.##", CultureInfo.InvariantCulture)} since last run."
            : "Compared to prior run (overall score unavailable on one side).";

        return new ChangeOverTimeResult(
            true,
            priorRunId,
            priorCompletedAtUtc,
            subjectKey,
            currentOverall,
            priorOverall,
            overallDelta,
            dimDeltas,
            message);
    }

    public static ChangeOverTimeResult? FromRuns(
        GccV2TaskRunDto current,
        string currentCapabilityId,
        IReadOnlyList<GccV2TaskRunDto> candidatePriors,
        Func<Guid, string?> capabilityForDefinition)
    {
        if (!string.Equals(current.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            return null;

        var subjectKey = SubjectKeyFromInput(current.InputJson);
        if (string.IsNullOrWhiteSpace(subjectKey))
            return Compare(null, null, null, null, [], null, []);

        var currentPayload = LatestPayload(current);
        if (!TryReadScoreSnapshot(currentPayload, out var currentOverall, out var currentDims))
            return null;

        var prior = candidatePriors
            .Where(run => run.Id != current.Id)
            .Where(run => string.Equals(run.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            .Where(run =>
            {
                var capability = capabilityForDefinition(run.TaskAgentDefinitionId);
                return string.Equals(capability, currentCapabilityId, StringComparison.OrdinalIgnoreCase);
            })
            .Where(run => string.Equals(SubjectKeyFromInput(run.InputJson), subjectKey, StringComparison.Ordinal))
            .Where(run => (run.CompletedAtUtc ?? run.UpdatedAtUtc) < (current.CompletedAtUtc ?? current.UpdatedAtUtc))
            .OrderByDescending(run => run.CompletedAtUtc ?? run.UpdatedAtUtc)
            .FirstOrDefault();

        if (prior is null)
            return Compare(null, null, subjectKey, currentOverall, currentDims, null, []);

        TryReadScoreSnapshot(LatestPayload(prior), out var priorOverall, out var priorDims);
        return Compare(
            prior.Id,
            prior.CompletedAtUtc ?? prior.UpdatedAtUtc,
            subjectKey,
            currentOverall,
            currentDims,
            priorOverall,
            priorDims);
    }

    private static string? LatestPayload(GccV2TaskRunDto run) =>
        run.Artifacts?
            .SelectMany(a => a.Versions ?? [])
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => v.PayloadJson)
            .FirstOrDefault();

    private static string NormalizeSubject(string value) =>
        value.Trim().TrimEnd('/').ToLowerInvariant();

    private static bool TryReadString(JsonElement parent, string name, out string? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
            return false;
        value = el.GetString();
        return true;
    }

    private static double? ReadNullableNumber(JsonElement el) =>
        el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var number)
            ? number
            : el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : null;
}
