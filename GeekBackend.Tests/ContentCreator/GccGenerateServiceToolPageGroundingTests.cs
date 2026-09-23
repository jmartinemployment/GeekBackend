using GeekAPI.Services.ContentCreator;
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
/// consumed GccV2PartnerExtractionService's real payloads even though the "AI Tools" manual-entry
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
    // The body names the product, because a real tool page does and the generator now refuses one
    // that does not. This fixture said "Body." / "Capabilities." / "Considerations." and would have
    // shipped a page about nothing in particular.
    private const string ToolBodyJson =
        """{"sections":[{"tag":"h2","heading":"Where the setup hours actually go","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget removes the manual pass."}]}],"href":null,"children":[]},{"tag":"h2","heading":"What the wizard takes off your desk","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget captures the invoice on arrival."}]}],"href":null,"children":[]},{"tag":"h2","heading":"Mapping your data before go-live","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget needs the vendor master mapped first."}]}],"href":null,"children":[]}]}""";
    private const string ToolImagePromptsJson =
        // One per H1 plus one per H2, with headroom for the optional FAQ section -- a short list is
        // refused now rather than silently leaving sections without a prompt.
        """{"prompts":[{"section":"Hero","prompt":"hero image prompt"},{"section":"Section 1","prompt":"capabilities image prompt"},{"section":"Section 2","prompt":"considerations image prompt"},{"section":"Section 3","prompt":"third image prompt"},{"section":"Section 4","prompt":"fourth image prompt"},{"section":"Section 5","prompt":"fifth image prompt"},{"section":"Section 6","prompt":"sixth image prompt"}]}""";
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

    private sealed class ScriptedProvider(bool includeFaq = false) : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => LlmProviderType.OpenAi;
        public List<ChatCompletionRequest> Requests { get; } = [];

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            // Call 0 = the lede, call 1 = tool body (sections array), [call 2 = FAQ section when
            // includeFaq], then per-H2 image prompts, last = tool metadata (flat object).
            //
            // The lede runs first so the body can continue it. It used to run after the body, which
            // is how a page reads well for three paragraphs and then turns into a chore -- the
            // opening was fitted to the front of a draft already written in reference voice.
            var content = Requests.Count switch
            {
                1 => ToolLedeJson,
                2 => ToolBodyJson,
                3 when includeFaq => ToolFaqJson,
                3 => ToolImagePromptsJson,
                4 when includeFaq => ToolImagePromptsJson,
                _ => ToolMetadataJson,
            };
            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
        }
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
        IContentGenerationProvider provider, GeekAPI.Services.ContentCreatorV2.Partner.GccV2PartnerExtractionService partnerExtraction) => new(
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
        new GccCompetitorAnalysisResolverTests.FakeProjects(null));

    private static string ResearchJsonWithOnePartnerPage() =>
        GccResearchFetchService.Serialize(new GccResearchDocument(
            SerpIndex: null,
            Quoteables:
            [
                new GccQuoteablePage(
                    Url: "https://partner.test/widget",
                    Title: "Partner Widget",
                    Headings: [new HeadingDto(2, "Pricing")],
                    Paragraphs: ["Partner Widget starts at $19 per month, billed monthly."],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
            ]));

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
            Icp = [new GeekAPI.Services.ContentCreatorV2.Partner.PartnerIcpItem(
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
        Assert.Contains("1 of 22 payload categories populated", ex.Message, StringComparison.Ordinal);
        Assert.Contains("core capability signal", ex.Message, StringComparison.Ordinal);
        Assert.Contains("missing", ex.Message, StringComparison.Ordinal);
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
            Citables = [new GeekAPI.Services.ContentCreatorV2.Partner.PartnerCitableItem(
                "Partner Widget reduces setup time by half.", "reduces setup time by half")],
            FeatureInventory = [new GeekAPI.Services.ContentCreatorV2.Partner.PartnerFeatureItem(
                "Automated setup wizard", "Onboarding", null, "automated setup wizard")],
            Integrations = [new GeekAPI.Services.ContentCreatorV2.Partner.PartnerIntegrationItem(
                "Slack", "Notifications", "API", "Slack integration")],
        };
        var partner = GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction);
        var service = Build(provider, partner);

        var result = await service.GenerateToolPageAsync(
            "Partner Widget", "brief", "context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None,
            create: Create(ResearchJsonWithOnePartnerPage()));

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
    public async Task GroundedFaqBankDataProducesAnAdditionalFaqSectionBeyondTheWordCountTarget()
    {
        var provider = new ScriptedProvider(includeFaq: true);
        // Three categories populated, not just one -- HasSufficientPartnerData (2026-09-22) refuses
        // a create with only a single thin field, the same standard Pillar/Blog get from
        // GccHeadingProvenanceGuard.
        var extraction = GccPartnerExtractionFakes.EmptyPageExtraction with
        {
            Citables = [new GeekAPI.Services.ContentCreatorV2.Partner.PartnerCitableItem(
                "Partner Widget reduces setup time by half.", "reduces setup time by half")],
            FeatureInventory = [new GeekAPI.Services.ContentCreatorV2.Partner.PartnerFeatureItem(
                "Automated setup wizard", "Onboarding", null, "automated setup wizard")],
            // PartnerFaqItem is the raw per-page shape ExtractFromPagesAsync aggregates into the
            // final GccPartnerFaqAsset (page.Url + built provenance) -- no quote-in-text gate at
            // this stage, just non-empty Question/Answer.
            Faqs = [new GeekAPI.Services.ContentCreatorV2.Partner.PartnerFaqItem(
                "Is Partner Widget secure?", "Yes, SOC 2 Type II certified.", "SOC 2 Type II certified")],
        };
        var partner = GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction);
        var service = Build(provider, partner);

        var result = await service.GenerateToolPageAsync(
            "Partner Widget", "brief", "context", "marketing", null,
            ContentGeneratorProvider.OpenAi, CancellationToken.None,
            create: Create(ResearchJsonWithOnePartnerPage()));

        // FAQ is a real, distinct call of its own -- carrying the verified answer for the model to
        // paraphrase, not a question it must answer from scratch -- and the resulting section
        // survives into the persisted document, beyond the body's own outline.
        var faqRequest = provider.Requests[2];
        var faqUserMessage = faqRequest.Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.Contains("SOC 2 Type II certified", faqUserMessage, StringComparison.Ordinal);
        Assert.Contains("Frequently Asked Questions", result.Document.Sections.Select(s => s.Heading));
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
        // same way -- the image-prompt branch's own precondition message ("requires topic and
        // notes") only fires from inside that branch, never from the generic long-form path.
        var provider = new ScriptedProvider();
        var partner = GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider));
        var service = Build(provider, partner);
        var create = DispatchCreate(startingContentType) with { Notes = null };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateStartingContentAsync(create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None));

        Assert.Contains("Standalone image prompt requires topic and notes", ex.Message, StringComparison.Ordinal);
    }
}
