using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Stage 2 (heading provenance): pure logic. Binary, queryable -- a provenance tag either resolves
/// against the evidence a generation call actually had, or it doesn't. No similarity matching.
/// </summary>
public class GccHeadingProvenanceGuardTests
{
    private static GccHeadingProvenanceEvidence Evidence(
        IEnumerable<string>? fields = null,
        IEnumerable<string>? paa = null,
        IEnumerable<string>? competitor = null,
        IEnumerable<string>? site = null,
        IEnumerable<string>? retrieved = null) =>
        new(
            new HashSet<string>(fields ?? [], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(paa ?? [], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(competitor ?? [], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(site ?? [], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(retrieved ?? [], StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void A_heading_covering_one_of_the_sites_own_subtopics_is_licensed()
    {
        // The must-mention block calls these compulsory and licensed none of them, so a heading
        // written to obey it could not be tagged and the draft was refused.
        var evidence = Evidence(site: ["Invoice capture"]);

        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("How invoices arrive", "site:Invoice capture")], evidence);

        Assert.Empty(violations);
    }

    [Fact]
    public void A_site_tag_naming_the_list_rather_than_a_subtopic_is_refused()
    {
        // The exact failure: the model described where it had found the material instead of quoting
        // it -- "brief:Subtopics the site already treats under it, all of which this piece must
        // cover" -- and that must keep failing, whichever kind it is filed under.
        var evidence = Evidence(site: ["Invoice capture"]);

        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("Top Tools for Streamlining Your Accounts Payable",
                 "site:Subtopics the site already treats under it, all of which this piece must cover")],
            evidence);

        Assert.Single(violations);
    }

    [Fact]
    public void A_site_tag_with_no_subtopics_supplied_is_refused()
    {
        // No matched section is no source. Absence of evidence never licenses a heading.
        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("Invented", "site:Invoice capture")], Evidence());

        Assert.Single(violations);
    }

    private static Section Sec(string heading, string? provenance, IReadOnlyList<Section>? children = null) =>
        new("h3", heading, [], null, children ?? [], Provenance: provenance);

    [Fact]
    public void PlanTagIsAlwaysLicensedEvenWithNoOtherEvidence()
    {
        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings([Sec("Overview", "plan")], Evidence());
        Assert.Empty(violations);
    }

    [Fact]
    public void MissingProvenanceIsAViolation()
    {
        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings([Sec("Overview", null)], Evidence());
        Assert.Single(violations);
    }

    [Fact]
    public void EmptyStringProvenanceIsAViolation()
    {
        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings([Sec("Overview", "")], Evidence());
        Assert.Single(violations);
    }

    [Fact]
    public void RetrievalTagIsNoLongerARecognizedKind()
    {
        // "retrieval:<url>" was removed 2026-09-22 -- it checked a claimed source URL against
        // create.ResearchJson's Quoteables, an optional, often-empty set, so it failed on missing
        // research rather than on bad output. Now an unrecognized kind, same as any other.
        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "retrieval:https://partner.test/page")], Evidence());
        Assert.Single(violations);
    }

    [Fact]
    public void BriefTagResolvesOnlyAgainstAPopulatedFieldName()
    {
        var evidence = Evidence(fields: ["primaryIntent"]);

        Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "brief:primaryIntent")], evidence));
        Assert.Single(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "brief:angle")], evidence));
    }

    [Fact]
    public void PaaTagResolvesOnlyAgainstACuratedQuestion()
    {
        var evidence = Evidence(paa: ["What is AI implementation?"]);

        Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "paa:What is AI implementation?")], evidence));
        Assert.Single(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "paa:What is something else?")], evidence));
    }

    [Fact]
    public void CompetitorTagResolvesOnlyAgainstAKnownCompetitorHeading()
    {
        var evidence = Evidence(competitor: ["Enterprise Pricing"]);

        Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "competitor:Enterprise Pricing")], evidence));
        Assert.Single(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "competitor:Something Else")], evidence));
    }

    [Fact]
    public void UnknownTagKindIsAViolationNotSilentlyAccepted()
    {
        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "wikipedia:https://en.wikipedia.org/wiki/AI")], Evidence());
        Assert.Single(violations);
    }

    [Fact]
    public void NestedChildrenAreCheckedTooNotJustTopLevelSections()
    {
        var child = Sec("Bad Child", null);
        var parent = Sec("Good Parent", "plan", [child]);

        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings([parent], Evidence());

        Assert.Single(violations);
        Assert.Contains("Bad Child", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AFullyLicensedTreeReturnsNoViolations()
    {
        var evidence = Evidence(competitor: ["Nested Gap"]);
        // Fills the gap that competitor heading revealed, in this page's own words -- which is what
        // a "competitor:" tag licenses. This test used to head the child "Nested Gap", the exact
        // text it cites, and assert that as fully licensed: the copy the guard now rejects.
        var child = Sec("What the rollout actually takes", "competitor:Nested Gap");
        var parent = Sec("Top", "plan", [child]);

        Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings([parent], evidence));
    }

    [Fact]
    public void ACompetitorHeadingReusedVerbatimAsTheSectionHeadingIsAViolation()
    {
        // The shape grounding was quietly rewarding. Licensing a heading needs an exact-match tag,
        // and the prompt renders up to 125 competitor headings beside that rule -- so lifting one
        // and quoting it back as its own source satisfied the constraint perfectly, and every
        // grounded page drifted toward the outline every page in the niche already has (Jeff,
        // 2026-09-23: "With RAG the content is far worse").
        var evidence = Evidence(competitor: ["Common Challenges"]);

        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("Common Challenges", "competitor:Common Challenges")], evidence);

        Assert.Single(violations);
        Assert.Contains("copied verbatim", violations[0]);
    }

    [Fact]
    public void TheCopyCheckIgnoresCaseAndSurroundingWhitespace()
    {
        var evidence = Evidence(competitor: ["Enterprise Pricing"]);

        Assert.Single(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("  enterprise pricing ", "competitor:Enterprise Pricing")], evidence));
    }

    [Fact]
    public void MatchingIsCaseInsensitiveNotExactByteEquality()
    {
        var evidence = Evidence(competitor: ["Enterprise Pricing"]);

        Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "competitor:enterprise pricing")], evidence));
    }

    [Fact]
    public void OneViolationIsReportedPerUnlicensedSectionNotJustTheFirst()
    {
        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("First", null), Sec("Second", "brief:nonexistent")], Evidence());

        Assert.Equal(2, violations.Count);
    }
}

/// <summary>
/// The `evidence:` channel, added 2026-09-29. It restores the only licensing route for what the
/// Library retrieved -- `retrieval:&lt;url&gt;` was removed on 2026-09-22, leaving the guard with no
/// way to license a heading built on a retrieved passage.
///
/// <para>
/// The property that answers the original objection: this channel only ever lets a heading
/// <em>pass</em>. An empty retrieved set licenses nothing and refuses nothing extra, so a create
/// with no research behaves exactly as it did before the channel existed.
/// </para>
/// </summary>
public class GccHeadingProvenanceRetrievedEvidenceTests
{
    private static Section Sec(string heading, string? provenance) =>
        new("h2", heading, [], null, [], Provenance: provenance);

    private static GccHeadingProvenanceEvidence Retrieved(params string[] retrieved) =>
        new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(retrieved, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void A_heading_built_on_a_retrieved_partner_is_licensed_by_its_name()
    {
        // The exact shape discarded on 2026-09-28: one heading per partner tool, which is what the
        // required-mentions block asks for, with no tag that could license it.
        var evidence = Retrieved("Melio", "Dext", "Lightyear");

        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [
                Sec("Melio: Simplify Payments", "evidence:Melio"),
                Sec("Dext: Accurate Data Capture", "evidence:Dext"),
            ],
            evidence);

        Assert.Empty(violations);
    }

    [Fact]
    public void A_heading_may_cite_the_section_the_passage_sat_under()
    {
        var evidence = Retrieved("Invoice capture", "tipalti.com");

        Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("How invoices arrive", "evidence:Invoice capture")], evidence));
        Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("What the vendor claims", "evidence:tipalti.com")], evidence));
    }

    [Fact]
    public void A_tag_naming_evidence_that_was_not_retrieved_is_still_refused()
    {
        // The guard stays binary. Naming a partner the Library did not return is exactly the
        // hallucination this stage exists to catch, and it must not become licensable just because
        // the channel now exists.
        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("Bill.com: Smart Approvals", "evidence:Bill.com")], Retrieved("Melio"));

        var violation = Assert.Single(violations);
        Assert.Contains("does not resolve to any available source", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_retrieved_set_licenses_nothing_and_refuses_nothing_extra()
    {
        // The answer to why retrieval:<url> was removed: that rule made generation FAIL on missing
        // research. This one cannot. With no research, an evidence: tag simply does not license --
        // the same refusal an unknown brief field would get, and no new failure mode.
        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("Anything", "evidence:Anything")], Retrieved());

        Assert.Single(violations);
    }
}
