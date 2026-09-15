using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Competitor;

/// <summary>
/// Completes the Information Gain note by joining our own site's coverage to what rivals cover.
///
/// The V2 builder was copied from v1's <c>GccSavedSerpParser.BuildPartialInformationGain</c> with the
/// <c>organics</c> parameter dropped, so <c>CompetitorOpens</c> was hardcoded empty and Information
/// Gain could only ever answer "what do we already cover?" — never "what are rivals covering that we
/// are not?", which is the half that makes it *gain*
/// (plans/rag-foundation-rewrite.md §3, W4).
///
/// Opens are derived from competitor-native extraction rather than a saved SERP upload: once the
/// competitor extractor looks for topical coverage, <c>GapMap</c> and <c>CoverageMap</c> are a
/// better source than a pasted results page and need no new operator input.
/// </summary>
public static class GccV2InformationGain
{
    /// <summary>
    /// Returns the note with <c>CompetitorOpens</c> populated from competitor coverage. When no
    /// competitor extraction is available the note is returned unchanged — "not computed" must stay
    /// distinguishable from "computed, nothing found".
    /// </summary>
    public static InformationGainNote? Enrich(
        InformationGainNote? note,
        GccCompetitorExtractionDocument? competitor)
    {
        if (note is null) return null;
        if (competitor is null) return note;

        var ourHosts = note.ThisSiteCovers
            .Select(HostOf)
            .Where(h => h is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        var opens = new List<string>();

        // Explicit gaps the extractor already framed as our opportunity.
        foreach (var gap in competitor.GapMap)
        {
            if (string.IsNullOrWhiteSpace(gap.GapTopic)) continue;
            opens.Add($"{gap.GapTopic} — {gap.OpportunityForUs} [{gap.DepthAssessment}]");
        }

        // Topics a rival covers on a host that is not ours.
        foreach (var coverage in competitor.CoverageMap)
        {
            if (string.IsNullOrWhiteSpace(coverage.TopicPath)) continue;
            var host = HostOf(coverage.OriginProofUrl);
            if (host is not null && ourHosts.Contains(host)) continue;
            opens.Add($"{coverage.TopicPath} (rival coverage: {coverage.DepthAssessment})");
        }

        var deduped = opens
            .GroupBy(o => o.Trim().ToLowerInvariant())
            .Where(g => g.Key.Length > 0)
            .Select(g => g.First())
            .Take(12)
            .ToList();

        var summary = deduped.Count == 0
            ? $"This site covers {note.ThisSiteCovers.Count} related page(s). "
              + "Competitor research ran and found no open topics to differentiate against."
            : $"This site covers {note.ThisSiteCovers.Count} related page(s); "
              + $"competitor research shows {deduped.Count} open topic(s) to differentiate against.";

        return note with { CompetitorOpens = deduped, Summary = summary };
    }

    private static string? HostOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Trim();
        // ThisSiteCovers entries are rendered as "url: title · heading"; take the leading token.
        var separator = candidate.IndexOf(": ", StringComparison.Ordinal);
        if (separator > 0) candidate = candidate[..separator];
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ? uri.Host : null;
    }
}
