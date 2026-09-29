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
    IReadOnlySet<string> CompetitorHeadings,
    /// <summary>
    /// The publisher's own subtopics for this section, from the must-mention block. Compulsory
    /// content -- the block says "all of which this piece must cover" -- and until 2026-09-28 they
    /// licensed nothing, so a heading written to obey that instruction could not be tagged and the
    /// draft was refused for it.
    /// </summary>
    IReadOnlySet<string> SiteSubtopics,
    /// <summary>
    /// What the Library actually retrieved for this create: each passage's section title, its
    /// page title, its host, and the partner spelling that host is written with.
    ///
    /// <para>
    /// This replaces the <c>retrieval:&lt;url&gt;</c> rule removed on 2026-09-22, and the objection
    /// that removed it is answered rather than ignored. That rule made generation *fail on missing
    /// research*: it checked a heading's claimed source URL against a set that was optional,
    /// operator-uploaded and often empty. Two things changed. The UI no longer supplies
    /// operator-uploaded quoteables at all, so the set is retrieved evidence or nothing (Jeff,
    /// 2026-09-29). And <c>GccGroundingResolver</c> now refuses a create outright when no indexed
    /// run returns a citable passage, so an empty set cannot reach generation.
    /// </para>
    ///
    /// <para>
    /// It licenses; it never requires. An empty set licenses nothing and changes no outcome — it
    /// adds a way for a heading to pass, never a new way for a draft to fail. That is the whole
    /// difference from the rule that was removed.
    /// </para>
    ///
    /// <para>
    /// Why it has to exist: only Tool *requires* a citeable quote — for Blog and Pillar evidence is
    /// nice-to-have. But heading provenance is enforced identically on all three, so a Blog draft
    /// that voluntarily built structure on retrieved evidence was refused while one that ignored the
    /// Library passed. On 2026-09-28 a blog draft was discarded carrying one heading per partner
    /// tool -- Melio, Dext, Lightyear, Stampli, AvidXchange -- exactly what the required-mentions
    /// block asks for, with no tag available that could license any of them.
    /// </para>
    /// </summary>
    IReadOnlySet<string> RetrievedEvidence);

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
            // "retrieval:<url>" was removed 2026-09-22 (Jeff, "remove this stupid rule") because it
            // checked a heading's claimed source URL against an optional, operator-uploaded,
            // often-empty set, and so failed on missing research rather than on bad output. The
            // replacement below is keyed on what was retrieved rather than on a URL the model
            // claims, and it cannot fail a draft -- see RetrievedEvidence for why that objection no
            // longer applies.
            "brief" => value is { Length: > 0 } && evidence.PopulatedBriefFields.Contains(value),
            "paa" => value is { Length: > 0 } && evidence.PaaQuestions.Contains(value),
            "competitor" => value is { Length: > 0 } && evidence.CompetitorHeadings.Contains(value),
            "site" => value is { Length: > 0 } && evidence.SiteSubtopics.Contains(value),
            "evidence" => value is { Length: > 0 } && evidence.RetrievedEvidence.Contains(value),
            _ => false,
        };
    }
}
