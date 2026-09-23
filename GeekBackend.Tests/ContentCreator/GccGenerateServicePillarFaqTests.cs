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
    private const string SectionJson = """{"tag":"h2","heading":"Section","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}],"href":null,"children":[]}""";
    // "provenance":"plan" -- the lede/body calls stand in for the pillar's assigned outline
    // headings, so Stage 2's guard accepts them unconditionally, same as production would.
    // Call 0 is the lede, which asks for LedeAndIntroductionJsonContract -- not a sections array.
    private const string LedeAndIntroJson =
        """{"lede":{"ledeType":"summary","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}]},"introduction":{"tag":"h2","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}],"href":null,"children":[]}}""";

    private const string SectionsArrayJson = """{"sections":[{"tag":"h2","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}],"href":null,"children":[],"provenance":"plan"}]}""";

    private sealed class RecordingProvider : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => LlmProviderType.OpenAi;
        public List<string> SystemPromptsSeen { get; } = [];

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
            // Call order, not content-sniffing: Stage 2's provenance instruction text itself
            // mentions "People Also Ask" (describing the "paa:" tag) now that it's appended to the
            // body prompt too, so a substring match against the FAQ prompt's own heading text is no
            // longer reliable. GeneratePillarBodyAsync's own sequence is lede(0) -> body(1) ->
            // FAQ(2), so index is unambiguous.
            var callIndex = SystemPromptsSeen.Count;
            SystemPromptsSeen.Add(system);
            var content = callIndex switch
            {
                0 => LedeAndIntroJson,
                2 => SectionJson,
                _ => SectionsArrayJson,
            };
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
        new FakeProviderFactory(provider),
        new SoftwareApplicationSchemaBuilder(),
        Options.Create(new CompanyProfileOptions()),
        NullLogger<GccGenerateService>.Instance,
        GccCompetitorAnalysisResolverTests.Build(
            new GccCompetitorAnalysisResolverTests.FakeProjects(null),
            new GccCompetitorAnalysisResolverTests.FakePages(),
            new GccCompetitorAnalysisResolverTests.FakeRag()),
        GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider)));

    [Fact]
    public async Task NoPaaQuestionsMeansNoFaqCompletionCall()
    {
        var provider = new RecordingProvider();
        var service = Build(provider);
        var brief = """{"primaryIntent":"commercial_investigation"}""";

        await service.GeneratePillarBodyAsync(Create(brief), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        // Lede + body only.
        Assert.Equal(2, provider.SystemPromptsSeen.Count);
        Assert.DoesNotContain(provider.SystemPromptsSeen, p => p.Contains("FAQ section", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PaaQuestionsPresentAddsExactlyOneFaqCompletionCall()
    {
        var provider = new RecordingProvider();
        var service = Build(provider);
        var brief = """{"paaQuestions":["What is AI implementation?","How much does it cost?"]}""";

        await service.GeneratePillarBodyAsync(Create(brief), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        // Lede + body + FAQ.
        Assert.Equal(3, provider.SystemPromptsSeen.Count);
        Assert.Contains("FAQ section", provider.SystemPromptsSeen[2], StringComparison.Ordinal);
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
