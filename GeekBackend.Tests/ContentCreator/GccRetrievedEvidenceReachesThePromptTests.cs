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
/// Retrieved evidence reaches the model, asserted on the rendered prompt.
///
/// <para>
/// Retrieval resolving and a list being populated prove nothing about what the writer was shown:
/// this codebase has shipped a competitor extraction subsystem whose every caller sat on a path
/// that never ran, a <c>KnownCrawlTools</c> field that was empty on every generate so no draft ever
/// linked a tool, and a partner/competitor index check whose answers were rendered and read by
/// nothing. Each of those passed its own unit tests.
/// </para>
///
/// <para>
/// So these run the real generate path against a create carrying real retrieved evidence, capture
/// the prompts actually sent, and assert the passage text is in them. Nothing here mocks a prompt
/// builder or inspects an intermediate list.
/// </para>
/// </summary>
public class GccRetrievedEvidenceReachesThePromptTests
{
    private const string PartnerText = "Partner Widget is billed per document processed.";
    private const string CompetitorText = "We run accounts payable projects end to end.";
    private const string SiteText = "We published our AP automation guide last quarter.";
    private const string CompetitorUrl = "https://rival.test/services";

    private const string LedeJson =
        """{"lede":{"ledeType":"summary","heading":"Lede","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}]},"introduction":{"tag":"h2","heading":"Lede","paragraphs":[{"type":"text","runs":[{"text":"Body."}]},{"type":"text","runs":[{"text":"Book a free consultation.","href":"#consultationAppointment2xl"}]}],"href":null,"children":[]}}""";
    private const string MetadataJson =
        """{"title":"A Title","summary":"A standfirst.","metaDescription":"A meta description.","keywords":["k"],"sectionOutline":["A"]}""";

    private sealed class CapturingProvider : IContentGenerationProvider
    {
        private int bodyCalls;

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;
        public List<ChatCompletionRequest> Requests { get; } = [];

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var asked = string.Join("\n", request.Messages.Select(m => m.Content));
            var content = request.JsonSchemaName == "sections"
                ? ScriptedBody.PlannedBatch(bodyCalls++)
                : asked.Contains("ledeType", StringComparison.OrdinalIgnoreCase) ? LedeJson
                : asked.Contains("image-generation prompts", StringComparison.OrdinalIgnoreCase)
                    ? ScriptedBody.ImagePrompts()
                : MetadataJson;
            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
        }

        /// <summary>Everything sent on the first body call — the one the evidence is attached to.</summary>
        public string FirstBodyPrompt =>
            string.Join(
                "\n",
                Requests.First(r => r.JsonSchemaName == "sections").Messages.Select(m => m.Content));
    }

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

    private static GccQuoteablePage Page(string url, string title, string paragraph) =>
        new(url, title, [new HeadingDto(2, title)], [paragraph],
            RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk);

    /// <summary>A create carrying exactly what the resolver would have merged into it.</summary>
    private static GccCreateDto CreateWithRetrievedEvidence()
    {
        var research = new GccResearchDocument(
            SerpIndex: null,
            Quoteables: [Page("https://partner.test/pricing", "Partner pricing", PartnerText)],
            CompetitorQuoteables: [Page(CompetitorUrl, "Rival services", CompetitorText)],
            SiteQuoteables: [Page("https://acme.test/ap-guide", "Our AP guide", SiteText)]);

        return new GccCreateDto(
            Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
            StartingContentType: "pillar", Topic: "Accounts Payable: Automated Data Entry",
            Notes: null, ProjectSiteRunId: null, SiteSectionJson: null, BriefJson: null,
            ResearchJson: GccResearchFetchService.Serialize(research),
            Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);
    }

    private static GccGenerateService Build(IContentGenerationProvider provider) => new(
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
        GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider)),
        new GccCompetitorAnalysisResolverTests.FakeProjects(null),
        new GccPublisherProfileResolver(
            new GccCompetitorAnalysisResolverTests.FakeProjects(null),
            new GccCompetitorAnalysisResolverTests.FakePages(),
            NullLogger<GccPublisherProfileResolver>.Instance),
        new GccKnownToolsResolver(
            new GccCompetitorAnalysisResolverTests.FakePages(),
            NullLogger<GccKnownToolsResolver>.Instance));

    private static async Task<CapturingProvider> GeneratePillar()
    {
        var provider = new CapturingProvider();
        await Build(provider).GeneratePillarBodyAsync(
            CreateWithRetrievedEvidence(), null, ContentGeneratorProvider.OpenAi, null,
            CancellationToken.None);
        return provider;
    }

    [Fact]
    public async Task PartnerProseIsInThePromptTheModelReceives()
    {
        var provider = await GeneratePillar();

        Assert.Contains(PartnerText, provider.FirstBodyPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompetitorProseIsInThePromptWithItsOwnRules()
    {
        // The finding this closes: competitor pages were crawled, extracted to typed blocks,
        // ingested and vector-indexed, and the only thing a writer ever saw of them was heading
        // text regexed out of raw HTML.
        var provider = await GeneratePillar();

        Assert.Contains(CompetitorText, provider.FirstBodyPrompt, StringComparison.Ordinal);
        Assert.Contains("never quote, cite or link", provider.FirstBodyPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheRivalsUrlIsNeverPutInFrontOfTheModel()
    {
        // A URL in the prompt is a URL that can end up in the page. The partner block deliberately
        // carries its URLs, because a partner claim must be attributable; the competitor block
        // deliberately carries none.
        var provider = await GeneratePillar();

        Assert.DoesNotContain(CompetitorUrl, provider.FirstBodyPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnSiteCoverageIsInThePromptAsGroundAlreadyCovered()
    {
        var provider = await GeneratePillar();

        Assert.Contains(SiteText, provider.FirstBodyPrompt, StringComparison.Ordinal);
        Assert.Contains("do not write these again", provider.FirstBodyPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EveryBatchGetsTheEvidence_NotJustTheFirst()
    {
        // The body is written in batches. Evidence attached to only the first call would leave the
        // later sections writing about partners and rivals they were never shown.
        var provider = await GeneratePillar();

        var bodyPrompts = provider.Requests
            .Where(r => r.JsonSchemaName == "sections")
            .Select(r => string.Join("\n", r.Messages.Select(m => m.Content)))
            .ToList();

        Assert.True(bodyPrompts.Count > 1, "the pillar body should be written in more than one call");
        Assert.All(bodyPrompts, p => Assert.Contains(CompetitorText, p, StringComparison.Ordinal));
        Assert.All(bodyPrompts, p => Assert.Contains(SiteText, p, StringComparison.Ordinal));
    }
}
