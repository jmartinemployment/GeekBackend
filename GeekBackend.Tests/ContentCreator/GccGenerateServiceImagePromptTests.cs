using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Interfaces.ContentWriterV3;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Image prompts, 2026-09-22. GenerateSectionImagePromptsAsync computed a real, paid per-H2
/// image-prompt response and every caller discarded it -- the return value was never used.
/// AddImagePromptForContentAsync's own comment admitted the same for email/LinkedIn/Facebook:
/// "for now, just return the content as-is; image prompts can be stored separately." Every
/// content type that attempted image prompts at all threw the result away; every other type
/// never attempted them. Fixed so the computed prompts actually reach the persisted document.
/// </summary>
public class GccGenerateServiceImagePromptTests
{
    private sealed class ScriptedProvider(string response) : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => LlmProviderType.OpenAi;
        public List<ChatCompletionRequest> Requests { get; } = [];

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ChatCompletionResult(response, "test-model", null, null));
        }
    }

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

    private static GccGenerateService Build(IContentGenerationProvider provider) => new(
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

    private static string TwoSectionDocumentJson() =>
        GccGenerateService.SerializeDocument(new ContentDocument(
            Lede: new Section("h2", "Intro", [], null, []),
            Sections:
            [
                new Section("h2", "Overview", [], null, []),
                new Section("h2", "Details", [], null, []),
            ]));

    [Fact]
    public async Task HeroAndPerSectionPromptsAreMergedIntoTheDocumentPositionally()
    {
        const string promptsResponse =
            """{"prompts":[{"section":"Hero","prompt":"hero image prompt"},{"section":"Section 1","prompt":"overview image prompt"},{"section":"Section 2","prompt":"details image prompt"}]}""";
        var provider = new ScriptedProvider(promptsResponse);
        var service = Build(provider);

        var updatedJson = await service.GenerateSectionImagePromptsAsync(
            "pillar", "Test Title", TwoSectionDocumentJson(), null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        var document = JsonSerializer.Deserialize<ContentDocument>(
            updatedJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal("hero image prompt", document.Lede.ImagePrompt);
        Assert.Equal("overview image prompt", document.Sections[0].ImagePrompt);
        Assert.Equal("details image prompt", document.Sections[1].ImagePrompt);
        // The rest of the document survives the round-trip untouched.
        Assert.Equal("Intro", document.Lede.Heading);
        Assert.Equal("Overview", document.Sections[0].Heading);
    }

    [Fact]
    public async Task FewerPromptsThanSectionsLeavesTheRestNullRatherThanThrowing()
    {
        // Only a hero and one section prompt came back -- the second section gets none, not an
        // exception and not a mismatched/shifted assignment.
        const string promptsResponse =
            """{"prompts":[{"section":"Hero","prompt":"hero image prompt"},{"section":"Section 1","prompt":"overview image prompt"}]}""";
        var provider = new ScriptedProvider(promptsResponse);
        var service = Build(provider);

        var updatedJson = await service.GenerateSectionImagePromptsAsync(
            "pillar", "Test Title", TwoSectionDocumentJson(), null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        var document = JsonSerializer.Deserialize<ContentDocument>(
            updatedJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal("overview image prompt", document.Sections[0].ImagePrompt);
        Assert.Null(document.Sections[1].ImagePrompt);
    }

    [Fact]
    public async Task EmptyPromptsListThrowsRatherThanSilentlySucceedingWithNoImages()
    {
        var provider = new ScriptedProvider("""{"prompts":[]}""");
        var service = Build(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateSectionImagePromptsAsync(
                "pillar", "Test Title", TwoSectionDocumentJson(), null, ContentGeneratorProvider.OpenAi, CancellationToken.None));
    }

    [Fact]
    public void MergeImagePromptFieldAttachesTheRichPromptObjectAsANestedField()
    {
        const string emailBody = """{"subject":"Hi there","body":"Email text.","ctaLabel":"Read more"}""";
        const string imagePrompt = """{"prompt":"an illustration","style":"Illustration","aspectRatio":"16:9"}""";

        var merged = GccGenerateService.MergeImagePromptField(emailBody, imagePrompt);
        using var doc = JsonDocument.Parse(merged);
        var root = doc.RootElement;

        // Original fields survive untouched.
        Assert.Equal("Hi there", root.GetProperty("subject").GetString());
        Assert.Equal("Read more", root.GetProperty("ctaLabel").GetString());
        // The rich object is nested whole, not flattened or stringified.
        var imagePromptNode = root.GetProperty("imagePrompt");
        Assert.Equal("an illustration", imagePromptNode.GetProperty("prompt").GetString());
        Assert.Equal("16:9", imagePromptNode.GetProperty("aspectRatio").GetString());
    }

    [Fact]
    public void MergeImagePromptFieldThrowsOnNonObjectContent()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GccGenerateService.MergeImagePromptField("[1,2,3]", """{"prompt":"x"}"""));
    }
}
