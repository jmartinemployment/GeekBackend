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
            NullLogger<GccPublisherProfileResolver>.Instance));

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
    public async Task FewerPromptsThanSectionsRefusesRatherThanAttachingAPartialSet()
    {
        // Only a hero and one section prompt came back for a two-section document. This used to
        // leave the second section null and say nothing: ask for six, get one, ship a page whose
        // H2s have no prompts, with a paid call behind it. One per H1 and every H2 is the contract
        // the system prompt states and the operator asked for (Jeff, 2026-09-23), so a short list
        // is a failure rather than a partial result.
        const string promptsResponse =
            """{"prompts":[{"section":"Hero","prompt":"hero image prompt"},{"section":"Section 1","prompt":"overview image prompt"}]}""";
        var provider = new ScriptedProvider(promptsResponse);
        var service = Build(provider);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateSectionImagePromptsAsync(
                "pillar", "Test Title", TwoSectionDocumentJson(), null,
                ContentGeneratorProvider.OpenAi, CancellationToken.None));

        // The message has to say what was expected and what arrived -- "image prompts failed" sends
        // the reader back to the logs, which is the pattern this codebase keeps paying for.
        Assert.Contains("expected 3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("received 2", ex.Message, StringComparison.Ordinal);
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
