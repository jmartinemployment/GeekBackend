using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

/// <summary>
/// Fail-closed VALIDATE gate: WRITE must not launder rival claim-risk text (superlatives/absolutes)
/// as verified truth (competitor-extraction §6.7).
/// </summary>
public static class GccV2CompetitorClaimRiskGate
{
    public static IReadOnlyList<string> CollectGaps(
        GccV2WriteOutput output,
        GccCompetitorExtractionDocument? extraction)
    {
        if (extraction is null || extraction.ClaimRiskFlags.Count == 0)
            return [];

        var risky = extraction.ClaimRiskFlags
            .Where(c =>
                !string.IsNullOrWhiteSpace(c.ClaimText)
                && (string.Equals(c.WriteGuidance, "do_not_echo_as_fact", StringComparison.OrdinalIgnoreCase)
                    || c.RiskKind is "superlative" or "absolute" or "unverifiable"))
            .ToList();
        if (risky.Count == 0) return [];

        var gaps = new List<string>();
        foreach (var section in output.AllSections)
        {
            if (section.Section is null) continue;
            var body = BuildPlainText(section.Section);
            if (string.IsNullOrWhiteSpace(body)) continue;
            var normalizedBody = GccV2PartnerMentionGate.Normalize(body);

            foreach (var flag in risky)
            {
                var needle = GccV2PartnerMentionGate.Normalize(flag.ClaimText);
                if (needle.Length < 12) continue;
                // Substantial span echo — not a one-word overlap.
                var probe = needle.Length > 80 ? needle[..80] : needle;
                if (!normalizedBody.Contains(probe, StringComparison.Ordinal))
                    continue;

                gaps.Add(
                    $"claim-risk '{flag.RiskKind}' echoed on section '{section.SectionKey}' "
                    + $"(guidance={flag.WriteGuidance}): do not present rival marketing as verified fact "
                    + $"({flag.OriginProofUrl})");
            }
        }

        return gaps.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string BuildPlainText(Section section)
    {
        var parts = new List<string>();
        Collect(section, parts);
        return string.Join("\n", parts);
    }

    private static void Collect(Section section, List<string> parts)
    {
        if (!string.IsNullOrWhiteSpace(section.Heading))
            parts.Add(section.Heading);
        foreach (var paragraph in section.Paragraphs)
        {
            switch (paragraph)
            {
                case TextParagraph text:
                    parts.Add(string.Join(" ", text.Runs.Select(r => r.Text)));
                    break;
                case ListParagraph list:
                    foreach (var item in list.Items)
                        parts.Add(string.Join(" ", item.Select(r => r.Text)));
                    break;
            }
        }

        foreach (var child in section.Children)
            Collect(child, parts);
    }
}
