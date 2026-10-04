using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The coordinator's own rules, which had no test at all until 2026-10-01.
///
/// <para>
/// <c>NormalizeRequestedTypes</c>, <c>ValidateRequestedTypes</c> and <c>MergeRetrievedEvidence</c>
/// decide what Generate refuses before it starts and what evidence reaches the prompt. The file is
/// about to be cut into — grounding moves out of the per-type fan-out — so these exist to prove the
/// parts that must not change did not.
/// </para>
/// </summary>
public class GccGenerationCoordinatorTests
{
    // ---- NormalizeRequestedTypes -------------------------------------------------------------

    [Fact]
    public void Requested_types_are_trimmed_deduped_and_blanks_dropped()
    {
        var types = GccGenerationCoordinator.NormalizeRequestedTypes(
            [" pillar ", "blog", "", "   ", "PILLAR", "blog"]);

        Assert.Equal(["pillar", "blog"], types);
    }

    [Fact]
    public void Deduplication_is_case_insensitive_and_keeps_the_first_spelling()
    {
        // "pillar" and "PILLAR" are one type. Keeping the first spelling matters because the value
        // is echoed back to the operator in refusals.
        Assert.Equal(["Pillar"], GccGenerationCoordinator.NormalizeRequestedTypes(["Pillar", "pillar"]));
    }

    [Fact]
    public void A_null_or_empty_request_normalizes_to_nothing_rather_than_a_default()
    {
        Assert.Empty(GccGenerationCoordinator.NormalizeRequestedTypes(null));
        Assert.Empty(GccGenerationCoordinator.NormalizeRequestedTypes([]));
    }

    // ---- ValidateRequestedTypes --------------------------------------------------------------

    [Fact]
    public void No_requested_type_is_refused_and_never_defaulted()
    {
        // An omitted outputTypes used to fall back to the type the create happened to be minted
        // with -- the default-content-type pattern removed everywhere else.
        var refusal = GccGenerationCoordinator.ValidateRequestedTypes([]);

        Assert.NotNull(refusal);
        Assert.Contains("no default or fallback type", refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    public void An_implemented_type_is_allowed(string type)
    {
        Assert.Null(GccGenerationCoordinator.ValidateRequestedTypes([type]));
    }

    [Fact]
    public void A_disabled_type_is_refused_and_named()
    {
        var refusal = GccGenerationCoordinator.ValidateRequestedTypes(["pillar", "whitepaper"]);

        Assert.NotNull(refusal);
        Assert.Contains("whitepaper", refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_disabled_type_is_named_not_just_the_first()
    {
        // One round trip should tell the operator everything wrong with the request.
        var refusal = GccGenerationCoordinator.ValidateRequestedTypes(["whitepaper", "listicle"]);

        Assert.NotNull(refusal);
        Assert.Contains("whitepaper", refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("listicle", refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_refusal_carries_the_prefix_generate_filters_on()
    {
        // GenerateAsync's catch filters on "Refused:" to answer 400 rather than 503. Without the
        // prefix an operator-facing decision is reported as the service being down.
        Assert.StartsWith("Refused:", GccGenerationCoordinator.ValidateRequestedTypes([])!, StringComparison.Ordinal);
    }

    // ---- MergeRetrievedEvidence --------------------------------------------------------------

    private static GccQuoteablePage Page(string url, string title = "T") =>
        new(url, title, [], ["Some retrieved prose about the product."]);

    private static GccCreateDto Create(string? researchJson) =>
        new(
            Id: Guid.NewGuid(),
            ClientId: Guid.NewGuid(),
            OwnerUserId: Guid.NewGuid(),
            StartingContentType: "pillar",
            Topic: "Accounts Payable Automation",
            Notes: null,
            ProjectSiteRunId: null,
            SiteSectionJson: null,
            BriefJson: null,
            ResearchJson: researchJson,
            Status: "draft",
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow);

    private static GccResearchDocument MergedDoc(
        string? existingJson,
        IReadOnlyList<GccQuoteablePage>? partner = null,
        IReadOnlyList<GccQuoteablePage>? competitor = null,
        IReadOnlyList<GccQuoteablePage>? site = null)
    {
        // Positional: Pages, Warnings, Refusal, PartnerPassages, CompetitorPages, SitePages.
        var outcome = new GccGroundingOutcome(
            partner ?? [], [], null, [], competitor ?? [], site ?? []);
        var merged = GccGenerationCoordinator.MergeRetrievedEvidence(Create(existingJson), outcome);
        return GccResearchFetchService.Deserialize(merged.ResearchJson)!;
    }

    [Fact]
    public void An_operator_upload_outranks_retrieval_at_the_same_url()
    {
        // The upload was a deliberate choice about this create; retrieval is automatic.
        var existing = GccResearchFetchService.Serialize(
            new GccResearchDocument(null, [Page("https://p.test/a", "OPERATOR")]));

        var merged = MergedDoc(existing, partner: [Page("https://p.test/a", "RETRIEVED")]);

        Assert.Equal("OPERATOR", Assert.Single(merged.Quoteables).Title);
    }

    [Fact]
    public void Retrieval_appends_rather_than_replaces()
    {
        var existing = GccResearchFetchService.Serialize(
            new GccResearchDocument(null, [Page("https://p.test/a")]));

        Assert.Equal(2, MergedDoc(existing, partner: [Page("https://p.test/b")]).Quoteables.Count);
    }

    [Fact]
    public void Partner_competitor_and_site_lists_never_merge_into_each_other()
    {
        // The prompt blocks that render them say opposite things about attribution: partner
        // evidence is to cite, competitor evidence is to be different from and never quoted.
        var merged = MergedDoc(
            null,
            partner: [Page("https://partner.test/a")],
            competitor: [Page("https://rival.test/a")],
            site: [Page("https://own.test/a")]);

        Assert.Equal("https://partner.test/a", Assert.Single(merged.Quoteables).Url);
        Assert.Equal("https://rival.test/a", Assert.Single(merged.CompetitorQuoteables!).Url);
        Assert.Equal("https://own.test/a", Assert.Single(merged.SiteQuoteables!).Url);
    }

    [Fact]
    public void Nothing_retrieved_leaves_the_create_exactly_as_it_was()
    {
        // The early return. An empty merge must not mint an empty research document over a null
        // column -- the repository treats whitespace as "set to NULL" and a minted empty document
        // would read as "we looked and found nothing" rather than "we never looked".
        var create = Create(null);

        var merged = GccGenerationCoordinator.MergeRetrievedEvidence(
            create, new GccGroundingOutcome([], [], null, [], [], []));

        Assert.Null(merged.ResearchJson);
        Assert.Same(create.Topic, merged.Topic);
    }

    // ---- RecordGroundingWarningsAsync ---------------------------------------------------------

    [Fact]
    public async Task Retrieval_warnings_are_recorded_and_pushed_not_dropped()
    {
        // GccGroundingOutcome.Warnings had no consumer: the resolver collected the Library's warnings
        // and the generate returned without them, so a degraded retrieval read as a clean one.
        var recorded = new List<string>();
        var pushed = new List<(string Type, string Warning)>();

        await GccGenerationCoordinator.RecordGroundingWarningsAsync(
            ["partner run 1: reranker unavailable"],
            recorded,
            (type, warning) =>
            {
                pushed.Add((type, warning));
                return Task.CompletedTask;
            });

        Assert.Equal(["grounding: partner run 1: reranker unavailable"], recorded);
        Assert.Equal([("grounding", "partner run 1: reranker unavailable")], pushed);
    }

    // ---- PartnerEvidenceRefusal -----------------------------------------------------------------

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public void A_partner_with_no_passage_refuses_a_pillar_or_blog_naming_it(string type)
    {
        var refusal = GccGenerationCoordinator.PartnerEvidenceRefusal(type, ["Ramp (ramp.test)"]);

        Assert.StartsWith("Refused:", refusal, StringComparison.Ordinal);
        Assert.Contains("Ramp (ramp.test)", refusal!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("aiTool")]
    public void A_tool_page_type_is_not_refused_its_fan_out_refuses_that_partner(string type) =>
        Assert.Null(GccGenerationCoordinator.PartnerEvidenceRefusal(type, ["Ramp (ramp.test)"]));

    [Fact]
    public void Every_partner_answering_refuses_nothing() =>
        Assert.Null(GccGenerationCoordinator.PartnerEvidenceRefusal("pillar", []));
}
