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
        /// <summary>A question the FAQ call returns no child for.</summary>
        public string? LeaveOut { get; init; }
        /// <summary>A question the FAQ call answers under other words: the question, and the heading used instead.</summary>
        public (string Question, string Heading)? Reword { get; init; }

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
                content = AnswerFaqWithNoChildren
                    ? SectionJson
                    : FaqAnswer(
                    [
                        .. questions
                            .Where(q => q != LeaveOut)
                            .Select(q => Reword is { } r && r.Question == q ? r.Heading : q),
                    ]);
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
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"heading\":\"People Also Ask\""));
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

    private static IReadOnlyList<string> WarningsIn(string json) =>
    [
        .. System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("warnings")
            .EnumerateArray().Select(w => w.GetString() ?? string.Empty),
    ];

    /// <summary>
    /// 2026-10-10: a call that answered seven of its eight questions shipped the seven with no word
    /// that one was missing. The summary on <c>WriteFaqInBatchesAsync</c> called a missing answer a
    /// refusal and the code refused only a call that answered nothing. The page still ships; the
    /// question is named.
    /// </summary>
    [Fact]
    public async Task AFaqCallThatAnswersSevenOfEightShipsTheSevenAndNamesTheEighth()
    {
        var questions = Enumerable.Range(1, 8).Select(i => $"Question number {i}?").ToList();
        var provider = new RecordingProvider { LeaveOut = "Question number 5?" };
        var brief = System.Text.Json.JsonSerializer.Serialize(new { paaQuestions = questions });

        var json = await Build(provider).GeneratePillarBodyAsync(
            Create(brief), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        foreach (var answered in questions.Where(q => q != "Question number 5?"))
        {
            Assert.Contains($"\"heading\":\"{answered}\"", json, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("\"heading\":\"Question number 5?\"", json, StringComparison.Ordinal);
        var named = Assert.Single(WarningsIn(json), w => w.Contains("Question number 5?", StringComparison.Ordinal));
        Assert.Equal("the pillar's People Also Ask: no answer came back under \"Question number 5?\".", named);
        Assert.DoesNotContain(WarningsIn(json), w => w.Contains("Question number 4?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAnswerUnderOtherWordsStaysOnThePageAndBothHalvesAreNamed()
    {
        var provider = new RecordingProvider
        {
            Reword = ("How much does it cost?", "Pricing for a typical engagement"),
        };
        var brief = """{"paaQuestions":["What is AI implementation?","How much does it cost?"]}""";

        var json = await Build(provider).GeneratePillarBodyAsync(
            Create(brief), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        // What ships is what shipped before: every child the call returned.
        Assert.Contains("\"heading\":\"Pricing for a typical engagement\"", json, StringComparison.Ordinal);
        var warnings = WarningsIn(json);
        Assert.Contains("the pillar's People Also Ask: no answer came back under \"How much does it cost?\".", warnings);
        Assert.Contains(
            "the pillar's People Also Ask: the writer answered under \"Pricing for a typical engagement\", "
            + "which is not a question it was sent.",
            warnings);
    }

    [Fact]
    public async Task AFaqThatAnswersEveryQuestionReportsNothing()
    {
        var provider = new RecordingProvider();
        var brief = """{"paaQuestions":["What is AI implementation?","How much does it cost?"]}""";

        var json = await Build(provider).GeneratePillarBodyAsync(
            Create(brief), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        Assert.DoesNotContain(WarningsIn(json), w => w.Contains("People Also Ask", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("How much does it cost?", "How much does it cost?")]
    // A heading the writer tightened, or wrapped the question in: one wording inside the other.
    [InlineData("How much does it cost", "How much does it cost?")]
    [InlineData("Pricing: how much does it cost?", "How much does it cost?")]
    // Pasted questions carry these; each is one space between two words.
    [InlineData("How much does it cost?", "How  much does it cost?")]
    [InlineData("How much does it cost?", "How\tmuch does it cost?")]
    [InlineData("How much does it cost?", "How much does it cost?")]
    [InlineData("How much does it cost?", "  How much does it cost?  ")]
    public void A_heading_answers_a_question_whatever_the_spacing_between_its_words(string heading, string question)
    {
        Assert.True(GccGenerateService.AnswersQuestion(heading, question));
    }

    [Theory]
    [InlineData("What does it cost each month?", "How much does it cost?")]
    [InlineData("", "How much does it cost?")]
    [InlineData("How much does it cost?", "  \t")]
    public void A_heading_in_other_words_or_an_empty_one_answers_nothing(string heading, string question)
    {
        Assert.False(GccGenerateService.AnswersQuestion(heading, question));
    }

    [Fact]
    public void A_question_is_normalized_to_its_words_with_one_space_between_them()
    {
        Assert.Equal(
            "how do past due invoices move the forecast",
            GccGenerateService.NormalizeQuestion("  How do past due\tinvoices   move -- the forecast?  "));
        // A hyphen joins, as it always has: the same on both sides of the comparison.
        Assert.Equal("pastdue", GccGenerateService.NormalizeQuestion("Past-due"));
    }
}
