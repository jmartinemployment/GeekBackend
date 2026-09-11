using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

/// <summary>
/// Deterministic readiness scorecard and competitor-finding deltas vs a prior succeeded run
/// for the same subject.
/// </summary>
internal static class GccV2ArtifactChangeOverTime
{
    public sealed record DimensionDelta(
        string Dimension,
        double? Current,
        double? Prior,
        double? Delta);

    public sealed record FindingDelta(
        string Key,
        string Change,
        string? CurrentPriority,
        string? PriorPriority,
        string Summary);

    public sealed record ChangeOverTimeResult(
        bool Available,
        Guid? PriorRunId,
        DateTimeOffset? PriorCompletedAtUtc,
        string? SubjectKey,
        double? CurrentOverall,
        double? PriorOverall,
        double? OverallDelta,
        IReadOnlyList<DimensionDelta> Dimensions,
        IReadOnlyList<FindingDelta> Findings,
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

            foreach (var pageArrayName in new[] { "subjectPages", "brandPages" })
            {
                if (!root.TryGetProperty(pageArrayName, out var pages)
                    || pages.ValueKind != JsonValueKind.Array
                    || pages.GetArrayLength() == 0)
                {
                    continue;
                }

                var first = pages[0];
                if (first.ValueKind != JsonValueKind.Object) continue;
                if (TryReadPageUrl(first, out var pageUrl) && !string.IsNullOrWhiteSpace(pageUrl))
                    return NormalizeSubject(pageUrl!);
                if (TryReadString(first, "visibleContent", out var visible)
                    && !string.IsNullOrWhiteSpace(visible))
                {
                    return BodySubjectKey(visible!);
                }
            }

            // Same exact input → same subject when no URL is present (re-audit of pasted body).
            if (root.TryGetProperty("document", out var bodyDoc)
                && bodyDoc.ValueKind == JsonValueKind.Object
                && TryReadString(bodyDoc, "bodyMarkdown", out var body)
                && !string.IsNullOrWhiteSpace(body))
            {
                return BodySubjectKey(body!);
            }

            if (root.TryGetProperty("document", out var contentDoc)
                && contentDoc.ValueKind == JsonValueKind.Object
                && TryReadString(contentDoc, "visibleContent", out var content)
                && !string.IsNullOrWhiteSpace(content))
            {
                return BodySubjectKey(content!);
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

    public static bool TryReadFindingSnapshot(
        string? payloadJson,
        out Dictionary<string, FindingSnapshot> findings)
    {
        findings = new Dictionary<string, FindingSnapshot>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(payloadJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.TryGetProperty("prioritizedActions", out var actions)
                && actions.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in actions.EnumerateArray())
                    TryAddActionFinding(entry, findings);
            }

            if (root.TryGetProperty("contentGap", out var contentGap)
                && contentGap.ValueKind == JsonValueKind.Object
                && contentGap.TryGetProperty("gaps", out var nestedGaps)
                && nestedGaps.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in nestedGaps.EnumerateArray())
                    TryAddGapFinding(entry, findings);
            }

            if (root.TryGetProperty("gaps", out var gaps) && gaps.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in gaps.EnumerateArray())
                    TryAddGapFinding(entry, findings);
            }

            return findings.Count > 0;
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
                false, null, null, subjectKey, currentOverall, null, null, [], [],
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
            [],
            message);
    }

    public static ChangeOverTimeResult CompareFindings(
        Guid? priorRunId,
        DateTimeOffset? priorCompletedAtUtc,
        string? subjectKey,
        IReadOnlyDictionary<string, FindingSnapshot> current,
        IReadOnlyDictionary<string, FindingSnapshot> prior)
    {
        if (priorRunId is null)
        {
            return new ChangeOverTimeResult(
                false, null, null, subjectKey, null, null, null, [], [],
                "No prior succeeded run for this subject.");
        }

        var deltas = new List<FindingDelta>();
        foreach (var key in current.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            var cur = current[key];
            if (!prior.TryGetValue(key, out var old))
            {
                deltas.Add(new FindingDelta(key, "added", cur.Priority, null, cur.Summary));
                continue;
            }

            if (!string.Equals(cur.Priority, old.Priority, StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(cur.Priority) || !string.IsNullOrWhiteSpace(old.Priority)))
            {
                deltas.Add(new FindingDelta(
                    key, "priorityChanged", cur.Priority, old.Priority, cur.Summary));
            }
        }

        foreach (var key in prior.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (current.ContainsKey(key)) continue;
            var old = prior[key];
            deltas.Add(new FindingDelta(key, "removed", null, old.Priority, old.Summary));
        }

        var added = deltas.Count(x => x.Change == "added");
        var removed = deltas.Count(x => x.Change == "removed");
        var shifted = deltas.Count(x => x.Change == "priorityChanged");
        var parts = new List<string>();
        if (added > 0) parts.Add($"{added} finding{(added == 1 ? "" : "s")} added");
        if (removed > 0) parts.Add($"{removed} removed");
        if (shifted > 0) parts.Add($"{shifted} priority shifted");
        var message = parts.Count == 0
            ? "Competitor findings unchanged since last audit."
            : string.Join(", ", parts) + " since last audit.";

        return new ChangeOverTimeResult(
            true,
            priorRunId,
            priorCompletedAtUtc,
            subjectKey,
            null,
            null,
            null,
            [],
            deltas,
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
        var hasScores = TryReadScoreSnapshot(currentPayload, out var currentOverall, out var currentDims);
        var hasFindings = TryReadFindingSnapshot(currentPayload, out var currentFindings);
        if (!hasScores && !hasFindings)
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
        {
            return hasScores
                ? Compare(null, null, subjectKey, currentOverall, currentDims, null, [])
                : CompareFindings(null, null, subjectKey, currentFindings, new Dictionary<string, FindingSnapshot>());
        }

        var priorPayload = LatestPayload(prior);
        if (hasScores && TryReadScoreSnapshot(priorPayload, out var priorOverall, out var priorDims))
        {
            return Compare(
                prior.Id,
                prior.CompletedAtUtc ?? prior.UpdatedAtUtc,
                subjectKey,
                currentOverall,
                currentDims,
                priorOverall,
                priorDims);
        }

        if (hasFindings)
        {
            TryReadFindingSnapshot(priorPayload, out var priorFindings);
            return CompareFindings(
                prior.Id,
                prior.CompletedAtUtc ?? prior.UpdatedAtUtc,
                subjectKey,
                currentFindings,
                priorFindings);
        }

        return null;
    }

    public sealed record FindingSnapshot(string Priority, string Summary);

    private static string? LatestPayload(GccV2TaskRunDto run) =>
        run.Artifacts?
            .SelectMany(a => a.Versions ?? [])
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => v.PayloadJson)
            .FirstOrDefault();

    private static string BodySubjectKey(string body)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeSubject(body))))
            .ToLowerInvariant();
        return $"body:{hash[..16]}";
    }

    private static string NormalizeSubject(string value) =>
        value.Trim().TrimEnd('/').ToLowerInvariant();

    private static bool TryReadPageUrl(JsonElement page, out string? value)
    {
        if (TryReadString(page, "url", out value) && !string.IsNullOrWhiteSpace(value))
            return true;
        if (page.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object
            && TryReadString(source, "url", out value) && !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static void TryAddActionFinding(
        JsonElement entry, Dictionary<string, FindingSnapshot> findings)
    {
        if (entry.ValueKind != JsonValueKind.Object) return;
        TryReadString(entry, "actionId", out var actionId);
        TryReadString(entry, "action", out var action);
        TryReadString(entry, "dimension", out var dimension);
        TryReadString(entry, "priority", out var priority);
        var summary = !string.IsNullOrWhiteSpace(action)
            ? action!
            : !string.IsNullOrWhiteSpace(dimension)
                ? dimension!
                : "action";
        var key = !string.IsNullOrWhiteSpace(actionId)
            ? $"action:{actionId}"
            : $"action:{NormalizeSubject($"{dimension}|{summary}")}";
        findings[key] = new FindingSnapshot(priority ?? "", Truncate(summary));
    }

    private static void TryAddGapFinding(
        JsonElement entry, Dictionary<string, FindingSnapshot> findings)
    {
        if (entry.ValueKind != JsonValueKind.Object) return;
        TryReadString(entry, "gapId", out var gapId);
        TryReadString(entry, "dimension", out var dimension);
        TryReadString(entry, "status", out var status);
        var summary = !string.IsNullOrWhiteSpace(dimension)
            ? $"{dimension} ({status ?? "unknown"})"
            : status ?? "gap";
        var key = !string.IsNullOrWhiteSpace(gapId)
            ? $"gap:{gapId}"
            : $"gap:{NormalizeSubject(summary)}";
        findings[key] = new FindingSnapshot(status ?? "", Truncate(summary));
    }

    private static string Truncate(string value) =>
        value.Length <= 160 ? value : value[..157] + "...";

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
