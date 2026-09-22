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
    private const string SectionsArrayJson = """{"sections":[{"tag":"h2","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}],"href":null,"children":[]}]}""";

    private sealed class RecordingProvider : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => LlmProviderType.OpenAi;
        public List<string> SystemPromptsSeen { get; } = [];

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
            SystemPromptsSeen.Add(system);
            // Lede/body calls expect a sections array; the FAQ call expects a single Section.
            var content = system.Contains("People Also Ask", StringComparison.Ordinal)
                ? SectionJson
                : SectionsArrayJson;
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
        NullLogger<GccGenerateService>.Instance);

    [Fact]
    public async Task NoPaaQuestionsMeansNoFaqCompletionCall()
    {
        var provider = new RecordingProvider();
        var service = Build(provider);
        var brief = """{"primaryIntent":"commercial_investigation"}""";

        await service.GeneratePillarBodyAsync(Create(brief), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        // Lede + body only.
        Assert.Equal(2, provider.SystemPromptsSeen.Count);
        Assert.DoesNotContain(provider.SystemPromptsSeen, p => p.Contains("People Also Ask", StringComparison.Ordinal));
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
        Assert.Contains(provider.SystemPromptsSeen, p => p.Contains("People Also Ask", StringComparison.Ordinal));
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
