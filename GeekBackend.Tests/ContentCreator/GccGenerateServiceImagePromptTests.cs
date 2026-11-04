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
    internal sealed class ScriptedProvider(string response) : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public string ModelFor(ChatCompletionRequest request) => "gpt-4.1";
        public List<ChatCompletionRequest> Requests { get; } = [];

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ChatCompletionResult(response, "test-model", null, null));
        }
    }

    internal sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

    internal static GccGenerateService Build(IContentGenerationProvider provider) => new(
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

    private static Paragraph Text(string text) => new TextParagraph([new Run(text)]);

    private static string TwoSectionDocumentJson() =>
        JsonSerializer.Serialize(
            new ContentDocument(
                Lede: new Section("h2", "Intro", [Text("Late payroll filings cost a small firm penalties every quarter.")], null, []),
                Sections:
                [
                    new Section("h2", "Overview", [Text("Worker classification mistakes between W-2 staff and 1099 contractors.")], null, []),
                    new Section("h2", "Details", [Text("Quarterly filings, deposits and the notices that follow a miss.")], null, []),
                ]),
            GccDocumentJson.Options);

    private static string ThreeScenes(string hero = "the hero", string overview = "the overview", string details = "the details") =>
        $$"""{"prompts":[{"section":"Hero","prompt":"{{ScriptedBody.Scene(hero)}}"},{"section":"Section 1","prompt":"{{ScriptedBody.Scene(overview)}}"},{"section":"Section 2","prompt":"{{ScriptedBody.Scene(details)}}"}]}""";

    [Fact]
    public async Task HeroAndPerSectionPromptsAreMergedIntoTheDocumentPositionally()
    {
        var provider = new ScriptedProvider(ThreeScenes());
        var service = Build(provider);

        var updatedJson = await service.GenerateSectionImagePromptsAsync(
            "pillar", "Test Title", TwoSectionDocumentJson(), null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        var document = JsonSerializer.Deserialize<ContentDocument>(
            updatedJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        // The model's scene, then the closing the code appends: palette, lighting, no text.
        Assert.Equal($"{ScriptedBody.Scene("the hero")} {GccImageStyle.Closing}", document.Lede.ImagePrompt);
        Assert.Equal($"{ScriptedBody.Scene("the overview")} {GccImageStyle.Closing}", document.Sections[0].ImagePrompt);
        Assert.Equal($"{ScriptedBody.Scene("the details")} {GccImageStyle.Closing}", document.Sections[1].ImagePrompt);
        // The rest of the document survives the round-trip untouched.
        Assert.Equal("Intro", document.Lede.Heading);
        Assert.Equal("Overview", document.Sections[0].Heading);
    }

    [Fact]
    public async Task TheRequestShowsEachSectionsTextAndTheAudienceAndNoSizeThatContradictsAnother()
    {
        var provider = new ScriptedProvider(ThreeScenes());
        var service = Build(provider);

        await service.GenerateSectionImagePromptsAsync(
            "pillar", "Payroll tax for small firms", TwoSectionDocumentJson(), null, ContentGeneratorProvider.OpenAi,
            CancellationToken.None, audienceLines: "Audience notes: Florida construction company owners");

        var request = Assert.Single(provider.Requests);
        var system = request.Messages.First(m => m.Role == ChatRole.System).Content;
        var user = request.Messages.First(m => m.Role == ChatRole.User).Content;
        // What each picture is about is what the section says, not only its heading.
        Assert.Contains("Late payroll filings cost a small firm penalties", user, StringComparison.Ordinal);
        Assert.Contains("W-2 staff and 1099 contractors", user, StringComparison.Ordinal);
        Assert.Contains("Quarterly filings, deposits", user, StringComparison.Ordinal);
        Assert.Contains("Florida construction company owners", user, StringComparison.Ordinal);
        // One style, one place: the palette is the code's to append, so the model is told not to write it.
        Assert.Contains("A clean, flat-design 3D vector illustration of", system, StringComparison.Ordinal);
        Assert.Contains("Do not write the palette", system, StringComparison.Ordinal);
        Assert.DoesNotContain("1200x630", system, StringComparison.Ordinal);
        Assert.DoesNotContain("16:9", system, StringComparison.Ordinal);
        Assert.DoesNotContain('\u2014', system);
        // Image prompts are Utility work, and eleven scenes do not fit in the old 2048-token reply.
        Assert.Equal(LlmTaskClass.Utility, request.TaskClass);
        Assert.True(request.MaxOutputTokens > 2048);
    }

    [Fact]
    public async Task ABriefThatGivesNoAudienceTellsTheModelNone()
    {
        var provider = new ScriptedProvider(ThreeScenes());
        var service = Build(provider);

        await service.GenerateSectionImagePromptsAsync(
            "pillar", "Test Title", TwoSectionDocumentJson(), null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        var user = provider.Requests.Single().Messages.First(m => m.Role == ChatRole.User).Content;
        Assert.DoesNotContain("Audience", user, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASceneOutsideTheBandRefusesTheWholeSetAndAttachesNothing()
    {
        // The second scene is a few words. A sentence is not an image prompt, and nothing pads it.
        var response =
            $$"""{"prompts":[{"section":"Hero","prompt":"{{ScriptedBody.Scene("the hero")}}"},{"section":"Section 1","prompt":"a nice picture"},{"section":"Section 2","prompt":"{{ScriptedBody.Scene("the details")}}"}]}""";
        var service = Build(new ScriptedProvider(response));

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() =>
            service.GenerateSectionImagePromptsAsync(
                "pillar", "Test Title", TwoSectionDocumentJson(), null, ContentGeneratorProvider.OpenAi, CancellationToken.None));

        Assert.Equal(ContentGenerationFailureKind.UnusableReply, ex.Kind);
        Assert.Contains("the section 'Overview'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("3 words", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Not attaching a partial set", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWarningIsAddedToAShortPiecesOwnWarnings()
    {
        var withNone = GccGenerateService.AppendWarning("""{"subject":"Hi"}""", "first");
        var withOne = GccGenerateService.AppendWarning(withNone, "second");

        Assert.Equal(["first", "second"], GccGenerationCoordinator.WarningsOf(withOne));
        using var doc = JsonDocument.Parse(withOne);
        Assert.Equal("Hi", doc.RootElement.GetProperty("subject").GetString());
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

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() =>
            service.GenerateSectionImagePromptsAsync(
                "pillar", "Test Title", TwoSectionDocumentJson(), null,
                ContentGeneratorProvider.OpenAi, CancellationToken.None));

        Assert.Equal(ContentGenerationFailureKind.UnusableReply, ex.Kind);
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

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() =>
            service.GenerateSectionImagePromptsAsync(
                "pillar", "Test Title", TwoSectionDocumentJson(), null, ContentGeneratorProvider.OpenAi, CancellationToken.None));

        Assert.Equal(ContentGenerationFailureKind.UnusableReply, ex.Kind);
        Assert.Contains("the reply carried no prompts", ex.Message, StringComparison.Ordinal);
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
