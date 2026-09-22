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
        IEnumerable<string>? urls = null,
        IEnumerable<string>? fields = null,
        IEnumerable<string>? paa = null,
        IEnumerable<string>? competitor = null) =>
        new(
            new HashSet<string>(urls ?? [], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(fields ?? [], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(paa ?? [], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(competitor ?? [], StringComparer.OrdinalIgnoreCase));

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
    public void RetrievalTagResolvesOnlyAgainstAUrlActuallyInEvidence()
    {
        var evidence = Evidence(urls: ["https://partner.test/page"]);

        Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "retrieval:https://partner.test/page")], evidence));
        Assert.Single(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "retrieval:https://unknown.test/page")], evidence));
    }

    [Fact]
    public void BriefTagResolvesOnlyAgainstAPopulatedFieldName()
    {
        var evidence = Evidence(fields: ["primaryIntent"]);

        Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "brief:primaryIntent")], evidence));
        Assert.Single(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "brief:ctaType")], evidence));
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
        var child = Sec("Nested Gap", "competitor:Nested Gap");
        var parent = Sec("Top", "plan", [child]);

        Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings([parent], evidence));
    }

    [Fact]
    public void MatchingIsCaseInsensitiveNotExactByteEquality()
    {
        var evidence = Evidence(urls: ["https://Partner.Test/Page"]);

        Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("X", "retrieval:https://partner.test/page")], evidence));
    }

    [Fact]
    public void OneViolationIsReportedPerUnlicensedSectionNotJustTheFirst()
    {
        var violations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(
            [Sec("First", null), Sec("Second", "brief:nonexistent")], Evidence());

        Assert.Equal(2, violations.Count);
    }
}
