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
///
/// <para>
/// 2026-10-13: each question is written from the answer the operator entered for it on the Brief, in
/// the brief's voice, and no crawl is searched for it. From 2026-10-11 until then each was searched
/// for in every partner's crawl. A question with no answer entered, or that the writer did not write,
/// is left out and named; a request that fails loses its own questions; none of those refuses the
/// page.
/// </para>
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

        public string ModelFor(ChatCompletionRequest request) => "gpt-4.1";
        public List<string> SystemPromptsSeen { get; } = [];
        /// <summary>The questions each FAQ call was handed, in the order the calls were made.</summary>
        public List<IReadOnlyList<string>> FaqCallQuestions { get; } = [];
        /// <summary>When true, every FAQ call answers with an h2 that has no children.</summary>
        public bool AnswerFaqWithNoChildren { get; init; }
        /// <summary>A question the FAQ call returns no child for.</summary>
        public string? LeaveOut { get; init; }
        /// <summary>A question the FAQ call answers under other words: the question, and the heading used instead.</summary>
        public (string Question, string Heading)? Reword { get; init; }
        /// <summary>What every FAQ answer says.</summary>
        public string AnswerText { get; init; } = "Answer.";
        /// <summary>The user message of each FAQ call, in the order the calls were made.</summary>
        public List<string> FaqUserPromptsSeen { get; } = [];
        /// <summary>The system message of each FAQ call, in the order the calls were made.</summary>
        public List<string> FaqSystemPromptsSeen { get; } = [];
        /// <summary>The FAQ call, counted from 1, that fails instead of answering.</summary>
        public int? FailFaqCall { get; init; }
        /// <summary>What the failing FAQ call fails with: by default, a reply that ended in blanks at its limit.</summary>
        public Exception FaqFault { get; init; } = CutReply.EndingInBlanks();
        /// <summary>When set, what every FAQ call replies instead of a section.</summary>
        public string? FaqReply { get; init; }
        /// <summary>When set, what the opening call replies: the blog's opening is a lede alone, the pillar's a lede and an introduction.</summary>
        public string? LedeReply { get; init; }

        /// <summary>The literal that opens BuildArticleFaqSectionPrompt, and nothing else. The
        /// metadata prompt names "People Also Ask" too, when it tells the model to end the outline
        /// with one, so the bare phrase does not identify this call.</summary>
        internal const string FaqPromptMarker = """Write ONLY the "People Also Ask" FAQ section""";

        /// <summary>The same line of the same prompt, as the blog's FAQ call carries it.</summary>
        internal const string BlogFaqPromptMarker = """Write ONLY the "Frequently Asked Questions" FAQ section""";

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
            else if (system.Contains(FaqPromptMarker, StringComparison.Ordinal)
                || system.Contains(BlogFaqPromptMarker, StringComparison.Ordinal))
            {
                var user = request.Messages.First(m => m.Role == ChatRole.User).Content;
                FaqUserPromptsSeen.Add(user);
                FaqSystemPromptsSeen.Add(system);
                var questions = QuestionsIn(user);
                FaqCallQuestions.Add(questions);
                if (FailFaqCall == FaqCallQuestions.Count)
                    return Task.FromException<ChatCompletionResult>(FaqFault);

                content = FaqReply ?? (AnswerFaqWithNoChildren
                    ? SectionJson
                    : FaqAnswer(
                    [
                        .. questions
                            .Where(q => q != LeaveOut)
                            .Select(q => Reword is { } r && r.Question == q ? r.Heading : q),
                    ],
                    AnswerText));
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
                content = LedeReply ?? LedeAndIntroJson;
            }

            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
        }

        /// <summary>The "Q1: question" lines the request carries (GccFaqAnswers.Render), as questions.</summary>
        private static IReadOnlyList<string> QuestionsIn(string user) => user
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^Q\d+: "))
            .Select(l => l[(l.IndexOf(':') + 1)..].Trim())
            .ToList();

        /// <summary>The shape the FAQ prompt asks for: an h2 with one h3 child per question.</summary>
        private static string FaqAnswer(IReadOnlyList<string> questions, string answer)
        {
            var children = string.Join(",", questions.Select(q =>
                $$"""{"tag":"h3","heading":"{{q}}","paragraphs":[{"type":"text","runs":[{"text":"{{answer}}"}]}],"href":null,"children":[]}"""));
            return $$"""{"tag":"h2","heading":"People Also Ask","paragraphs":[],"href":null,"children":[{{children}}]}""";
        }
    }

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

    private static GccCreateDto Create(string? briefJson, string? researchJson = null) => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
        StartingContentType: "pillar", Topic: "AI implementation", Notes: null,
        ProjectSiteRunId: null, SiteSectionJson: null, BriefJson: briefJson, ResearchJson: researchJson,
        Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

    /// <summary>What the operator entered as the answer to <paramref name="question"/>, in these tests.</summary>
    private static string AnswerTo(string question) => $"The operator's note on: {question}";

    /// <summary>A brief whose People Also Ask holds every question with an answer entered for it.</summary>
    private static string Answered(IEnumerable<string> questions) => BriefOf(questions.Select(q => (q, (string?)AnswerTo(q))));

    /// <summary>A brief whose People Also Ask holds these rows, as the form saves them: a question and its answer, or no answer.</summary>
    private static string BriefOf(IEnumerable<(string Question, string? Answer)> rows) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            paaQuestions = rows.Select(row => new { question = row.Question, answer = row.Answer ?? string.Empty }),
        });

    private static Task<string> Pillar(RecordingProvider provider, string briefJson) =>
        Build(provider).GeneratePillarBodyAsync(
            Create(briefJson), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

    private static readonly string[] TwoQuestions = ["What is AI implementation?", "How much does it cost?"];

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

        await Pillar(provider, Answered(TwoQuestions));

        // Exactly one, which is the claim -- two PAA questions are one FAQ section, not two calls.
        Assert.Equal(
            1,
            provider.SystemPromptsSeen.Count(
                p => p.Contains(RecordingProvider.FaqPromptMarker, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheFaqSectionActuallyReachesTheGeneratedDocument()
    {
        string[] questions = ["What is AI implementation?"];

        var json = await Pillar(new RecordingProvider(), Answered(questions));

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
        var questions = Enumerable.Range(1, 20).Select(i => $"Question number {i}?").ToList();

        var json = await Pillar(provider, Answered(questions));

        Assert.Equal([8, 8, 4], provider.FaqCallQuestions.Select(q => q.Count).ToArray());
        Assert.Equal(questions, provider.FaqCallQuestions.SelectMany(q => q).ToList());
        // One People Also Ask section carrying every answer, in order: the position of each heading
        // in the saved document rises with its number.
        var positions = questions.Select(q => json.IndexOf($"\"heading\":\"{q}\"", StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p).ToList(), positions);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"heading\":\"People Also Ask\""));
    }

    /// <summary>
    /// A reply is limited to 3,072 tokens and an answer is written about as long as the one entered,
    /// so long answers go in more requests of fewer questions. None is left out and none is divided.
    /// </summary>
    [Fact]
    public async Task LongAnswersAreSpreadOverMoreRequestsAndNoneIsLeftOut()
    {
        var provider = new RecordingProvider();
        var questions = Enumerable.Range(1, 5).Select(i => $"Question number {i}?").ToList();
        var long_ = new string('a', GccFaqAnswers.MaxAnswerCharsPerCall / 2 - 10) + ".";
        var brief = BriefOf(questions.Select(q => (q, (string?)long_)));

        await Pillar(provider, brief);

        Assert.Equal([2, 2, 1], provider.FaqCallQuestions.Select(q => q.Count).ToArray());
        Assert.Equal(questions, provider.FaqCallQuestions.SelectMany(q => q).ToList());
    }

    private static IReadOnlyList<string> WarningsIn(string json) =>
    [
        .. System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("warnings")
            .EnumerateArray().Select(w => w.GetString() ?? string.Empty),
    ];

    private static string NotWritten(string question) =>
        $"the pillar's People Also Ask: the writer was sent the answer entered for \"{question}\" and did not write it; "
        + "it was left out.";

    [Fact]
    public async Task TheRequestCarriesEachQuestionWithTheAnswerEnteredForItAndIsToldToAddNothing()
    {
        var provider = new RecordingProvider();

        await Pillar(provider, Answered(TwoQuestions));

        var system = Assert.Single(provider.FaqSystemPromptsSeen);
        Assert.Contains(ContentPromptBuilder.OperatorAnswersInstruction, system, StringComparison.Ordinal);
        Assert.Contains("Write a child for EVERY question", system, StringComparison.Ordinal);
        Assert.Contains("add no fact, capability, price or figure it does not state", system, StringComparison.Ordinal);
        Assert.Contains("in the voice described above", system, StringComparison.Ordinal);
        // No passage is shown and nothing tells the writer to decide which questions it can answer.
        Assert.DoesNotContain("PARTNER EVIDENCE", system, StringComparison.Ordinal);
        Assert.DoesNotContain("LEAVE THAT QUESTION OUT", system, StringComparison.Ordinal);

        var user = Assert.Single(provider.FaqUserPromptsSeen);
        Assert.Contains(ContentPromptBuilder.OperatorAnswersHeading, user, StringComparison.Ordinal);
        // Each answer sits under its own question, in the order entered.
        var q1 = user.IndexOf($"Q1: {TwoQuestions[0]}", StringComparison.Ordinal);
        var q2 = user.IndexOf($"Q2: {TwoQuestions[1]}", StringComparison.Ordinal);
        Assert.True(q1 >= 0 && q2 > q1);
        Assert.Contains(
            "The operator's answer to Q1:" + Environment.NewLine + AnswerTo(TwoQuestions[0]), user[q1..q2], StringComparison.Ordinal);
        Assert.Contains(
            "The operator's answer to Q2:" + Environment.NewLine + AnswerTo(TwoQuestions[1]), user[q2..], StringComparison.Ordinal);
        Assert.DoesNotContain("PARTNER EVIDENCE", user, StringComparison.Ordinal);
        Assert.DoesNotContain("Found for Q", user, StringComparison.Ordinal);
    }

    /// <summary>
    /// A question with no answer entered has nothing to be written from. Nothing is sent for it, it is
    /// never answered from the writer's own knowledge, and one line names it with where its answer goes.
    /// </summary>
    [Fact]
    public async Task AQuestionWithNoAnswerEnteredIsNotSentAndIsNamedWithWhereItsAnswerGoes()
    {
        var provider = new RecordingProvider();
        var brief = BriefOf([(TwoQuestions[0], AnswerTo(TwoQuestions[0])), (TwoQuestions[1], null)]);

        var json = await Pillar(provider, brief);

        Assert.Equal([TwoQuestions[0]], Assert.Single(provider.FaqCallQuestions));
        Assert.Contains($"\"heading\":\"{TwoQuestions[0]}\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain($"\"heading\":\"{TwoQuestions[1]}\"", json, StringComparison.Ordinal);
        Assert.Contains(
            $"the pillar's People Also Ask: no answer is entered for \"{TwoQuestions[1]}\", and the question was left out. "
            + "Its answer goes beside it under \"People Also Ask (direct entry, optional)\" on the Brief.",
            WarningsIn(json));
    }

    /// <summary>
    /// A brief saved before 2026-10-13 holds its questions as lines with no answers. None is sent,
    /// the page ships without the section, and the questions are named together in one line.
    /// </summary>
    [Theory]
    [InlineData("""{"paaQuestions":"What is AI implementation?\nHow much does it cost?"}""")]
    [InlineData("""{"paaQuestions":["What is AI implementation?","How much does it cost?"]}""")]
    public async Task QuestionsSavedWithoutAnswersAreNotSentAndAreNamedInOneLine(string brief)
    {
        var provider = new RecordingProvider();

        var json = await Pillar(provider, brief);

        Assert.Empty(provider.FaqCallQuestions);
        Assert.DoesNotContain("\"heading\":\"People Also Ask\"", json, StringComparison.Ordinal);
        var line = Assert.Single(WarningsIn(json), w => w.StartsWith("the pillar's People Also Ask:", StringComparison.Ordinal));
        Assert.Equal(
            "the pillar's People Also Ask: no answer is entered for 2 questions, and all 2 were left out. "
            + "Their answers go beside them under \"People Also Ask (direct entry, optional)\" on the Brief. "
            + "The questions: \"What is AI implementation?\"; \"How much does it cost?\".",
            line);
    }

    /// <summary>
    /// A request that writes none of its questions does not refuse the pillar (until 2026-10-11 it
    /// did). The page ships without the section and each question is named.
    /// </summary>
    [Fact]
    public async Task AFaqCallThatWritesNothingShipsThePillarWithoutTheSectionAndNamesEachQuestion()
    {
        var provider = new RecordingProvider { AnswerFaqWithNoChildren = true };

        var json = await Pillar(provider, Answered(TwoQuestions));

        Assert.DoesNotContain("\"heading\":\"People Also Ask\"", json, StringComparison.Ordinal);
        var warnings = WarningsIn(json);
        Assert.All(TwoQuestions, q => Assert.Contains(NotWritten(q), warnings));
    }

    /// <summary>
    /// 2026-10-10: a call that answered seven of its eight questions shipped the seven with no word
    /// that one was missing. The page still ships; the question is named.
    /// </summary>
    [Fact]
    public async Task AFaqCallThatWritesSevenOfEightShipsTheSevenAndNamesTheEighth()
    {
        var questions = Enumerable.Range(1, 8).Select(i => $"Question number {i}?").ToList();
        var provider = new RecordingProvider { LeaveOut = "Question number 5?" };

        var json = await Pillar(provider, Answered(questions));

        foreach (var answered in questions.Where(q => q != "Question number 5?"))
        {
            Assert.Contains($"\"heading\":\"{answered}\"", json, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("\"heading\":\"Question number 5?\"", json, StringComparison.Ordinal);
        var named = Assert.Single(WarningsIn(json), w => w.Contains("Question number 5?", StringComparison.Ordinal));
        Assert.Equal(NotWritten("Question number 5?"), named);
        Assert.DoesNotContain(WarningsIn(json), w => w.Contains("Question number 4?", StringComparison.Ordinal));
    }

    /// <summary>
    /// An answer under a heading that is none of the questions sent is dropped, as the tool page drops
    /// it. Until 2026-10-11 it stayed on the pillar.
    /// </summary>
    [Fact]
    public async Task AnAnswerUnderOtherWordsIsDroppedAndBothHalvesAreNamed()
    {
        var provider = new RecordingProvider
        {
            Reword = ("How much does it cost?", "Pricing for a typical engagement"),
        };

        var json = await Pillar(provider, Answered(TwoQuestions));

        Assert.DoesNotContain("\"heading\":\"Pricing for a typical engagement\"", json, StringComparison.Ordinal);
        Assert.Contains("\"heading\":\"What is AI implementation?\"", json, StringComparison.Ordinal);
        var warnings = WarningsIn(json);
        Assert.Contains(NotWritten("How much does it cost?"), warnings);
        Assert.Contains(
            "the pillar's People Also Ask: the writer answered under \"Pricing for a typical engagement\", "
            + "which is not a question it was sent; the answer was dropped.",
            warnings);
    }

    [Fact]
    public async Task AFaqThatWritesEveryQuestionReportsNothing()
    {
        var json = await Pillar(new RecordingProvider(), Answered(TwoQuestions));

        Assert.DoesNotContain(WarningsIn(json), w => w.Contains("People Also Ask", StringComparison.Ordinal));
    }

    /// <summary>
    /// The 13:16 UTC run of 2026-10-13 lost its blog to this: the reply to the questions request ended
    /// in 2,913 tabs and reached its limit, the failure was thrown out of the questions writer, and the
    /// whole page was refused. The page is kept, its questions are left out, and one line says why.
    /// </summary>
    [Fact]
    public async Task AFaqRequestCutAtItsLimitShipsThePillarWithoutItsQuestionsAndSaysWhyInOneLine()
    {
        var provider = new RecordingProvider { FailFaqCall = 1 };

        var json = await Pillar(provider, Answered(TwoQuestions));

        Assert.DoesNotContain("\"heading\":\"People Also Ask\"", json, StringComparison.Ordinal);
        var line = Assert.Single(WarningsIn(json), w => w.Contains("People Also Ask", StringComparison.Ordinal));
        Assert.Equal(
            GccGenerateService.FaqRequestFailed(
                "the pillar's People Also Ask", TwoQuestions, CutReply.EndingInBlanks().Message),
            line);
        Assert.StartsWith(
            "the pillar's People Also Ask: the request that answers 2 questions failed, and all 2 were left out. "
            + "OpenAI stopped at the output limit of 3072 tokens before finishing; the response is incomplete. "
            + "Its last 2,913 characters are blank",
            line,
            StringComparison.Ordinal);
        Assert.All(TwoQuestions, q => Assert.Contains($"\"{q}\"", line, StringComparison.Ordinal));
    }

    /// <summary>
    /// The page that run lost, through its own writer: the blog's questions go through the same
    /// method as the pillar's, and the blog is kept the same way.
    /// </summary>
    [Fact]
    public async Task AFaqRequestCutAtItsLimitShipsTheBlogWithoutItsQuestionsAndSaysWhyInOneLine()
    {
        var provider = new RecordingProvider
        {
            FailFaqCall = 1,
            LedeReply = """{"ledeType":"summary","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}]}""",
        };
        var brief = System.Text.Json.JsonSerializer.Serialize(new
        {
            blogFaqQuestions = TwoQuestions.Select(q => new { question = q, answer = AnswerTo(q) }),
        });

        var json = await Build(provider).GenerateBlogBodyAsync(
            Create(brief) with { StartingContentType = "blog" },
            null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        Assert.Single(provider.FaqCallQuestions);
        Assert.DoesNotContain("\"heading\":\"Frequently Asked Questions\"", json, StringComparison.Ordinal);
        var line = Assert.Single(WarningsIn(json), w => w.Contains("the request that answers", StringComparison.Ordinal));
        Assert.Contains("failed, and all 2 were left out.", line, StringComparison.Ordinal);
        Assert.Contains("Its last 2,913 characters are blank", line, StringComparison.Ordinal);
        Assert.All(TwoQuestions, q => Assert.Contains($"\"{q}\"", line, StringComparison.Ordinal));
    }

    /// <summary>The blog's questions are written from the answers entered under "Blog FAQ (optional)", and a question with none names that box.</summary>
    [Fact]
    public async Task TheBlogWritesItsQuestionsFromTheAnswersEnteredAndNamesItsOwnBoxForOneWithNone()
    {
        var provider = new RecordingProvider
        {
            LedeReply = """{"ledeType":"summary","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}]}""",
        };
        var brief = System.Text.Json.JsonSerializer.Serialize(new
        {
            blogFaqQuestions = new[]
            {
                new { question = TwoQuestions[0], answer = AnswerTo(TwoQuestions[0]) },
                new { question = TwoQuestions[1], answer = string.Empty },
            },
        });

        var json = await Build(provider).GenerateBlogBodyAsync(
            Create(brief) with { StartingContentType = "blog" },
            null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        Assert.Equal([TwoQuestions[0]], Assert.Single(provider.FaqCallQuestions));
        Assert.Contains(AnswerTo(TwoQuestions[0]), Assert.Single(provider.FaqUserPromptsSeen), StringComparison.Ordinal);
        Assert.Contains($"\"heading\":\"{TwoQuestions[0]}\"", json, StringComparison.Ordinal);
        Assert.Contains(
            $"the blog's FAQ: no answer is entered for \"{TwoQuestions[1]}\", and the question was left out. "
            + "Its answer goes beside it under \"Blog FAQ (optional)\" on the Brief.",
            WarningsIn(json));
    }

    /// <summary>A failed request loses its own questions. The request after it is still sent and its answers are kept.</summary>
    [Fact]
    public async Task AFailedFaqRequestLosesItsOwnQuestionsAndTheNextRequestIsStillSent()
    {
        var questions = Enumerable.Range(1, 9).Select(i => $"Question number {i}?").ToList();
        var provider = new RecordingProvider { FailFaqCall = 1 };

        var json = await Pillar(provider, Answered(questions));

        Assert.Equal(2, provider.FaqCallQuestions.Count);
        Assert.Contains("\"heading\":\"Question number 9?\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"heading\":\"Question number 1?\"", json, StringComparison.Ordinal);
        // By the section's own label: with the section on the page, the keyword's line names it too.
        var line = Assert.Single(
            WarningsIn(json), w => w.StartsWith("the pillar's People Also Ask:", StringComparison.Ordinal));
        Assert.Contains("the request that answers 8 questions failed, and all 8 were left out.", line, StringComparison.Ordinal);
        Assert.Contains("\"Question number 8?\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Question number 9?", line, StringComparison.Ordinal);
    }

    /// <summary>A reply that is no section is the same failure: nothing of it can be kept, and the page is.</summary>
    [Fact]
    public async Task AFaqReplyThatCannotBeReadShipsThePillarAndNamesItsQuestions()
    {
        var provider = new RecordingProvider { FaqReply = "These questions cannot be answered." };

        var json = await Pillar(provider, Answered(TwoQuestions));

        Assert.DoesNotContain("\"heading\":\"People Also Ask\"", json, StringComparison.Ordinal);
        var line = Assert.Single(WarningsIn(json), w => w.Contains("People Also Ask", StringComparison.Ordinal));
        Assert.StartsWith(
            "the pillar's People Also Ask: the request that answers 2 questions failed, and all 2 were left out. "
            + "Model did not return a valid structured section",
            line,
            StringComparison.Ordinal);
    }

    /// <summary>A run that is stopped is stopped. It is not a gap in a questions section.</summary>
    [Fact]
    public async Task ARunStoppedDuringTheFaqRequestIsStillStopped()
    {
        var provider = new RecordingProvider { FailFaqCall = 1, FaqFault = new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Pillar(provider, Answered(TwoQuestions)));
    }

    [Fact]
    public void A_failed_request_for_one_question_is_one_sentence_naming_it()
    {
        Assert.Equal(
            "FAQ: the request that answers \"Does it sync?\" failed, and the question was left out. The reply was cut.",
            GccGenerateService.FaqRequestFailed("FAQ", ["Does it sync?"], " The reply was cut. "));
    }

    /// <summary>
    /// The page's figure check reads the Brief whole as the operator's own statements, and the answers
    /// are entered on the Brief. So a figure the operator's answer states is one the written answer may
    /// state, and nothing has to be added to what the check reads.
    /// </summary>
    [Fact]
    public async Task AFigureTheOperatorsAnswerStatesDoesNotRefuseThePage()
    {
        var brief = BriefOf([("How fast are invoices paid?", "Customers are paid within 45 minutes of approval.")]);
        var provider = new RecordingProvider { AnswerText = "Invoices are paid within 45 minutes of approval." };

        var json = await Pillar(provider, brief);

        Assert.Contains("within 45 minutes", json, StringComparison.Ordinal);
    }

    /// <summary>The writer is told to add no figure. One it adds anyway is caught by the page's check, as any figure is.</summary>
    [Fact]
    public async Task AFigureTheWriterAddsToAnAnswerStillRefusesThePage()
    {
        var brief = BriefOf([("How fast are invoices paid?", "Customers are paid soon after approval.")]);
        var provider = new RecordingProvider { AnswerText = "Invoices are paid within 45 minutes of approval." };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Pillar(provider, brief));
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
