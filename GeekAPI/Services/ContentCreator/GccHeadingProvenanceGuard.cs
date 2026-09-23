using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The real evidence one specific generation call had available -- the concrete set a model's
/// provenance tags are checked against. Assembled once per call (<c>GccGenerateService</c>) and
/// used for both the prompt context (what the model is shown) and the guard below (what its tags
/// must resolve to), so the two can never drift out of sync with each other.
/// </summary>
public sealed record GccHeadingProvenanceEvidence(
    IReadOnlySet<string> PopulatedBriefFields,
    IReadOnlySet<string> PaaQuestions,
    IReadOnlySet<string> CompetitorHeadings);

/// <summary>
/// Stage 2 (heading provenance). Every section a model invents beyond its assigned outline --
/// pillar's required h3/h4 children chief among them -- must be licensed by real material, not
/// invented from nothing. Binary, queryable: a provenance tag either resolves against <see
/// cref="GccHeadingProvenanceEvidence"/> or it doesn't. No text-similarity matching.
/// </summary>
public static class GccHeadingProvenanceGuard
{
    /// <summary>
    /// Walks every section in the tree, at every depth, and returns one message per section whose
    /// provenance tag is missing or does not resolve against <paramref name="evidence"/>. Empty
    /// means every heading in the tree is licensed -- the caller's signal to proceed.
    /// </summary>
    public static IReadOnlyList<string> FindUnlicensedHeadings(
        IReadOnlyList<Section> sections, GccHeadingProvenanceEvidence evidence)
    {
        var violations = new List<string>();
        Walk(sections, evidence, violations);
        return violations;
    }

    private static void Walk(
        IReadOnlyList<Section> sections, GccHeadingProvenanceEvidence evidence, List<string> violations)
    {
        foreach (var section in sections)
        {
            if (!IsLicensed(section.Provenance, evidence))
            {
                violations.Add(
                    $"\"{section.Heading}\" ({section.Tag}): provenance " +
                    $"\"{section.Provenance ?? "(missing)"}\" does not resolve to any available source.");
            }
            else if (IsCopiedCompetitorHeading(section))
            {
                violations.Add(
                    $"\"{section.Heading}\" ({section.Tag}): copied verbatim from the competitor heading it " +
                    "cites. A competitor heading licenses a gap worth covering, never the words at the top of " +
                    "the section that covers it.");
            }

            if (section.Children.Count > 0)
            {
                Walk(section.Children, evidence, violations);
            }
        }
    }

    /// <summary>
    /// A section tagged <c>competitor:&lt;text&gt;</c> whose own heading is that same text.
    ///
    /// <para>
    /// This is the one shape the provenance rules accidentally rewarded. Licensing a heading
    /// required an exact-match tag against real material, and up to 125 competitor headings are
    /// rendered into the prompt beside that rule -- so the cheapest way to satisfy a hard,
    /// fail-closed constraint was to lift a competitor's heading and quote it back as the tag,
    /// which matched exactly and passed. Grounding then pushed every page toward the aggregate
    /// shape of the pages already ranking, which is where "Overview", "Key Benefits", "Common
    /// Challenges" and "Final Thoughts" live. Jeff, 2026-09-23: "With RAG the content is far
    /// worse."
    /// </para>
    ///
    /// <para>
    /// Exact match, trimmed and case-insensitive -- the same binary, queryable test as the rest of
    /// this guard, and deliberately not a similarity score. A section that genuinely fills the gap
    /// a competitor heading revealed still passes; only reproducing the words fails.
    /// </para>
    /// </summary>
    private static bool IsCopiedCompetitorHeading(Section section)
    {
        var provenance = section.Provenance;
        if (string.IsNullOrWhiteSpace(provenance))
        {
            return false;
        }

        var colonIndex = provenance.IndexOf(':');
        if (colonIndex < 0
            || !provenance[..colonIndex].Trim().Equals("competitor", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var cited = provenance[(colonIndex + 1)..].Trim();
        return cited.Length > 0
               && cited.Equals(section.Heading?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLicensed(string? provenance, GccHeadingProvenanceEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(provenance))
        {
            return false;
        }

        var colonIndex = provenance.IndexOf(':');
        var kind = (colonIndex < 0 ? provenance : provenance[..colonIndex]).Trim().ToLowerInvariant();
        var value = colonIndex < 0 ? null : provenance[(colonIndex + 1)..].Trim();

        return kind switch
        {
            "plan" => true,
            // "retrieval:<url>" removed 2026-09-22 (Jeff, "remove this stupid rule"): it checked a
            // heading's claimed source URL against create.ResearchJson's Quoteables -- an optional,
            // operator-uploaded, often-empty set unrelated to whether the model actually
            // hallucinated anything. That made it fail on missing research, not on bad output.
            "brief" => value is { Length: > 0 } && evidence.PopulatedBriefFields.Contains(value),
            "paa" => value is { Length: > 0 } && evidence.PaaQuestions.Contains(value),
            "competitor" => value is { Length: > 0 } && evidence.CompetitorHeadings.Contains(value),
            _ => false,
        };
    }
}
