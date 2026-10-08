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
        /// <summary>The questions each FAQ call was handed, in the order the calls were made.</summary>
        public List<IReadOnlyList<string>> FaqCallQuestions { get; } = [];
        /// <summary>When true, every FAQ call answers with an h2 that has no children.</summary>
        public bool AnswerFaqWithNoChildren { get; init; }

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
            string content;
            if (request.JsonSchemaName == "sections")
            {
                content = bodyCalls < 1 ? SectionsArrayJson : ScriptedBody.PlannedBatch(bodyCalls);
                bodyCalls++;
            }
            else if (system.Contains(FaqPromptMarker, StringComparison.Ordinal))
            {
                var user = request.Messages.First(m => m.Role == ChatRole.User).Content;
                var questions = QuestionsIn(user);
                FaqCallQuestions.Add(questions);
                content = AnswerFaqWithNoChildren ? SectionJson : FaqAnswer(questions);
            }
            else if (system.Contains("image-generation prompts", StringComparison.Ordinal))
            {
                content = ImagePromptsJson;
            }
            else if (system.Contains("sectionOutline", StringComparison.Ordinal))
            {
                content = ArticleMetadataJson;
            }
            else
            {
                content = LedeAndIntroJson;
            }

            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
        }

        /// <summary>The "  - Q1: question" lines BuildArticleFaqSectionPrompt writes, as questions.</summary>
        private static IReadOnlyList<string> QuestionsIn(string user) => user
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- Q", StringComparison.Ordinal))
            .Select(l => l[(l.IndexOf(':') + 1)..].Trim())
            .ToList();

        /// <summary>The shape the FAQ prompt asks for: an h2 with one h3 child per question.</summary>
        private static string FaqAnswer(IReadOnlyList<string> questions)
        {
            var children = string.Join(",", questions.Select(q =>
                $$"""{"tag":"h3","heading":"{{q}}","paragraphs":[{"type":"text","runs":[{"text":"Answer."}]}],"href":null,"children":[]}"""));
            return $$"""{"tag":"h2","heading":"People Also Ask","paragraphs":[],"href":null,"children":[{{children}}]}""";
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

        // The question is the h3's heading -- confirm it landed as a real section in the persisted
        // document, not just that a call happened.
        Assert.Contains("\"heading\":\"What is AI implementation?\"", json);
    }

    /// <summary>
    /// 2026-10-08: a brief with 37 People Also Ask questions went to one FAQ call with a 3,072-token
    /// budget, OpenAI stopped at the limit, and the whole pillar was refused for its appendix. The
    /// questions are now written in calls of eight and joined into one section, in the brief's order.
    /// </summary>
    [Fact]
    public async Task ManyPaaQuestionsAreWrittenInCallsOfEightAndJoinedInOrder()
    {
        var provider = new RecordingProvider();
        var service = Build(provider);
        var questions = Enumerable.Range(1, 20).Select(i => $"Question number {i}?").ToList();
        var brief = System.Text.Json.JsonSerializer.Serialize(new { paaQuestions = questions });

        var json = await service.GeneratePillarBodyAsync(Create(brief), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        Assert.Equal([8, 8, 4], provider.FaqCallQuestions.Select(q => q.Count).ToArray());
        Assert.Equal(questions, provider.FaqCallQuestions.SelectMany(q => q).ToList());
        // One People Also Ask section carrying every answer, in order: the position of each heading
        // in the saved document rises with its number.
        var positions = questions.Select(q => json.IndexOf($"\"heading\":\"{q}\"", StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p).ToList(), positions);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(json, "\"heading\":\"People Also Ask\"").Count);
    }

    [Fact]
    public async Task AFaqCallThatAnswersNothingRefusesThePillarNamingTheCall()
    {
        var provider = new RecordingProvider { AnswerFaqWithNoChildren = true };
        var service = Build(provider);
        var brief = """{"paaQuestions":["What is AI implementation?","How much does it cost?"]}""";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GeneratePillarBodyAsync(Create(brief), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None));

        Assert.Contains("People Also Ask call 1 answered none of its 2 questions", ex.Message);
    }
}
