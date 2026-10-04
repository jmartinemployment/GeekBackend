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
/// Stage 8c: PaaQuestions was parsed out of the brief and then silently dropped on the live
/// pillar-generation path -- never fed to an FAQ section. These prove the fix at the level that
/// actually matters here: how many completion calls fire, and whether the extra one is real.
/// </summary>
public class GccGenerateServicePillarFaqTests
{
    private const string SectionJson = """{"tag":"h2","heading":"Section","paragraphs":[{"type":"text","runs":[{"text":"Body."}]},{"type":"text","runs":[{"text":"Book a free consultation.","href":"#consultationAppointment2xl"}]}],"href":null,"children":[]}""";
    // "provenance":"plan" -- the lede/body calls stand in for the pillar's assigned outline
    // headings, so Stage 2's guard accepts them unconditionally, same as production would.
    // Call 0 is the lede, which asks for LedeAndIntroductionJsonContract -- not a sections array.
    private const string LedeAndIntroJson =
        """{"lede":{"ledeType":"summary","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}]},"introduction":{"tag":"h2","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body."}]},{"type":"text","runs":[{"text":"Book a free consultation.","href":"#consultationAppointment2xl"}]}],"href":null,"children":[]}}""";

    private static readonly string ImagePromptsJson = ScriptedBody.ImagePrompts();
    // ArticleMetadataDraft, now including the standfirst summary.
    private const string ArticleMetadataJson =
        """{"title":"A Title","summary":"A standfirst.","metaDescription":"A meta description.","keywords":["k"],"sectionOutline":["A"]}""";

    private const string SectionsArrayJson = """{"sections":[{"tag":"h2","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body."}]},{"type":"text","runs":[{"text":"Book a free consultation.","href":"#consultationAppointment2xl"}]}],"href":null,"children":[],"provenance":"plan"}]}""";

    /// <summary>
    /// Answers by what each prompt asks for. This used to key off call order -- lede(0), body(1),
    /// FAQ(2) -- which stopped being a sequence the moment the body began arriving in batches: the
    /// FAQ answer went to a body call and the run failed on a shape mismatch. Whether the FAQ call
    /// happens is exactly what these tests are about, so it is the one thing the fixture must not
    /// assume.
    /// </summary>
    private sealed class RecordingProvider : IContentGenerationProvider
    {
        private int bodyCalls;

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;
        public List<string> SystemPromptsSeen { get; } = [];

        /// <summary>The literal that opens BuildArticleFaqSectionPrompt, and nothing else. The
        /// metadata prompt names "People Also Ask" too, when it tells the model to end the outline
        /// with one, so the bare phrase does not identify this call.</summary>
        internal const string FaqPromptMarker = """Write ONLY the "People Also Ask" FAQ section""";

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
            SystemPromptsSeen.Add(system);

            // The body is the only call asking for a sections array; the FAQ asks for one section,
            // and the remaining three are told apart by their own prompt text. The lede is the
            // fallback because its contract is the one thing named in no other prompt here.
            var content = request.JsonSchemaName == "sections"
                ? bodyCalls < 1 ? SectionsArrayJson : ScriptedBody.PlannedBatch(bodyCalls)
                : system.Contains(FaqPromptMarker, StringComparison.Ordinal) ? SectionJson
                : system.Contains("image-generation prompts", StringComparison.Ordinal) ? ImagePromptsJson
                : system.Contains("sectionOutline", StringComparison.Ordinal) ? ArticleMetadataJson
                : LedeAndIntroJson;
            if (request.JsonSchemaName == "sections") bodyCalls++;

            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
        }
    }

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

    private static GccCreateDto Create(string? briefJson) => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
        StartingContentType: "pillar", Topic: "AI implementation", Notes: null,
        ProjectSiteRunId: null, SiteSectionJson: null, BriefJson: briefJson, ResearchJson: null,
        Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

    private static GccGenerateService Build(RecordingProvider provider) => new(
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
            NullLogger<GccKnownToolsResolver>.Instance),
        new GccToolPageFanOutFixture.FakeExtractionBank());

    [Fact]
    public async Task NoPaaQuestionsMeansNoFaqCompletionCall()
    {
        var provider = new RecordingProvider();
        var service = Build(provider);
        var brief = """{"primaryIntent":"commercial_investigation"}""";

        await service.GeneratePillarBodyAsync(Create(brief), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        // No FAQ call. The total call count used to stand in for this and no longer can: the body
        // is written in batches, so it is several calls and the number is the outline's business.
        Assert.DoesNotContain(
            provider.SystemPromptsSeen,
            p => p.Contains(RecordingProvider.FaqPromptMarker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PaaQuestionsPresentAddsExactlyOneFaqCompletionCall()
    {
        var provider = new RecordingProvider();
        var service = Build(provider);
        var brief = """{"paaQuestions":["What is AI implementation?","How much does it cost?"]}""";

        await service.GeneratePillarBodyAsync(Create(brief), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        // Exactly one, which is the claim -- two PAA questions are one FAQ section, not two calls.
        Assert.Equal(
            1,
            provider.SystemPromptsSeen.Count(
                p => p.Contains(RecordingProvider.FaqPromptMarker, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheFaqSectionActuallyReachesTheGeneratedDocument()
    {
        var provider = new RecordingProvider();
        var service = Build(provider);
        var brief = """{"paaQuestions":["What is AI implementation?"]}""";

        var json = await service.GeneratePillarBodyAsync(Create(brief), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        // SectionJson's own heading is "Section" -- confirm it landed as a real section in the
        // persisted document, not just that a call happened.
        Assert.Contains("\"heading\":\"Section\"", json);
    }
}
