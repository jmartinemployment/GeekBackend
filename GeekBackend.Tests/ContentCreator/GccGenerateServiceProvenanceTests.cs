using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Stage 2 (heading provenance), at the live-path integration level. Three things the pure
/// GccHeadingProvenanceGuardTests can't prove on their own: that GeneratePillarBodyAsync actually
/// resolves competitor evidence and research (not just that the guard's math is right), that the
/// resolved evidence actually reaches the rendered prompt the model sees, and that a body carrying
/// an unlicensed invented heading actually refuses the whole generation rather than silently
/// dropping the offending section.
/// </summary>
public class GccGenerateServiceProvenanceTests
{
    /// <summary>
    /// Answers by what the prompt asks for rather than by call index.
    ///
    /// <para>
    /// The index form encoded the exact sequence of model calls, so writing the body in batches --
    /// three calls where there had been one -- made every later index answer a different question
    /// than the one being asked, and six tests failed for a change none of them was about. A
    /// fixture keyed on call order pins an implementation detail it was never meant to assert.
    /// </para>
    ///
    /// <para>
    /// <c>body</c> is served once per batch, in order; batches past the scripted ones — these tests
    /// script the one heading they are about — get <see cref="ScriptedBody.PlannedBatch"/>, so the
    /// page the service assembles is a page rather than several copies of one section.
    /// </para>
    /// </summary>
    private sealed class ScriptedProvider(
        string lede, string imagePrompts, string metadata, params string[] body) : IContentGenerationProvider
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
                ? bodyCalls < body.Length ? body[bodyCalls++] : ScriptedBody.PlannedBatch(bodyCalls++)
                : asked.Contains("ledeType", StringComparison.OrdinalIgnoreCase) ? lede
                : asked.Contains("image-generation prompts", StringComparison.OrdinalIgnoreCase) ? imagePrompts
                : metadata;

            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
        }
    }

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

    // The real LedeAndIntroductionJsonContract BuildPillarLedePrompt asks for. This used to be a
    // sections array, which is the shape the caller wrongly parsed -- so the fixture encoded the
    // bug and kept it green while every pillar generation in production failed at the lede step.
    // Lede and introduction share a heading, the common case, so they merge into one H2.
    // Pillar now attaches image prompts and metadata before returning, so both calls are scripted.
    private static readonly string ImagePromptsJson = ScriptedBody.ImagePrompts();
    private const string ArticleMetadataJson =
        """{"title":"A Title","summary":"A standfirst.","metaDescription":"A meta description.","keywords":["k"],"sectionOutline":["A"]}""";

    private const string LedeJson =
        """{"lede":{"ledeType":"summary","heading":"Lede","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}]},"introduction":{"tag":"h2","heading":"Lede","paragraphs":[{"type":"text","runs":[{"text":"Body."}]},{"type":"text","runs":[{"text":"Book a free consultation.","href":"#consultationAppointment2xl"}]}],"href":null,"children":[]}}""";

    private static GccCreateDto Create(string? briefJson = null, string? researchJson = null, Guid? projectId = null) => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
        StartingContentType: "pillar", Topic: "AI implementation", Notes: null,
        ProjectSiteRunId: null, SiteSectionJson: null, BriefJson: briefJson, ResearchJson: researchJson,
        Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow,
        ProjectId: projectId);

    private static GccGenerateService Build(
        IContentGenerationProvider provider, GccCompetitorAnalysisResolver competitorResolver) => new(
        new ContentPromptBuilder(),
        TestContentTypePrompts.Registry(),
        new FakeProviderFactory(provider),
        new SoftwareApplicationSchemaBuilder(),
        new BlogPostingSchemaBuilder(),
        new ArticleSchemaBuilder(new SoftwareApplicationSchemaBuilder()),
        Options.Create(new CompanyProfileOptions()),
        NullLogger<GccGenerateService>.Instance,
        competitorResolver,
        GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider)),
        new GccCompetitorAnalysisResolverTests.FakeProjects(null),
        new GccPublisherProfileResolver(
            new GccCompetitorAnalysisResolverTests.FakeProjects(null),
            new GccCompetitorAnalysisResolverTests.FakePages(),
            NullLogger<GccPublisherProfileResolver>.Instance),
        new GccKnownToolsResolver(
            new GccCompetitorAnalysisResolverTests.FakePages(),
            NullLogger<GccKnownToolsResolver>.Instance));

    private static GccCompetitorAnalysisResolver NoCompetitorData() =>
        GccCompetitorAnalysisResolverTests.Build(
            new GccCompetitorAnalysisResolverTests.FakeProjects(null),
            new GccCompetitorAnalysisResolverTests.FakePages(),
            new GccCompetitorAnalysisResolverTests.FakeRag());

    [Fact]
    public async Task UnlicensedInventedHeadingRefusesTheWholeGeneration()
    {
        var provider = new ScriptedProvider(
            lede: LedeJson,
            imagePrompts: ImagePromptsJson,
            metadata: ArticleMetadataJson,
            body: """{"sections":[{"tag":"h2","heading":"Overview","paragraphs":[],"href":null,"provenance":"plan","children":[{"tag":"h3","heading":"Made Up Subtopic","paragraphs":[],"href":null,"children":[]}]}]}""");
        var service = Build(provider, NoCompetitorData());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GeneratePillarBodyAsync(Create(), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None));

        Assert.Contains("unlicensed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Made Up Subtopic", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanTaggedTopLevelHeadingsPassWithoutAnyOtherEvidence()
    {
        var provider = new ScriptedProvider(
            lede: LedeJson,
            imagePrompts: ImagePromptsJson,
            metadata: ArticleMetadataJson,
            body: """{"sections":[{"tag":"h2","heading":"Overview","paragraphs":[],"href":null,"provenance":"plan","children":[]}]}""");
        var service = Build(provider, NoCompetitorData());

        var json = await service.GeneratePillarBodyAsync(Create(), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        Assert.Contains("Overview", json);
    }

    [Fact]
    public async Task CompetitorHeadingReachesThePromptAndLicensesAMatchingTag()
    {
        const string competitorHtml =
            """
            <html><body>
              <h1>Pricing</h1>
              <h2>Enterprise Rollout Timeline</h2>
            </body></html>
            """;
        var rag = new GccCompetitorAnalysisResolverTests.FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://competitor.test", "competitor.test", true, Guid.NewGuid().ToString())]);
        var pages = new GccCompetitorAnalysisResolverTests.FakePages(
            [GccCompetitorAnalysisResolverTests.CrawledPage("https://competitor.test/pricing", competitorHtml)]);
        var project = GccCompetitorAnalysisResolverTests.Project("https://competitor.test");
        var resolver = GccCompetitorAnalysisResolverTests.Build(
            new GccCompetitorAnalysisResolverTests.FakeProjects(project), pages, rag);

        var provider = new ScriptedProvider(
            lede: LedeJson,
            imagePrompts: ImagePromptsJson,
            metadata: ArticleMetadataJson,
            body: """{"sections":[{"tag":"h2","heading":"What a staged rollout costs in week one","paragraphs":[],"href":null,"provenance":"plan","children":[{"tag":"h3","heading":"How long the first site really takes","paragraphs":[],"href":null,"children":[],"provenance":"competitor:Enterprise Rollout Timeline"}]}]}""");
        var service = Build(provider, resolver);
        var create = Create(projectId: project.Id);

        var json = await service.GeneratePillarBodyAsync(create, null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        Assert.Contains("How long the first site really takes", json);
        // The guard passing isn't proof the wiring happened -- the rendered prompt is. Assert the
        // body call's own system message actually showed the model this heading and its source URL.
        var bodyRequest = provider.Requests[1];
        var systemMessage = bodyRequest.Messages.First(m => m.Role == ChatRole.System).Content;
        Assert.Contains("Enterprise Rollout Timeline", systemMessage, StringComparison.Ordinal);
        Assert.Contains("competitor.test/pricing", systemMessage, StringComparison.Ordinal);
    }

    // "retrieval:<url>" provenance was removed 2026-09-22 (Jeff, "remove this stupid rule") --
    // it checked a claimed source URL against create.ResearchJson's Quoteables, an optional,
    // operator-uploaded, often-empty set, so it failed on missing research rather than on bad
    // output. The two tests that lived here (a matching retrieval tag licensing a heading; a
    // non-matching one refusing the draft) tested a mechanism that no longer exists. Coverage that
    // an unrecognized provenance kind is always a violation lives in
    // GccHeadingProvenanceGuardTests.RetrievalTagIsNoLongerARecognizedKind.
}
