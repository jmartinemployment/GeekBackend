using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Partner grounding for the live "aiTool" content type, 2026-09-22. GenerateToolPageAsync never
/// consumed GccPartnerExtractionService's real payloads even though the "AI Tools" manual-entry
/// panel was deleted on the belief it already did (commit 7dcaea6). Per Jeff's explicit instruction
/// ("Partner crawl data required when content type Tool/Partner selected"), a create that reaches
/// this method must either ground in real partner extraction or refuse -- no silent ungrounded
/// fallback once a create is in play.
/// </summary>
public class GccGenerateServiceToolPageGroundingTests
{
    // Three body sections, because a single-section body leaves document.Sections empty once the
    // lede is separated and GenerateSectionImagePromptsAsync correctly refuses that.
    //
    // The headings are written ones, not the outline's slot names. Tool's sections used to arrive
    // headed "Overview / Key Capabilities / Implementation Considerations" on every page because
    // the outline was a fixed list of titles; it is a list of obligations now and the writer names
    // each one (Jeff, 2026-09-23: "I really don't want to see Overview again, on any content type.
    // Overview is a type of Lede.").
    // The body carries a block quotation of the partner, verbatim from the Citables span the
    // grounded tests supply and cited to the page it came from -- every grounded tool page does,
    // and GccToolQuoteGuard refuses one that does not (Jeff, 2026-09-26: "I want a blockquote in
    // each tool").
    // The body names the product, because a real tool page does and the generator now refuses one
    // that does not. This fixture said "Body." / "Capabilities." / "Considerations." and would have
    // shipped a page about nothing in particular.
    // The last section closes on the scheduler as a link, because every page does and
    // GccClosingCtaGuard refuses a draft that does not (Jeff, 2026-09-27: "CTA is on every page and
    // should be referenced").
    /// <summary>
    /// The body arrives in batches of two sections, so the fixture answers one batch per call. It
    /// used to be a single three-section string returned for every body call, which built a page
    /// out of three copies of itself.
    /// </summary>
    private static readonly string[] ToolBodyBatches =
    [
        """{"sections":[{"tag":"h2","heading":"Where the setup hours actually go","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget removes the manual pass."}]}],"href":null,"children":[]},{"tag":"h2","heading":"What the wizard takes off your desk","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget captures the invoice on arrival."}]}],"href":null,"children":[]}]}""",
        """{"sections":[{"tag":"h2","heading":"Mapping your data before go-live","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget needs the vendor master mapped first."}]},{"type":"quote","runs":[{"text":"reduces setup time by half"}],"cite":"https://partner.test/widget"}],"href":null,"children":[]},{"tag":"h2","heading":"Judging Partner Widget against the alternatives","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget is priced per document."}]}],"href":null,"children":[]}]}""",
        """{"sections":[{"tag":"h2","heading":"Who Partner Widget suits","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget fits a team already on a ledger."}]}],"href":null,"children":[]},{"tag":"h2","heading":"What to do next with Partner Widget","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget rewards a scoped pilot."}]},{"type":"text","runs":[{"text":"Book a free consultation.","href":"#consultationAppointment2xl"}]}],"href":null,"children":[]}]}""",
    ];
    private const string ToolImagePromptsJson =
        // One per H1 plus one per H2, with headroom for the optional FAQ section -- a short list is
        // refused now rather than silently leaving sections without a prompt.
        """{"prompts":[{"section":"Hero","prompt":"hero image prompt"},{"section":"Section 1","prompt":"capabilities image prompt"},{"section":"Section 2","prompt":"considerations image prompt"},{"section":"Section 3","prompt":"third image prompt"},{"section":"Section 4","prompt":"fourth image prompt"},{"section":"Section 5","prompt":"fifth image prompt"},{"section":"Section 6","prompt":"sixth image prompt"},{"section":"FAQ","prompt":"faq image prompt"}]}""";
    private const string ToolMetadataJson =
        """{"departmentListExcerpt":"x","summary":"x","mainSummary":"x","heroSummary":"x","homeSummary":"x","blogSummary":"x","toolPageExcerpt":"x","advertisingSummary":"x","metaDescription":"x"}""";
    private const string ToolFaqJson =
        """{"tag":"h2","heading":"Frequently Asked Questions","paragraphs":[],"href":null,"children":[{"tag":"h3","heading":"Is it secure?","paragraphs":[{"type":"text","runs":[{"text":"Yes, SOC 2 Type II certified."}]}],"href":null,"children":[]}]}""";

    /// <param name="includeFaq">
    /// True when the scripted extraction carries FaqBank entries, so GenerateToolPageAsync makes
    /// an extra call between the body and image-prompts calls.
    /// </param>
    // LedeJsonContract, what BuildArticleLedePrompt asks for -- Tool gets the same purpose-written
    // hook every other long-form type gets instead of promoting its first body section into the
    // lede slot.
    private const string ToolLedeJson =
        """{"ledeType":"directAddress","heading":"Reclaiming The Hours You Lose","paragraphs":[{"type":"text","runs":[{"text":"A hook paragraph that opens the page."}]}]}""";

    private sealed class ScriptedProvider(
        bool includeFaq = false, bool quoteByNumber = false, string? operatorFaqJson = null) : IContentGenerationProvider
    {
        private int bodyCalls;

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;
        public List<ChatCompletionRequest> Requests { get; } = [];

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            // Answers what was asked rather than counting calls. The call order used to be encoded
            // as indexes here, so the body being written in batches -- three calls where there was
            // one -- made every later index answer the wrong question, and ten tests failed for a
            // change none of them was about. A fixture that asserts call order is asserting an
            // implementation detail it was never meant to pin.
            var asked = string.Join("\n", request.Messages.Select(m => m.Content));
            // JsonSchemaName is the exact discriminator: "sections" is a body batch and "section" is
            // the single FAQ section. Matching on prompt text alone put the body in the FAQ branch,
            // because the body prompt mentions the FAQ when explaining what its word target excludes.
            // A shortfall retry re-asks for the batch it just wrote; answering it with the next
            // script would hand sections 3-4 to the call that owns 1-2. Same batch, not counted,
            // and no better -- so the service keeps the first draft, as it should.
            var isLengthRetry = asked.Contains("=== SHORTFALL", StringComparison.Ordinal);
            var content = request.JsonSchemaName switch
            {
                // One batch per body call. Returning the whole body each time gave the page three
                // copies of itself, which the image-prompt count then caught.
                "sections" when isLengthRetry => Script(ToolBodyBatches[Math.Min(Math.Max(bodyCalls - 1, 0), ToolBodyBatches.Length - 1)]),
                "sections" => Script(ToolBodyBatches[Math.Min(bodyCalls++, ToolBodyBatches.Length - 1)]),
                // The operator's FAQ questions are the one "section" call that carries the partner
                // evidence; the bank's FAQ call carries Q/Answer/Source pairs instead.
                "section" when operatorFaqJson is not null && Asked(asked, "=== PARTNER EVIDENCE") => operatorFaqJson,
                "section" => includeFaq ? ToolFaqJson : ToolMetadataJson,
                _ => Asked(asked, "ledeType") ? ToolLedeJson
                    : Asked(asked, "image-generation prompts") ? ToolImagePromptsJson
                    : ToolMetadataJson,
            };
            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
        }

        private static bool Asked(string prompt, string marker) =>
            prompt.Contains(marker, StringComparison.OrdinalIgnoreCase);

        /// <summary>The scripted batch, with its quotation answered the way the prompt now asks when
        /// the test wants that: by number, with nothing typed into it.</summary>
        private string Script(string batch) =>
            quoteByNumber
                ? batch.Replace(
                    """{"type":"quote","runs":[{"text":"reduces setup time by half"}],"cite":"https://partner.test/widget"}""",
                    """{"type":"quote","candidate":1,"runs":[],"cite":null}""",
                    StringComparison.Ordinal)
                : batch;
    }

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

    private static GccCreateDto Create(string? researchJson) => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
        StartingContentType: "aiTool", Topic: "Partner Widget", Notes: "Operator-supplied brief text",
        ProjectSiteRunId: null, SiteSectionJson: null, BriefJson: null, ResearchJson: researchJson,
        Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

    private static GccGenerateService Build(
        IContentGenerationProvider provider,
        GeekAPI.Services.ContentCreator.Partner.GccPartnerExtractionService partnerExtraction,
        // The project the create belongs to, when a test needs its partner URLs (the per-tool
        // framing and FAQ questions key off the partner host).
        GccProjectDto? project = null) => new(
        new ContentPromptBuilder(),
        TestContentTypePrompts.Registry(),
        new FakeProviderFactory(provider),
        new SoftwareApplicationSchemaBuilder(),
        new BlogPostingSchemaBuilder(),
        new ArticleSchemaBuilder(new SoftwareApplicationSchemaBuilder()),
        Options.Create(new CompanyProfileOptions()),
        NullLogger<GccGenerateService>.Instance,
        GccCompetitorAnalysisResolverTests.Build(
            new GccCompetitorAnalysisResolverTests.FakeProjects(null),
            new GccCompetitorAnalysisResolverTests.FakePages(),
            new GccCompetitorAnalysisResolverTests.FakeRag()),
        partnerExtraction,
        new GccCompetitorAnalysisResolverTests.FakeProjects(project),
        new GccPublisherProfileResolver(
            new GccCompetitorAnalysisResolverTests.FakeProjects(null),
            new GccCompetitorAnalysisResolverTests.FakePages(),
            NullLogger<GccPublisherProfileResolver>.Instance),
        new GccKnownToolsResolver(
            new GccCompetitorAnalysisResolverTests.FakePages(),
            NullLogger<GccKnownToolsResolver>.Instance),
        new GccToolPageFanOutFixture.FakeExtractionBank());

    private static string ResearchJsonWithOnePartnerPage() =>
        GccResearchFetchService.Serialize(new GccResearchDocument(
            SerpIndex: null,
            Quoteables:
            [
                new GccQuoteablePage(
                    Url: "https://partner.test/widget",
                    Title: "Partner Widget",
                    Headings: [new HeadingDto(2, "Pricing")],
                    // The quote the fixture drafts has to be ON this page: the guard's candidates
                    // come from the retrieved pages and nowhere else, so a page that does not carry
                    // the sentence cannot support a quotation of it.
                    Paragraphs:
                    [
                        "Partner Widget starts at $19 per month, billed monthly.",
                        "Partner Widget reduces setup time by half, and the vendor master maps itself.",
                    ],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
            ]));

    /// <summary>
    /// The partner page's typed blocks — where the quotation's candidate spans are cut from.
    ///
    /// <para>
    /// The same prose as the retrieved page above, because it is the same page: the retrieved copy is
    /// the prompt projection and this is the crawl page's blocks. The production path resolves both
    /// and hands the blocks down, so a fixture that supplied only the first would leave the writer
    /// with no span to quote and the page refused for not carrying a quotation.
    /// </para>
    /// </summary>
    private static IReadOnlyList<GccGroundedPassage> PartnerPassages() =>
    [
        new GccGroundedPassage("https://partner.test/widget", "Partner Widget",
        [
            new TextParagraph([new Run("Partner Widget starts at $19 per month, billed monthly.")]),
            new TextParagraph([new Run(
                "Partner Widget reduces setup time by half, and the vendor master maps itself.")]),
        ]),
    ];

    private const string RivalText = "We run accounts payable projects end to end.";
    private const string OwnSiteText = "We published our AP automation guide last quarter.";
    private const string RivalUrl = "https://rival.test/services";

    /// <summary>The same partner page, plus the two lists retrieval now also fills.</summary>
    private static string ResearchJsonWithAllThreeCrawlTypes() =>
        GccResearchFetchService.Serialize(new GccResearchDocument(
            SerpIndex: null,
            Quoteables:
            [
                new GccQuoteablePage(
                    "https://partner.test/widget", "Partner Widget",
                    [new HeadingDto(2, "Pricing")],
                    [
                        "Partner Widget starts at $19 per month, billed monthly.",
                        "Partner Widget reduces setup time by half, and the vendor master maps itself.",
                    ],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
            ],
            CompetitorQuoteables:
            [
                new GccQuoteablePage(
                    RivalUrl, "Rival services", [new HeadingDto(2, "What we do")], [RivalText],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
            ],
            SiteQuoteables:
            [
                new GccQuoteablePage(
                    "https://acme.test/ap-guide", "Our AP guide",
                    [new HeadingDto(2, "How AP automation works")], [OwnSiteText],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
            ]));

    [Fact]
    public async Task CompetitorAndOwnSiteEvidenceReachTheToolBodyPrompt()
    {
        // The tool page had no competitor evidence at all, while one of its six sections is "how a
        // buyer should judge this product -- fit, pricing, and the adjacent approaches they are
        // also weighing". Asserted on the rendered prompt, because a populated list is not proof
        // the writer was shown anything.
        var provider = new ScriptedProvider();
        var extraction = GccPartnerExtractionFakes.EmptyPageExtraction with
        {
            Citables = [new GeekAPI.Services.ContentCreator.Partner.PartnerCitableItem(
                "Partner Widget reduces setup time by half.", "reduces setup time by half")],
            FeatureInventory = [new GeekAPI.Services.ContentCreator.Partner.PartnerFeatureItem(
                "Automated setup wizard", "Onboarding", null, "automated setup wizard")],
            Integrations = [new GeekAPI.Services.ContentCreator.Partner.PartnerIntegrationItem(
                "Slack", "Notifications", "API", "Slack integration")],
        };
        var service = Build(
            provider, GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction));

        await service.GenerateToolPageAsync(
            "Partner Widget", "brief", "context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None,
            create: Create(ResearchJsonWithAllThreeCrawlTypes()), passages: PartnerPassages());

        var bodyPrompts = provider.Requests
            .Where(r => r.JsonSchemaName == "sections")
            .Select(r => string.Join("\n", r.Messages.Select(m => m.Content)))
            .ToList();

        Assert.NotEmpty(bodyPrompts);
        Assert.All(bodyPrompts, p => Assert.Contains(RivalText, p, StringComparison.Ordinal));
        Assert.All(bodyPrompts, p => Assert.Contains(OwnSiteText, p, StringComparison.Ordinal));
        // A URL in the prompt is a URL that can end up on the page.
        Assert.All(bodyPrompts, p => Assert.DoesNotContain(RivalUrl, p, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithNoCreateContextTheLegacyUngroundedPathStillWorks()
    {
        // GenerateToolAsync (the legacy alias) and any other caller that never passes `create`
        // must keep working exactly as before -- this method's new fail-closed gate only applies
        // once a create is actually in play.
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider));
        var service = Build(provider, partner);

        var result = await service.GenerateToolPageAsync(
            "Partner Widget", "A generic brief", "Some context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.Equal("Partner Widget", result.Name);
    }

    [Fact]
    public async Task ACreateWithNoPartnerResearchAtAllRefusesRatherThanGeneratingUngrounded()
    {
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider));
        var service = Build(provider, partner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateToolPageAsync(
                "Partner Widget", "brief", "context", "marketing", null,
                ContentGeneratorProvider.OpenAi, CancellationToken.None,
                create: Create(researchJson: null)));

        Assert.Contains("Partner grounding required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartnerPagesThatExtractToNothingUsableStillRefuses()
    {
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.Scripted(
            new FakeProviderFactory(provider), GccPartnerExtractionFakes.EmptyPageExtraction);
        var service = Build(provider, partner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateToolPageAsync(
                "Partner Widget", "brief", "context", "marketing", null,
                ContentGeneratorProvider.OpenAi, CancellationToken.None,
                create: Create(ResearchJsonWithOnePartnerPage())));

        Assert.Contains("Partner grounding required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThinExtractionBelowTheCategoryThresholdStillRefusesNotJustTotallyEmptyOnes()
    {
        // A single populated field used to pass HasAnyPartnerData -- a create with one lone ICP
        // entry and nothing else wrote 3,500-5,000 words with zero grounding for five of the six
        // sections. HasSufficientPartnerData (2026-09-22) requires real breadth, not just presence.
        var provider = new ScriptedProvider();
        var extraction = GccPartnerExtractionFakes.EmptyPageExtraction with
        {
            Icp = [new GeekAPI.Services.ContentCreator.Partner.PartnerIcpItem(
                ["SMB"], null, "11-50", ["Software"], ["IT Director"], "SMB software companies")],
        };
        var partner = GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction);
        var service = Build(provider, partner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateToolPageAsync(
                "Partner Widget", "brief", "context", "marketing", null,
                ContentGeneratorProvider.OpenAi, CancellationToken.None,
                create: Create(ResearchJsonWithOnePartnerPage())));

        // "Reported failure" -- the message names what was and wasn't found, not just that it failed.
        Assert.Contains("Partner grounding required", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1 of 20 payload categories populated", ex.Message, StringComparison.Ordinal);
        Assert.Contains("core capability signal", ex.Message, StringComparison.Ordinal);
        Assert.Contains("missing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_writer_is_shown_the_same_spans_the_quote_guard_will_check()
    {
        // One list, read by both. The live failure when they differed: the writer's instruction
        // pointed at the extraction JSON while the guard checked the draft against spans cut from the
        // retrieved pages, so a page that correctly wrote no quotation was refused for not using
        // spans it had never been shown -- "28 quotable partner span(s) were supplied and none was
        // used". The prompt builder renders the block; this is the wiring that fills it, and it is
        // the half that was wrong.
        var provider = new ScriptedProvider();
        var extraction = GccPartnerExtractionFakes.EmptyPageExtraction with
        {
            Citables = [new GeekAPI.Services.ContentCreator.Partner.PartnerCitableItem(
                "Partner Widget reduces setup time by half.", "reduces setup time by half")],
            FeatureInventory = [new GeekAPI.Services.ContentCreator.Partner.PartnerFeatureItem(
                "Automated setup wizard", "Onboarding", null, "automated setup wizard")],
            Integrations = [new GeekAPI.Services.ContentCreator.Partner.PartnerIntegrationItem(
                "Slack", "Notifications", "API", "Slack integration")],
        };
        var partner = GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction);
        var service = Build(provider, partner);

        await service.GenerateToolPageAsync(
            "Partner Widget", "brief", "context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None,
            create: Create(ResearchJsonWithOnePartnerPage()), passages: PartnerPassages());

        // Either role: the spans ride on the tool body's system message, beside the instruction
        // that governs them, and this test is about the list reaching the model at all.
        var prompt = provider.Requests
            .SelectMany(r => r.Messages.Select(m => m.Content))
            .FirstOrDefault(c => c.Contains("QUOTABLE SPANS", StringComparison.Ordinal));
        Assert.NotNull(prompt);
        // The span the passage carries, offered by number with its page as the cite -- so the model
        // answers with the number and never retypes the sentence.
        Assert.Contains("1. \"", prompt, StringComparison.Ordinal);
        Assert.Contains("the vendor master maps itself", prompt, StringComparison.Ordinal);
        Assert.Contains("[cite: https://partner.test/widget]", prompt, StringComparison.Ordinal);

        // The spans and the passages they were cut from reach the same body call. The body used to
        // be handed the competitor/own-site text in place of the research block, so the writer saw
        // numbered spans with none of the retrieved prose around them.
        var bodyRequests = provider.Requests.Where(r => r.JsonSchemaName == "sections").ToList();
        Assert.NotEmpty(bodyRequests);
        Assert.All(bodyRequests, r =>
            Assert.Contains(
                "=== QUOTEABLE RESEARCH",
                string.Join("\n", r.Messages.Select(m => m.Content)),
                StringComparison.Ordinal));
    }

    private static GeekAPI.Services.ContentCreator.Partner.PartnerPageExtraction GroundableExtraction() =>
        GccPartnerExtractionFakes.EmptyPageExtraction with
        {
            Citables = [new GeekAPI.Services.ContentCreator.Partner.PartnerCitableItem(
                "Partner Widget reduces setup time by half.", "reduces setup time by half")],
            FeatureInventory = [new GeekAPI.Services.ContentCreator.Partner.PartnerFeatureItem(
                "Automated setup wizard", "Onboarding", null, "automated setup wizard")],
            Integrations = [new GeekAPI.Services.ContentCreator.Partner.PartnerIntegrationItem(
                "Slack", "Notifications", "API", "Slack integration")],
        };

    [Fact]
    public async Task A_quotation_chosen_by_number_is_resolved_to_the_candidate_span_and_its_page()
    {
        // The Stampli refusal, 2026-10-03: the writer retyped a sentence and the guard, rightly,
        // matched it to nothing. The number is the answer now, and the words are the system's own.
        var provider = new ScriptedProvider(quoteByNumber: true);
        var partner = GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), GroundableExtraction());
        var service = Build(provider, partner);

        var result = await service.GenerateToolPageAsync(
            "Partner Widget", "brief", "context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None,
            create: Create(ResearchJsonWithOnePartnerPage()), passages: PartnerPassages());

        var quotes = result.Document.Sections
            .SelectMany(s => s.Paragraphs)
            .OfType<QuoteParagraph>()
            .ToList();
        var quote = Assert.Single(quotes);
        Assert.Null(quote.Candidate);
        // Candidate 1 is whatever GccQuoteCandidates cut first from these passages -- the page's own
        // characters, read back by number, which is the whole point.
        var first = GccQuoteCandidates.From(PartnerPassages())[0];
        Assert.Equal(first.PageUrl, quote.Cite);
        Assert.Equal(first.Text, Assert.Single(quote.Runs).Text);
    }

    [Fact]
    public async Task The_tool_body_is_told_the_creates_keyword_not_the_product_name()
    {
        // The page scored 0.00% density on its own keyword because the writer had never been told
        // it: the context was built from the product name, so every SEO instruction asked for
        // "Partner Widget" in the lede and a heading, and the scorer asked for the keyword.
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), GroundableExtraction());
        var service = Build(provider, partner);
        var create = Create(ResearchJsonWithOnePartnerPage()) with
        {
            Topic = "Accounts Payable: Automated Data Entry & Processing",
        };

        await service.GenerateToolPageAsync(
            "Partner Widget", "brief", "context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None,
            create: create, passages: PartnerPassages());

        var body = provider.Requests
            .SelectMany(r => r.Messages.Select(m => m.Content))
            .First(c => c.Contains("WHAT THIS PAGE IS", StringComparison.Ordinal));
        Assert.Contains("facing \"Automated Data Entry & Processing\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("facing \"Partner Widget\"", body, StringComparison.Ordinal);
        // The product is still the subject, named as itself.
        Assert.Contains("Partner Widget is a PARTNER", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealExtractionDataGroundsTheBodyPromptAndTheJsonLd()
    {
        var provider = new ScriptedProvider();
        // Three categories populated, not just one -- HasSufficientPartnerData (2026-09-22) refuses
        // a create with only a single thin field, the same standard Pillar/Blog get from
        // GccHeadingProvenanceGuard.
        var extraction = GccPartnerExtractionFakes.EmptyPageExtraction with
        {
            Citables = [new GeekAPI.Services.ContentCreator.Partner.PartnerCitableItem(
                "Partner Widget reduces setup time by half.", "reduces setup time by half")],
            FeatureInventory = [new GeekAPI.Services.ContentCreator.Partner.PartnerFeatureItem(
                "Automated setup wizard", "Onboarding", null, "automated setup wizard")],
            Integrations = [new GeekAPI.Services.ContentCreator.Partner.PartnerIntegrationItem(
                "Slack", "Notifications", "API", "Slack integration")],
        };
        var partner = GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction);
        var service = Build(provider, partner);

        var result = await service.GenerateToolPageAsync(
            "Partner Widget", "brief", "context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None,
            create: Create(ResearchJsonWithOnePartnerPage()), passages: PartnerPassages());

        // The body prompt's own request (call 1, after the lede) actually carried the extraction,
        // not just that the call succeeded.
        var bodyRequest = provider.Requests[1];
        var userMessage = bodyRequest.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("PARTNER DATA", userMessage, StringComparison.Ordinal);
        Assert.Contains("reduces setup time by half", userMessage, StringComparison.Ordinal);

        // Carrying the data is not enough -- the page is a paraphrase of it, so the instruction
        // that makes it the substance has to be in the same prompt. Without this the model was
        // handed real partner data and still wrote a category page that named no partner.
        Assert.Contains("paraphrase of the partner data", userMessage, StringComparison.Ordinal);

        // Real partner JSON-LD, not the thin generic builder -- proven by an identity field the
        // generic SoftwareApplicationSchemaBuilder has no source for (the page's own title).
        Assert.Contains("\"name\":\"Partner Widget\"", result.JsonLdSchema);
    }

    [Fact]
    public async Task The_tool_page_ends_on_the_pages_closing_before_the_FAQ_and_the_writer_is_never_sent_the_questions()
    {
        var provider = new ScriptedProvider(includeFaq: true);
        var extraction = GccPartnerExtractionFakes.EmptyPageExtraction with
        {
            Citables = [new GeekAPI.Services.ContentCreator.Partner.PartnerCitableItem(
                "Partner Widget reduces setup time by half.", "reduces setup time by half")],
            FeatureInventory = [new GeekAPI.Services.ContentCreator.Partner.PartnerFeatureItem(
                "Automated setup wizard", "Onboarding", null, "automated setup wizard")],
            Faqs = [new GeekAPI.Services.ContentCreator.Partner.PartnerFaqItem(
                "Is Partner Widget secure?", "Yes, SOC 2 Type II certified.", "SOC 2 Type II certified")],
        };
        var partner = GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction);
        var service = Build(provider, partner);
        var create = Create(ResearchJsonWithOnePartnerPage()) with
        {
            BriefJson = """{"ctaType":"book_now","nicheFraming":{"diagnosisQuestions":"What business objective should this automation serve?\nHow clean is the data the approvals draw on today?"}}""",
        };

        var result = await service.GenerateToolPageAsync(
            "Partner Widget", "brief", "context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None,
            create: create, passages: PartnerPassages());

        var sections = result.Document.Sections;
        Assert.Equal("Frequently Asked Questions", sections[^1].Heading);
        var paragraphs = sections[^2].Paragraphs;
        var line = Assert.IsType<TextParagraph>(paragraphs[^2]);
        Assert.Equal(
            ["Answer these questions when ", "booking your free consultation", "."],
            line.Runs.Select(r => r.Text));
        Assert.Equal("#consultationAppointment2xl", line.Runs[1].Href);
        var list = Assert.IsType<ListParagraph>(paragraphs[^1]);
        Assert.Equal(
            ["What business objective should this automation serve?", "How clean is the data the approvals draw on today?"],
            list.Items.Select(item => Assert.Single(item).Text));

        // What writes the page's body: its batches. The metadata and image-prompt calls that follow read the
        // finished page, closing included, to summarise it; they write none of it.
        var writers = provider.Requests.Where(r => r.JsonSchemaName == "sections");
        var sent = string.Join("\n", writers.SelectMany(r => r.Messages.Select(m => m.Content)));
        Assert.Contains("END OF THE PAGE", sent, StringComparison.Ordinal);
        string[] never =
        [
            "What business objective should this automation serve?", "How clean is the data", "book_now", "CLOSING:",
        ];
        var found = never.Where(needle => sent.Contains(needle, StringComparison.Ordinal)).ToList();
        Assert.True(found.Count == 0, "The writer was sent: " + string.Join(" | ", found));
    }

    [Fact]
    public async Task A_tool_pages_research_reaches_every_call_once_and_its_title_carries_the_keyword()
    {
        // The Ramp page of 2026-10-07 printed the whole research block three times per body call (the
        // publisher block, "Tool summary:" and the evidence block): 54% of the call, for nothing any check
        // reads. And its title was only the product.
        var provider = new ScriptedProvider(includeFaq: true);
        var extraction = GccPartnerExtractionFakes.EmptyPageExtraction with
        {
            Citables = [new GeekAPI.Services.ContentCreator.Partner.PartnerCitableItem(
                "Partner Widget reduces setup time by half.", "reduces setup time by half")],
            FeatureInventory = [new GeekAPI.Services.ContentCreator.Partner.PartnerFeatureItem(
                "Automated setup wizard", "Onboarding", null, "automated setup wizard")],
            Faqs = [new GeekAPI.Services.ContentCreator.Partner.PartnerFaqItem(
                "Is Partner Widget secure?", "Yes, SOC 2 Type II certified.", "SOC 2 Type II certified")],
        };
        var partner = GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction);
        var service = Build(provider, partner);
        var create = DispatchCreate("tool") with
        {
            Topic = "Accounts Payable: Automated Approval Workflows",
            ResearchJson = ResearchJsonWithOnePartnerPage(),
        };

        var envelope = await service.GenerateStartingContentAsync(
            create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None,
            passages: PartnerPassages(), toolName: "Partner Widget");

        const string header = "=== QUOTEABLE RESEARCH";
        var perCall = provider.Requests
            .Select(r => string.Join("\n", r.Messages.Select(m => m.Content)))
            .Select(prompt => prompt.Split(header).Length - 1)
            .ToList();
        Assert.All(perCall, count => Assert.True(count <= 1, $"a call carried the research {count} times"));
        // The body's calls carry it, once: it was not dropped to get there.
        var bodyCalls = provider.Requests.Where(r => r.JsonSchemaName == "sections").ToList();
        Assert.NotEmpty(bodyCalls);
        Assert.All(bodyCalls, call =>
            Assert.Equal(1, string.Join("\n", call.Messages.Select(m => m.Content)).Split(header).Length - 1));
        Assert.All(bodyCalls, call =>
        {
            var summary = string.Join("\n", call.Messages.Select(m => m.Content))
                .Split('\n').FirstOrDefault(l => l.StartsWith("Tool summary:", StringComparison.Ordinal));
            Assert.DoesNotContain("QUOTEABLE", summary ?? string.Empty, StringComparison.Ordinal);
        });

        using var doc = JsonDocument.Parse(envelope);
        Assert.Equal("Partner Widget: Automated Approval Workflows", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("Partner Widget", doc.RootElement.GetProperty("productName").GetString());
    }

    [Theory]
    [InlineData("Ramp", "Accounts Payable: Automated Approval Workflows", "Ramp: Automated Approval Workflows")]
    [InlineData("Bill", "Accounts Payable: Automated Data Entry & Processing", "Bill: Automated Data Entry & Processing")]
    [InlineData("Ramp", "Automated Approval Workflows", "Ramp: Automated Approval Workflows")]
    [InlineData("Ramp", "Pricing:", "Ramp: Pricing:")]
    [InlineData("Ramp", "", "Ramp")]
    [InlineData("Ramp", null, "Ramp")]
    public void A_tool_pages_title_is_the_product_then_the_projects_keyword(string product, string? topic, string expected)
    {
        Assert.Equal(expected, GccGenerateService.ToolPageTitle(product, topic));
    }

    [Fact]
    public async Task GroundedFaqBankDataProducesAnAdditionalFaqSectionBeyondTheWordCountTarget()
    {
        var provider = new ScriptedProvider(includeFaq: true);
        // Three categories populated, not just one -- HasSufficientPartnerData (2026-09-22) refuses
        // a create with only a single thin field, the same standard Pillar/Blog get from
        // GccHeadingProvenanceGuard.
        var extraction = GccPartnerExtractionFakes.EmptyPageExtraction with
        {
            Citables = [new GeekAPI.Services.ContentCreator.Partner.PartnerCitableItem(
                "Partner Widget reduces setup time by half.", "reduces setup time by half")],
            FeatureInventory = [new GeekAPI.Services.ContentCreator.Partner.PartnerFeatureItem(
                "Automated setup wizard", "Onboarding", null, "automated setup wizard")],
            // PartnerFaqItem is the raw per-page shape ExtractFromPagesAsync aggregates into the
            // final GccPartnerFaqAsset (page.Url + built provenance) -- no quote-in-text gate at
            // this stage, just non-empty Question/Answer.
            Faqs = [new GeekAPI.Services.ContentCreator.Partner.PartnerFaqItem(
                "Is Partner Widget secure?", "Yes, SOC 2 Type II certified.", "SOC 2 Type II certified")],
        };
        var partner = GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction);
        var service = Build(provider, partner);

        var result = await service.GenerateToolPageAsync(
            "Partner Widget", "brief", "context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None,
            create: Create(ResearchJsonWithOnePartnerPage()), passages: PartnerPassages());

        // FAQ is a real, distinct call of its own -- carrying the extracted answer for the model to
        // paraphrase, not a question it must answer from scratch -- and the resulting section
        // survives into the persisted document, beyond the body's own outline.
        var faqRequest = provider.Requests[2];
        var faqUserMessage = faqRequest.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("SOC 2 Type II certified", faqUserMessage, StringComparison.Ordinal);
        Assert.Contains("Frequently Asked Questions", result.Document.Sections.Select(s => s.Heading));
    }

    // ---- The operator's FAQ questions for a tool ----------------------------------------------
    //
    // FAQ fields for the tool pages (Jeff, 2026-10-08). The operator's questions for a tool live in
    // its own perTool entry and are answered from the partner's own pages alone; a question that is
    // not answered is left out of the section and reported as a gap, never answered from general
    // knowledge.
    //
    // 2026-10-10: each question is answered from what a search of the partner's crawl found for that
    // question (the research's FaqEvidence). Until then no question was searched for -- they were
    // answered from the passages the page's other searches had brought back, and one none of those
    // was about was reported as "no page of the partner's answers" when nothing had looked. So the
    // report says what was checked, and each of its four forms has a test below.

    private const string FaqHost = "partner.test";
    private const string SyncQuestion = "Does Partner Widget sync with QuickBooks Online?";
    private const string FlyQuestion = "Does Partner Widget fly?";
    private const string FaqBrief =
        """{"nicheFraming":{"perTool":{"partner.test":{"faqQuestions":"Does Partner Widget sync with QuickBooks Online?\nDoes Partner Widget fly?"}}}}""";
    private const string AnsweredOnlySync =
        """{"tag":"h2","heading":"Frequently Asked Questions","paragraphs":[],"href":null,"children":[{"tag":"h3","heading":"Does Partner Widget sync with QuickBooks Online?","paragraphs":[{"type":"text","runs":[{"text":"Yes: every payment syncs to QuickBooks Online."}]}],"href":null,"children":[]}]}""";

    /// <summary>A passage only a search for the sync question finds: it is on no page in the shared pool.</summary>
    private static GccQuoteablePage SyncPassage() => new(
        "https://partner.test/integrations", "Integrations", [],
        ["Every Partner Widget payment syncs to QuickBooks Online within the hour."]);

    private static GccQuoteablePage FlyPassage() => new(
        "https://partner.test/about", "About", [], ["Partner Widget is cloud software.", "It runs in a browser."]);

    /// <summary>The one partner page every tool test grounds on, with what each FAQ search found beside it.</summary>
    private static string ResearchWithFaqSearches(params GccFaqEvidence[] searches) =>
        GccResearchFetchService.Serialize(
            GccResearchFetchService.Deserialize(ResearchJsonWithOnePartnerPage())! with { FaqEvidence = searches });

    private async Task<(GccGenerateService.ToolPageResult Result, ScriptedProvider Provider)> WriteToolPageWithFaqAsync(
        string researchJson, string? operatorFaqJson)
    {
        var provider = new ScriptedProvider(operatorFaqJson: operatorFaqJson);
        var extraction = GccPartnerExtractionFakes.EmptyPageExtraction with
        {
            Citables = [new GeekAPI.Services.ContentCreator.Partner.PartnerCitableItem(
                "Partner Widget reduces setup time by half.", "reduces setup time by half")],
            FeatureInventory = [new GeekAPI.Services.ContentCreator.Partner.PartnerFeatureItem(
                "Automated setup wizard", "Onboarding", null, "automated setup wizard")],
            Integrations = [new GeekAPI.Services.ContentCreator.Partner.PartnerIntegrationItem(
                "QuickBooks Online", "accounting", null, null)],
        };
        var partner = GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction);
        var projectId = Guid.NewGuid();
        var partnerUrls = new[] { "https://partner.test/" };
        var project = new GccProjectDto(
            projectId, Guid.NewGuid(), "Acme", null, null, "active", null, null, null,
            partnerUrls, [], DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, null, null,
            DateTime.UtcNow, DateTime.UtcNow);
        var service = Build(provider, partner, project);
        // The product name the fan-out would use for this host, so the per-tool lookup resolves.
        var toolName = GccRequiredToolMentions.AnchorLookup(FaqBrief, partnerUrls).Values.Single();

        var result = await service.GenerateToolPageAsync(
            toolName, "brief", "context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None,
            create: Create(researchJson) with { ProjectId = projectId, BriefJson = FaqBrief },
            passages: PartnerPassages());
        return (result, provider);
    }

    private static IReadOnlyList<ChatCompletionRequest> FaqRequests(ScriptedProvider provider) =>
        [.. provider.Requests.Where(r => r.Messages.Any(m => m.Content.Contains("=== PARTNER EVIDENCE", StringComparison.Ordinal)))];

    private static IReadOnlyList<string> FaqWarnings(GccGenerateService.ToolPageResult result) =>
        [.. (result.Warnings ?? []).Where(w => w.StartsWith("FAQ:", StringComparison.Ordinal))];

    [Fact]
    public async Task AQuestionIsAnsweredFromWhatTheSearchForItFoundAndNotFromTheSharedPassages()
    {
        var (result, provider) = await WriteToolPageWithFaqAsync(
            ResearchWithFaqSearches(
                new GccFaqEvidence(FaqHost, SyncQuestion, [SyncPassage()]),
                new GccFaqEvidence(FaqHost, FlyQuestion, [FlyPassage()])),
            AnsweredOnlySync);

        var faqUser = Assert.Single(FaqRequests(provider)).Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("- Q1: " + SyncQuestion, faqUser, StringComparison.Ordinal);
        Assert.Contains("- Q2: " + FlyQuestion, faqUser, StringComparison.Ordinal);
        // The passage the search for the question found reaches its call, under its label, though no
        // page in the shared pool carries it. This is the defect, pinned: before, it could not.
        var evidence = faqUser[faqUser.IndexOf("=== PARTNER EVIDENCE", StringComparison.Ordinal)..];
        var forSync = evidence[evidence.IndexOf("Found for Q1:", StringComparison.Ordinal)..evidence.IndexOf("Found for Q2:", StringComparison.Ordinal)];
        Assert.Contains("https://partner.test/integrations", forSync, StringComparison.Ordinal);
        Assert.Contains("syncs to QuickBooks Online within the hour", forSync, StringComparison.Ordinal);
        // And the passages every other call is written from are not the FAQ's evidence.
        Assert.DoesNotContain("starts at $19 per month", evidence, StringComparison.Ordinal);

        var faq = Assert.Single(result.Document.Sections, s => s.Heading == "Frequently Asked Questions");
        Assert.Equal([SyncQuestion], faq.Children.Select(c => c.Heading));
    }

    [Fact]
    public async Task AQuestionShownPassagesAndNotAnsweredIsReportedAsThat()
    {
        var (result, _) = await WriteToolPageWithFaqAsync(
            ResearchWithFaqSearches(
                new GccFaqEvidence(FaqHost, SyncQuestion, [SyncPassage()]),
                new GccFaqEvidence(FaqHost, FlyQuestion, [FlyPassage()])),
            AnsweredOnlySync);

        Assert.Equal(
            ["FAQ: the writer was shown 2 passage(s) from partner.test for \"Does Partner Widget fly?\" and did not answer it; it was left out."],
            FaqWarnings(result));
    }

    [Fact]
    public async Task AQuestionWhoseSearchFoundNothingIsNotSentAndIsReportedAsThat()
    {
        var (result, provider) = await WriteToolPageWithFaqAsync(
            ResearchWithFaqSearches(
                new GccFaqEvidence(FaqHost, SyncQuestion, [SyncPassage()]),
                new GccFaqEvidence(FaqHost, FlyQuestion, [])),
            AnsweredOnlySync);

        var faqUser = Assert.Single(FaqRequests(provider)).Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("- Q1: " + SyncQuestion, faqUser, StringComparison.Ordinal);
        Assert.DoesNotContain(FlyQuestion, faqUser, StringComparison.Ordinal);
        Assert.Equal(
            ["FAQ: a search of partner.test's crawl found nothing for \"Does Partner Widget fly?\"; it was left out."],
            FaqWarnings(result));
        var faq = Assert.Single(result.Document.Sections, s => s.Heading == "Frequently Asked Questions");
        Assert.Equal([SyncQuestion], faq.Children.Select(c => c.Heading));
    }

    [Fact]
    public async Task AQuestionThatWasNeverSearchedForIsReportedAsThatAndNeverAsFoundNothing()
    {
        // Research that did not come through the search: the one partner page, and no FAQ searches.
        var (result, provider) = await WriteToolPageWithFaqAsync(ResearchJsonWithOnePartnerPage(), AnsweredOnlySync);

        // No model call: there is nothing to answer either question from.
        Assert.Empty(FaqRequests(provider));
        Assert.Equal(
            [
                "FAQ: partner.test's crawl was not searched for \"Does Partner Widget sync with QuickBooks Online?\"; it was left out.",
                "FAQ: partner.test's crawl was not searched for \"Does Partner Widget fly?\"; it was left out.",
            ],
            FaqWarnings(result));
        Assert.DoesNotContain(result.Document.Sections, s => s.Heading == "Frequently Asked Questions");
    }

    [Fact]
    public async Task AQuestionSearchedForInAnotherPartnersCrawlWasNotSearchedForInThisOne()
    {
        var (result, provider) = await WriteToolPageWithFaqAsync(
            ResearchWithFaqSearches(
                new GccFaqEvidence("rival.test", SyncQuestion, [SyncPassage()]),
                new GccFaqEvidence("rival.test", FlyQuestion, [FlyPassage()])),
            AnsweredOnlySync);

        Assert.Empty(FaqRequests(provider));
        Assert.All(FaqWarnings(result), w => Assert.Contains("partner.test's crawl was not searched for", w, StringComparison.Ordinal));
        Assert.Equal(2, FaqWarnings(result).Count);
    }

    [Fact]
    public async Task AnAnswerUnderAHeadingThatIsNoneOfTheQuestionsIsDroppedAndSaidToBe()
    {
        const string answeredUnderOtherWords =
            """{"tag":"h2","heading":"Frequently Asked Questions","paragraphs":[],"href":null,"children":[{"tag":"h3","heading":"Does Partner Widget sync with QuickBooks Online?","paragraphs":[{"type":"text","runs":[{"text":"Yes: every payment syncs to QuickBooks Online."}]}],"href":null,"children":[]},{"tag":"h3","heading":"Is Partner Widget installed on a server?","paragraphs":[{"type":"text","runs":[{"text":"No. It runs in a browser."}]}],"href":null,"children":[]}]}""";

        var (result, _) = await WriteToolPageWithFaqAsync(
            ResearchWithFaqSearches(
                new GccFaqEvidence(FaqHost, SyncQuestion, [SyncPassage()]),
                new GccFaqEvidence(FaqHost, FlyQuestion, [FlyPassage()])),
            answeredUnderOtherWords);

        var faq = Assert.Single(result.Document.Sections, s => s.Heading == "Frequently Asked Questions");
        Assert.Equal([SyncQuestion], faq.Children.Select(c => c.Heading));
        Assert.Equal(
            [
                "FAQ: the writer was shown 2 passage(s) from partner.test for \"Does Partner Widget fly?\" and did not answer it; it was left out.",
                "FAQ: the writer answered under \"Is Partner Widget installed on a server?\", which is not a question it was sent; the answer was dropped.",
            ],
            FaqWarnings(result));
    }

    /// <summary>
    /// The page's figure check refuses a figure that is in none of the evidence. What a question's
    /// search found is evidence the writer is shown, in the FAQ call and nowhere else, so an answer
    /// that takes a figure from it must not be refused as inventing one.
    /// </summary>
    [Fact]
    public async Task AFigureAnAnswerTakesFromItsOwnPassageIsNotRefusedAsUnsupported()
    {
        const string answeredWithAFigure =
            """{"tag":"h2","heading":"Frequently Asked Questions","paragraphs":[],"href":null,"children":[{"tag":"h3","heading":"Does Partner Widget sync with QuickBooks Online?","paragraphs":[{"type":"text","runs":[{"text":"Yes. Partner Widget syncs each payment to QuickBooks Online within 45 minutes."}]}],"href":null,"children":[]}]}""";
        var onlyTheSearchFoundThis = new GccQuoteablePage(
            "https://partner.test/integrations", "Integrations", [],
            ["Partner Widget syncs each payment to QuickBooks Online within 45 minutes."]);

        var (result, _) = await WriteToolPageWithFaqAsync(
            ResearchWithFaqSearches(
                new GccFaqEvidence(FaqHost, SyncQuestion, [onlyTheSearchFoundThis]),
                new GccFaqEvidence(FaqHost, FlyQuestion, [])),
            answeredWithAFigure);

        var faq = Assert.Single(result.Document.Sections, s => s.Heading == "Frequently Asked Questions");
        Assert.Equal([SyncQuestion], faq.Children.Select(c => c.Heading));
    }

    [Fact]
    public async Task EveryQuestionsOutcomeIsOnTheRunsRecord()
    {
        var written = new List<GccGenerateJobEventWrite>();
        GccRunLog.Begin(Guid.NewGuid(), (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);

        await WriteToolPageWithFaqAsync(
            ResearchWithFaqSearches(new GccFaqEvidence(FaqHost, SyncQuestion, [SyncPassage()])),
            AnsweredOnlySync);

        var faq = JsonDocument.Parse(Assert.Single(written, e => e.Kind == "faq").PayloadJson).RootElement;
        Assert.Equal("partner.test", faq.GetProperty("host").GetString());
        var outcomes = faq.GetProperty("questions").EnumerateArray()
            .ToDictionary(q => q.GetProperty("question").GetString()!, q => q.GetProperty("outcome").GetString());
        Assert.Equal("answered", outcomes[SyncQuestion]);
        // The brief asks two questions and one was searched for: the other is on the record as not searched.
        Assert.Equal("not searched", outcomes[FlyQuestion]);
    }

    [Fact]
    public async Task BriefNoLongerReachesTheModelFramedAsRevisionFeedback()
    {
        // The old positional call passed `brief` into BuildToolBodyPrompt's revisionNotes slot,
        // so a first-time generation read as "REVISION REQUIRED -- address the reviewer's
        // feedback: <brief text>". Confirmed gone -- brief still reaches the model, just correctly,
        // via app.Description ("Tool summary: ...").
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider));
        var service = Build(provider, partner);

        await service.GenerateToolPageAsync(
            "Partner Widget", "A generic brief", "Some context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None);

        var bodyRequest = provider.Requests[1];
        var system = bodyRequest.Messages.First(m => m.Role == ChatRole.System).Content;
        Assert.DoesNotContain("REVISION REQUIRED", system, StringComparison.Ordinal);
        var userMessage = bodyRequest.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("Tool summary:", userMessage, StringComparison.Ordinal);
    }

    private const string ValidBriefJson =
        """{"primaryIntent":"commercial_investigation","buyingStage":"consideration","audienceSegment":"SMB","audienceNotes":"n/a","angle":"comparative","ctaType":"demo","toneOfVoice":"consultant_professional","eeatSignals":["experience"],"lengthBand":"standard"}""";

    private static GccCreateDto DispatchCreate(string startingContentType) => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
        StartingContentType: startingContentType, Topic: "Partner Widget", Notes: "brief",
        ProjectSiteRunId: Guid.NewGuid(), SiteSectionJson: null, BriefJson: ValidBriefJson,
        ResearchJson: null, Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

    [Theory]
    [InlineData("tool")]
    [InlineData("aiTool")]
    public async Task GenerateStartingContentAsyncRoutesBothToolSpellingsToTheGroundedToolPageBranch(
        string startingContentType)
    {
        // content-types.ts's live picker sends "tool" ("Tool page"), never "aiTool" -- before this
        // fix, GenerateStartingContentAsync's dispatch only matched "aiTool", so selecting "Tool
        // page" fell through to the generic long-form branch instead, silently skipping partner
        // grounding. Proven here by the exception it throws: the grounded branch's own fail-closed
        // message means dispatch reached it; the generic branch has no concept of partner
        // grounding at all and would never throw this specific message.
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider));
        var service = Build(provider, partner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateStartingContentAsync(
                DispatchCreate(startingContentType), null, ContentGeneratorProvider.OpenAi, CancellationToken.None));

        Assert.Contains("Partner grounding required", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("imagePrompt")]
    [InlineData("image-prompt")]
    public async Task GenerateStartingContentAsyncRoutesBothImagePromptSpellingsToTheImagePromptBranch(
        string startingContentType)
    {
        // Same mismatch class as tool/aiTool: content-types.ts sends "image-prompt". Proven the
        // same way -- the image-prompt branch's own precondition message ("requires a topic")
        // only fires from inside that branch, never from the generic long-form path. Notes used to
        // be the precondition this test leaned on; they are optional now, because the frontend
        // never sends them and a required field nothing supplies is a type that cannot generate.
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider));
        var service = Build(provider, partner);
        var create = DispatchCreate(startingContentType) with { Notes = null, Topic = "   " };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateStartingContentAsync(create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None));

        Assert.Contains("Standalone image prompt requires a topic", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_grounding_refusal_names_the_product_it_searched_for()
    {
        // Twice, with two different partner sets, five partners carrying 84-226 quotable spans each and
        // 130+ features between them produced "1 of 22 payload categories populated". The refusal read
        // as though the partners were thin. They were not: extraction is asked for one product by name,
        // and on the Create path that name is create.Topic -- so a topic that is a keyword sends it
        // looking for a product nobody sells.
        //
        // The refusal has to name that, or abundant evidence reads as missing evidence and the operator
        // re-crawls partners that were never the problem.
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.Scripted(
            new FakeProviderFactory(provider), GccPartnerExtractionFakes.EmptyPageExtraction);
        var service = Build(provider, partner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateToolPageAsync(
                "Accounts Payable: Automated Data Entry & Processing", "brief", "context", "marketing",
                null, ContentGeneratorProvider.OpenAi, CancellationToken.None,
                create: Create(ResearchJsonWithOnePartnerPage()), passages: PartnerPassages()));

        Assert.Contains("Refused: Partner grounding required", ex.Message, StringComparison.Ordinal);
        Assert.Contains(
            "searched", ex.Message, StringComparison.Ordinal);
        Assert.Contains(
            "a product named \"Accounts Payable: Automated Data Entry & Processing\"",
            ex.Message,
            StringComparison.Ordinal);
        Assert.Contains("keyword rather than one partner's product", ex.Message, StringComparison.Ordinal);
    }
}
