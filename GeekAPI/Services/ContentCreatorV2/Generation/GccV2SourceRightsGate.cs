using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Rag;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

/// <summary>
/// P1.5 sourceRights provenance: missing ≡ unknown; brief override wins;
/// prohibited/unknown on displayed citations block shipReady.
/// </summary>
public static class GccV2SourceRightsGate
{
    public const string Consented = "consented";
    public const string Licensed = "licensed";
    public const string Unknown = "unknown";
    public const string Prohibited = "prohibited";

    public static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        Consented, Licensed, Unknown, Prohibited,
    };

    /// <summary>Normalize raw value; blank/missing/invalid → unknown.</summary>
    public static string Normalize(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        return Allowed.Contains(v) ? v : Unknown;
    }

    public static bool IsShipAllowed(string? raw)
    {
        var v = Normalize(raw);
        return v is Consented or Licensed;
    }

    /// <summary>
    /// Brief override map: keys are <c>runId|pageId</c>, <c>pageId</c>, or <c>runId</c>.
    /// Precedence for a citation: exact runId|pageId → pageId → runId → citation field → unknown.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseBriefOverrides(string? rawBriefJson)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return map;
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("sourceRightsOverrides", out var root)
                || root.ValueKind != JsonValueKind.Object)
                return map;

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var key = prop.Name.Trim();
                if (key.Length == 0) continue;
                map[key] = Normalize(prop.Value.GetString());
            }
        }
        catch (JsonException)
        {
            // Invalid brief → no overrides.
        }

        return map;
    }

    public static string Resolve(
        RagCitationDto citation,
        IReadOnlyDictionary<string, string>? briefOverrides)
    {
        if (briefOverrides is { Count: > 0 })
        {
            var pageId = (citation.PageId ?? "").Trim();
            var runId = (citation.RunId ?? "").Trim();
            if (pageId.Length > 0 && runId.Length > 0)
            {
                var compound = $"{runId}|{pageId}";
                if (briefOverrides.TryGetValue(compound, out var byBoth))
                    return Normalize(byBoth);
            }

            if (pageId.Length > 0 && briefOverrides.TryGetValue(pageId, out var byPage))
                return Normalize(byPage);
            if (runId.Length > 0 && briefOverrides.TryGetValue(runId, out var byRun))
                return Normalize(byRun);
        }

        return Normalize(citation.SourceRights);
    }

    /// <summary>
    /// Stamp resolved sourceRights onto citations and return blocking gaps for non-shippable values.
    /// Gap: <c>sourceRights '{value}' on citation for section '{sectionKey}'</c>
    /// </summary>
    public static (IReadOnlyList<RagCitationDto> Citations, IReadOnlyList<string> Gaps) ApplyAndCollectGaps(
        GccV2WriteOutput output,
        IReadOnlyDictionary<string, string>? briefOverrides)
    {
        var gaps = new List<string>();
        var stamped = new List<RagCitationDto>();

        foreach (var section in output.AllSections)
        {
            foreach (var citation in section.Citations ?? [])
            {
                var rights = Resolve(citation, briefOverrides);
                var sectionKey = string.IsNullOrWhiteSpace(citation.SectionKey)
                    ? section.SectionKey
                    : citation.SectionKey!;
                var next = CloneWithRights(citation, sectionKey, rights);
                stamped.Add(next);
                if (!IsShipAllowed(rights))
                {
                    gaps.Add($"sourceRights '{rights}' on citation for section '{sectionKey}'");
                }
            }
        }

        return (stamped, gaps.Distinct(StringComparer.Ordinal).ToList());
    }

    private static RagCitationDto CloneWithRights(RagCitationDto c, string sectionKey, string rights) =>
        new()
        {
            PageId = c.PageId,
            RunId = c.RunId,
            Url = c.Url,
            Title = c.Title,
            SectionTitle = c.SectionTitle,
            SectionKey = sectionKey,
            Quote = c.Quote,
            CrawlType = c.CrawlType,
            SourceDigest = c.SourceDigest,
            Verified = c.Verified,
            SourceRights = rights,
        };
}
