using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The real evidence one specific generation call had available -- the concrete set a model's
/// provenance tags are checked against. Assembled once per call (<c>GccGenerateService</c>) and
/// used for both the prompt context (what the model is shown) and the guard below (what its tags
/// must resolve to), so the two can never drift out of sync with each other.
/// </summary>
public sealed record GccHeadingProvenanceEvidence(
    IReadOnlySet<string> RetrievalUrls,
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

            if (section.Children.Count > 0)
            {
                Walk(section.Children, evidence, violations);
            }
        }
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
            "retrieval" => value is { Length: > 0 } && evidence.RetrievalUrls.Contains(value),
            "brief" => value is { Length: > 0 } && evidence.PopulatedBriefFields.Contains(value),
            "paa" => value is { Length: > 0 } && evidence.PaaQuestions.Contains(value),
            "competitor" => value is { Length: > 0 } && evidence.CompetitorHeadings.Contains(value),
            _ => false,
        };
    }
}
