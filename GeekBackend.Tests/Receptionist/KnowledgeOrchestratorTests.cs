using System.Diagnostics;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Receptionist.Knowledge;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// The plan the orchestrator runs, and the fact that it is the only thing making decisions. Each worker here is the real class over a
/// scripted model, so what is being tested is which worker is called, with what, how often, and what the orchestrator does with each of
/// the three things a worker can report. The two retries are the point: one more search when a question was too narrow, one more draft
/// when the grounding check rejected an overreach, each once and each ending at a refusal rather than at a salvaged answer.
/// </summary>
public sealed class KnowledgeOrchestratorTests
{
    // Distinctive on purpose. The zero-retention assertion at the bottom looks for every one of them in every line any of these five
    // loggers wrote, so a leak of the caller's words, the question, a passage or a draft fails a test rather than reaching a log.
    private const string Raw = "yeah hi, do you do the thing with the pipes under the sink";
    private const string Formulated = "Does this business repair garbage disposals?";
    private const string Broader = "Does this business do plumbing work at all?";
    private const string PageTitle = "Disposal repair";
    private const string PageText = "We clear a jammed grinder on the same visit.";
    private const string Draft = "Yes, they can sort that out on the same visit.";
    private const string Rewritten = "Yes, and the price is on their published list.";

    private static readonly string[] Sentinels =
    [
        Raw, Formulated, Broader, PageTitle, PageText, Draft, Rewritten,
        "under the sink", "garbage disposals", "plumbing work", "jammed grinder", "sort that out", "published list",
    ];

    private static readonly Guid Run = Guid.Parse("4f0c2d16-9a3b-4c8e-9f21-7b6d5a4e3c21");

    private static readonly IReadOnlyList<ReceptionistTurn> Turns = [new(ReceptionistSpeaker.Caller, Raw)];

    // What each worker's model answers with, in the shape that worker parses.
    private static string Formulates(string question) => $$"""{"question": "{{question}}"}""";
    private static string Writes(string answer) => $$"""{"answer": "{{answer}}"}""";
    private const string NothingToLookUp = """{"question": null}""";
    private const string PassagesDoNotAnswerIt = """{"answer": null}""";
    private const string KeepTheOne = """{"keep": [0]}""";
    private const string KeepNothing = """{"keep": []}""";
    private const string Supported = """{"supported": true}""";
    private const string NotSupported = """{"supported": false}""";

    /// <summary>An answer in no shape any worker can read, which is how a worker reports that it could not do its job.</summary>
    private const string Unreadable = "I am not really sure what to make of that one.";

    /// <summary>Every line the orchestrator and its four workers logged, in one place, so the zero-retention assertion reads all of them.</summary>
    private sealed class CapturingLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
    }

    /// <summary>
    /// One worker's model, answering from a script: call n gets answer n, and the last answer repeats. It keeps what it was asked, so a
    /// test can see that the second formulation was told what the first one tried, and that the rewrite was told what was rejected.
    /// </summary>
    private sealed class Model(Func<int, string, Task<ReceptionistReply>> reply) : IReceptionistModel
    {
        public List<string> Asked { get; } = [];

        public int Calls => Asked.Count;

        public bool IsConfigured => true;

        public Task<ReceptionistReply> ReplyAsync(
            string systemPrompt, IReadOnlyList<ReceptionistTurn> turns, IReadOnlyList<ReceptionistToolDefinition>? tools = null,
            CancellationToken ct = default)
        {
            Asked.Add(turns[^1].Text);
            ct.ThrowIfCancellationRequested();
            return reply(Asked.Count - 1, turns[^1].Text);
        }

        public Task<ReceptionistCallSummary> SummarizeAsync(IReadOnlyList<ReceptionistTurn> turns, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static Model Saying(params string[] answers) =>
        new((n, _) => Task.FromResult(new ReceptionistReply(answers[Math.Min(n, answers.Length - 1)], false)));

    /// <summary>A model slower than anyone waits for, deaf to cancellation: the provider this codebase refuses to trust with a budget.</summary>
    private static Model TooSlow(TimeSpan delay, string answer) =>
        new(async (_, _) =>
        {
            await Task.Delay(delay, CancellationToken.None);
            return new ReceptionistReply(answer, false);
        });

    /// <summary>
    /// A Library that answers from a script, keeps every search it was asked for, and can be made slow. Only the query-object overload
    /// is answered: a fake that quietly served the positional one would hide the day the orchestrator stopped sending a query.
    /// </summary>
    private sealed class FakeRag : IGeekCrawlerRagClient
    {
        public bool IsEnabled { get; set; } = true;

        public List<GeekCrawlerRagQueryResult?> Results { get; } = [];

        public List<GeekCrawlerRagQuery> Queries { get; } = [];

        /// <summary>How long the Library takes to answer. Honours the token it is given, which is the orchestrator's own attempt token.</summary>
        public TimeSpan Pause { get; set; } = TimeSpan.Zero;

        public async Task<GeekCrawlerRagQueryResult?> QueryAsync(GeekCrawlerRagQuery query, CancellationToken ct = default)
        {
            Queries.Add(query);
            if (Pause > TimeSpan.Zero) await Task.Delay(Pause, ct).ConfigureAwait(false);
            return Results[Math.Min(Queries.Count - 1, Results.Count - 1)];
        }

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need,
            Guid runId,
            string? crawlType = null,
            string? host = null,
            int topK = 8,
            bool? preferParent = null,
            bool? preferChild = null,
            IReadOnlyList<string>? entityNames = null,
            string? retrievalMode = null,
            IReadOnlyDictionary<string, string>? anchorToolLookup = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<GeekCrawlerRagTemplateIndexResult?> IndexTemplatesAsync(
            IReadOnlyList<GeekCrawlerRagTemplateDto> templates,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<GeekCrawlerRagTemplateQueryResult?> QueryTemplatesAsync(
            string need,
            int topK = 5,
            string? channel = null,
            IReadOnlyList<string>? entityTags = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<GeekCrawlerRagPageText?> GetPageTextAsync(
            string pageId,
            CancellationToken ct = default,
            string? runId = null) => throw new NotSupportedException();
    }

    /// <summary>The real four workers over scripted models, a scripted Library, and one list holding everything any of them logged.</summary>
    private sealed class Rig
    {
        public Rig(Model formulator, Model judge, Model composer, Model grounding)
        {
            Formulator = formulator;
            Judge = judge;
            Composer = composer;
            Grounding = grounding;
            Rag.Results.Add(Found(Page(PageTitle, PageText)));
            Orchestrator = new KnowledgeOrchestrator(
                new QuestionFormulator(formulator, new CapturingLogger<QuestionFormulator>(Lines)),
                new KnowledgePassageJudge(judge, new CapturingLogger<KnowledgePassageJudge>(Lines)),
                new AnswerComposer(composer, new CapturingLogger<AnswerComposer>(Lines)),
                new AnswerGrounding(grounding, new CapturingLogger<AnswerGrounding>(Lines)),
                new CapturingLogger<KnowledgeOrchestrator>(Lines));
        }

        public Model Formulator { get; }

        public Model Judge { get; }

        public Model Composer { get; }

        public Model Grounding { get; }

        public FakeRag Rag { get; } = new();

        public List<string> Lines { get; } = [];

        public KnowledgeOrchestrator Orchestrator { get; }

        public Task<KnowledgeAnswer> AnswerAsync(CancellationToken ct = default) =>
            Orchestrator.AnswerAsync(Turns, Run, Rag, ct);

        /// <summary>What the Library answers, in place of the one page every rig starts with.</summary>
        public Rig Finding(params GeekCrawlerRagQueryResult?[] results)
        {
            Rag.Results.Clear();
            Rag.Results.AddRange(results);
            return this;
        }
    }

    private static Rig Given(Model? formulator = null, Model? judge = null, Model? composer = null, Model? grounding = null) =>
        new(formulator ?? Saying(Formulates(Formulated)),
            judge ?? Saying(KeepTheOne),
            composer ?? Saying(Writes(Draft)),
            grounding ?? Saying(Supported));

    private static GccQuoteablePage Page(string title, params string[] paragraphs) =>
        new(Url: "https://example.test/" + Uri.EscapeDataString(title), Title: title, Headings: [], Paragraphs: paragraphs);

    private static GeekCrawlerRagQueryResult Found(params GccQuoteablePage[] pages) =>
        new() { RunId = Run, Pages = pages };

    private static GeekCrawlerRagQueryResult SearchFailed() =>
        new() { RunId = Run, Pages = [], Failed = true, Error = "RAG query unavailable.", Warning = "RAG query unavailable." };

    // ----- The happy path -----

    /// <summary>
    /// Every step reporting what it should: the question is worked out, the Library answers, the judge keeps a passage, an answer is
    /// written and the grounding check supports it. That answer, and nothing rewritten or summarised on the way out, is what is said.
    /// </summary>
    [Fact]
    public async Task A_question_the_business_published_an_answer_to_is_answered_in_the_composers_own_words()
    {
        var rig = Given();

        var result = await rig.AnswerAsync();

        Assert.True(result.Answered);
        Assert.Equal(Draft, result.Answer);
        Assert.Null(result.Refusal);
        Assert.Equal(1, rig.Formulator.Calls);
        Assert.Equal(1, rig.Composer.Calls);
        Assert.Equal(1, rig.Grounding.Calls);
        Assert.Single(rig.Rag.Queries);
    }

    /// <summary>
    /// The Library is asked the question the formulator wrote, not the words the caller said. Embedding what a caller actually says
    /// searches for their phrasing rather than their question, and a follow-up searches for nothing at all.
    /// </summary>
    [Fact]
    public async Task The_library_is_asked_the_formulated_question_and_never_the_callers_own_words()
    {
        var rig = Given();

        await rig.AnswerAsync();

        var query = Assert.Single(rig.Rag.Queries);
        Assert.Equal(Formulated, query.Need);
        Assert.DoesNotContain("under the sink", query.Need, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// How many passages a search asks for is the orchestrator's number, held on its own constant, not the Library client's default of
    /// eight. A caller is on the line and the judge reads every one of them.
    /// </summary>
    [Fact]
    public async Task A_search_asks_for_the_orchestrators_own_number_of_passages_from_this_businesss_own_run()
    {
        var rig = Given();

        await rig.AnswerAsync();

        var query = Assert.Single(rig.Rag.Queries);
        Assert.Equal(KnowledgeOrchestrator.TopK, query.TopK);
        Assert.Equal(Run, query.RunId);
    }

    // ----- The refusals -----

    /// <summary>
    /// A greeting is not a question, and the honest answer to it is that there is nothing to look up. Nothing is searched: a search for
    /// "hi there" costs a caller a round trip and can only come back with whatever is nearest.
    /// </summary>
    [Fact]
    public async Task A_caller_who_asked_nothing_is_nothing_asked_and_the_library_is_never_queried()
    {
        var rig = Given(formulator: Saying(NothingToLookUp));

        var result = await rig.AnswerAsync();

        Assert.False(result.Answered);
        Assert.Equal(AnswerRefusals.NothingAsked, result.Refusal);
        Assert.Empty(rig.Rag.Queries);
        Assert.Equal(0, rig.Composer.Calls);
    }

    /// <summary>
    /// A formulator that could not do its job is not a caller who asked nothing. Nothing was established either way, so the refusal is
    /// the one that means a step failed, and the Library is not searched for a question nobody worked out.
    /// </summary>
    [Fact]
    public async Task A_formulator_that_could_not_run_is_could_not_check_and_the_library_is_never_queried()
    {
        var rig = Given(formulator: Saying(Unreadable));

        var result = await rig.AnswerAsync();

        Assert.False(result.Answered);
        Assert.Equal(AnswerRefusals.CouldNotCheck, result.Refusal);
        Assert.NotEqual(AnswerRefusals.NothingAsked, result.Refusal);
        Assert.Empty(rig.Rag.Queries);
    }

    /// <summary>
    /// A Library that failed and a business that has published nothing are opposite facts. A failed search establishes nothing, so it is
    /// could-not-check, and it is never reported as the business not having published an answer.
    /// </summary>
    [Fact]
    public async Task A_library_that_failed_is_could_not_check_and_not_a_business_that_published_nothing()
    {
        var rig = Given().Finding(SearchFailed());

        var result = await rig.AnswerAsync();

        Assert.False(result.Answered);
        Assert.Equal(AnswerRefusals.CouldNotCheck, result.Refusal);
        Assert.NotEqual(AnswerRefusals.NotPublished, result.Refusal);
        Assert.Equal(0, rig.Composer.Calls);
    }

    /// <summary>
    /// A Library client answers null when it is disabled or went away mid-call. That is not an empty result and must not be read as one:
    /// nothing was searched, so nothing was established.
    /// </summary>
    [Fact]
    public async Task A_library_that_answered_nothing_at_all_is_could_not_check()
    {
        var rig = Given().Finding((GeekCrawlerRagQueryResult?)null);

        var result = await rig.AnswerAsync();

        Assert.False(result.Answered);
        Assert.Equal(AnswerRefusals.CouldNotCheck, result.Refusal);
    }

    /// <summary>
    /// Pages came back, the judge kept none of them, and the second, broader question could not be worked out. There is one more thing
    /// the orchestrator could have tried and it did not work, so the attempt ends at the business not having published an answer.
    /// </summary>
    [Fact]
    public async Task Passages_the_judge_keeps_none_of_with_no_broader_question_to_try_is_not_published()
    {
        var rig = Given(formulator: Saying(Formulates(Formulated), Unreadable), judge: Saying(KeepNothing));

        var result = await rig.AnswerAsync();

        Assert.False(result.Answered);
        Assert.Equal(AnswerRefusals.NotPublished, result.Refusal);
        Assert.Equal(2, rig.Formulator.Calls);
        Assert.Single(rig.Rag.Queries);
        Assert.Equal(0, rig.Composer.Calls);
    }

    /// <summary>
    /// The composer read the passages it was given and reported honestly that they do not answer the question. That is the business not
    /// having published it, and the grounding check is not run on an answer that was never written.
    /// </summary>
    [Fact]
    public async Task Passages_that_do_not_answer_the_question_are_not_published_rather_than_an_answer()
    {
        var rig = Given(composer: Saying(PassagesDoNotAnswerIt));

        var result = await rig.AnswerAsync();

        Assert.False(result.Answered);
        Assert.Equal(AnswerRefusals.NotPublished, result.Refusal);
        Assert.Equal(1, rig.Composer.Calls);
        Assert.Equal(0, rig.Grounding.Calls);
    }

    /// <summary>
    /// A composer that could not do its job establishes nothing about what the business published, so it is could-not-check and not
    /// not-published. Nothing is written a second time: the retry here exists for a rejected draft, not for a failed step.
    /// </summary>
    [Fact]
    public async Task A_composer_that_could_not_run_is_could_not_check()
    {
        var rig = Given(composer: Saying(Unreadable));

        var result = await rig.AnswerAsync();

        Assert.False(result.Answered);
        Assert.Equal(AnswerRefusals.CouldNotCheck, result.Refusal);
        Assert.NotEqual(AnswerRefusals.NotPublished, result.Refusal);
        Assert.Equal(1, rig.Composer.Calls);
    }

    /// <summary>
    /// The grounding check could not establish whether the passages say it. An answer nobody checked is exactly the answer this pipeline
    /// exists to stop, so the draft is discarded and the refusal says a step could not be run.
    /// </summary>
    [Fact]
    public async Task A_grounding_check_that_could_not_run_is_could_not_check_and_the_draft_is_discarded()
    {
        var rig = Given(grounding: Saying(Unreadable));

        var result = await rig.AnswerAsync();

        Assert.False(result.Answered);
        Assert.Null(result.Answer);
        Assert.Equal(AnswerRefusals.CouldNotCheck, result.Refusal);
        Assert.Equal(1, rig.Composer.Calls);
    }

    // ----- The two retries, each once, each for its own reason -----

    /// <summary>
    /// The commonest reason a search keeps nothing is a question narrower than anything the business wrote. So the formulator is asked
    /// again, told what the first question was, the Library is searched a second time with the different question it wrote, and that
    /// search can answer the caller. The first question is not searched twice.
    /// </summary>
    [Fact]
    public async Task A_search_that_keeps_nothing_is_asked_a_second_broader_way_and_can_answer_the_caller()
    {
        var rig = Given(
            formulator: Saying(Formulates(Formulated), Formulates(Broader)),
            judge: Saying(KeepNothing, KeepTheOne));

        var result = await rig.AnswerAsync();

        Assert.True(result.Answered);
        Assert.Equal(Draft, result.Answer);

        Assert.Equal(2, rig.Rag.Queries.Count);
        Assert.Equal(2, rig.Judge.Calls);
        Assert.Equal(Formulated, rig.Rag.Queries[0].Need);
        Assert.Equal(Broader, rig.Rag.Queries[1].Need);
        Assert.NotEqual(rig.Rag.Queries[0].Need, rig.Rag.Queries[1].Need);

        // The second formulation was told what the first one tried; the first was told only what the caller said.
        Assert.Equal(2, rig.Formulator.Calls);
        Assert.DoesNotContain(Formulated, rig.Formulator.Asked[0], StringComparison.Ordinal);
        Assert.Contains(Formulated, rig.Formulator.Asked[1], StringComparison.Ordinal);
        Assert.Contains("broader", rig.Formulator.Asked[1], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A second question that is the first question again is not a second search. Running it would spend a caller's time on the search
    /// that already came back with nothing, so the attempt ends at the business not having published an answer. Spelling it differently
    /// in capitals does not make it a different question either.
    /// </summary>
    [Theory]
    [InlineData(Formulated)]
    [InlineData("DOES THIS BUSINESS REPAIR GARBAGE DISPOSALS?")]
    public async Task A_broader_question_that_is_the_same_question_is_not_searched_a_second_time(string broader)
    {
        var rig = Given(
            formulator: Saying(Formulates(Formulated), Formulates(broader)),
            judge: Saying(KeepNothing));

        var result = await rig.AnswerAsync();

        Assert.False(result.Answered);
        Assert.Equal(AnswerRefusals.NotPublished, result.Refusal);
        Assert.Equal(2, rig.Formulator.Calls);
        Assert.Single(rig.Rag.Queries);
    }

    /// <summary>
    /// The commonest reason a draft is rejected is one sentence reaching past the passages, not a wrong retrieval. So the composer is
    /// asked again over the same passages, told what was rejected, and a second draft the grounding check supports is what is said.
    /// </summary>
    [Fact]
    public async Task A_draft_the_grounding_check_rejects_is_written_again_told_what_was_rejected()
    {
        var rig = Given(
            composer: Saying(Writes(Draft), Writes(Rewritten)),
            grounding: Saying(NotSupported, Supported));

        var result = await rig.AnswerAsync();

        Assert.True(result.Answered);
        Assert.Equal(Rewritten, result.Answer);
        Assert.NotEqual(Draft, result.Answer);

        Assert.Equal(2, rig.Composer.Calls);
        Assert.DoesNotContain(Draft, rig.Composer.Asked[0], StringComparison.Ordinal);
        Assert.Contains(Draft, rig.Composer.Asked[1], StringComparison.Ordinal);
        Assert.Contains("not supported", rig.Composer.Asked[1], StringComparison.OrdinalIgnoreCase);

        // Nothing is searched again: the retrieval was fine, the sentence was not.
        Assert.Single(rig.Rag.Queries);
    }

    /// <summary>
    /// The rewrite is a second attempt, not a loop. A second draft the grounding check also rejects ends the attempt at the refusal that
    /// says an answer was written and the business's own words did not support it, with exactly two drafts written and two checked.
    /// </summary>
    [Fact]
    public async Task A_second_draft_the_grounding_check_also_rejects_ends_at_unsupported_after_exactly_two_tries()
    {
        var rig = Given(
            composer: Saying(Writes(Draft), Writes(Rewritten)),
            grounding: Saying(NotSupported));

        var result = await rig.AnswerAsync();

        Assert.False(result.Answered);
        Assert.Null(result.Answer);
        Assert.Equal(AnswerRefusals.Unsupported, result.Refusal);
        Assert.Equal(2, rig.Composer.Calls);
        Assert.Equal(2, rig.Grounding.Calls);
    }

    /// <summary>
    /// The one thing that must never happen, on every path that could produce it: a draft the business's own words did not support is
    /// discarded, whether the check rejected it twice or could not be run at all. There is no path that salvages one.
    /// </summary>
    [Fact]
    public async Task An_unsupported_draft_is_never_what_the_caller_is_told()
    {
        string[][] verdicts =
        [
            [NotSupported],
            [NotSupported, Unreadable],
            [Unreadable],
            [NotSupported, PassagesDoNotAnswerIt],
        ];

        foreach (var script in verdicts)
        {
            var rig = Given(composer: Saying(Writes(Draft), Writes(Rewritten)), grounding: Saying(script));

            var result = await rig.AnswerAsync();

            Assert.False(result.Answered);
            Assert.Null(result.Answer);
            Assert.NotNull(result.Refusal);
            Assert.True(rig.Composer.Calls <= 2, "The composer was asked more than twice.");
        }
    }

    // ----- The bound, and the call ending -----

    /// <summary>
    /// A model that ignores the token it was handed cannot hold a caller on the line. The formulator here never answers inside the whole
    /// attempt's budget, and the attempt still ends well inside it with the refusal that says a step could not be run.
    /// </summary>
    [Fact]
    public async Task A_worker_that_ignores_cancellation_cannot_hold_a_caller_past_the_whole_budget()
    {
        var rig = Given(formulator: TooSlow(KnowledgeOrchestrator.Budget + TimeSpan.FromSeconds(5), Formulates(Formulated)));

        var started = Stopwatch.GetTimestamp();
        var result = await rig.AnswerAsync();
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.False(result.Answered);
        Assert.Equal(AnswerRefusals.CouldNotCheck, result.Refusal);
        Assert.True(elapsed < KnowledgeOrchestrator.Budget, $"The caller waited {elapsed} for an attempt bounded at {KnowledgeOrchestrator.Budget}.");
        Assert.Empty(rig.Rag.Queries);
    }

    /// <summary>
    /// The Library call has a budget of its own now. It used to be the one step without one, so a client that never came back held the
    /// caller until the whole attempt's bound expired - which is a bound doing a step's job, and nothing said which step was at fault.
    /// A search that does not return is cut at its own budget, reported as a step that could not be run, and named in the line.
    ///
    /// <para>
    /// That leaves the attempt's own <see cref="KnowledgeOrchestrator.Budget"/> as a true backstop: with every step bounded, no single
    /// slow step can reach it, and only the accumulation of a slow run and both retries can. That is deliberate, and it is why nothing
    /// here tries to provoke it - a test that did would be asserting the sum of six budgets rather than any behaviour of its own.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_search_that_does_not_come_back_is_cut_at_its_own_budget_and_is_could_not_check()
    {
        var rig = Given();
        rig.Rag.Pause = KnowledgeOrchestrator.SearchBudget + TimeSpan.FromSeconds(10);

        var started = Stopwatch.GetTimestamp();
        var result = await rig.AnswerAsync();
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.False(result.Answered);
        Assert.Equal(AnswerRefusals.CouldNotCheck, result.Refusal);
        Assert.True(elapsed < rig.Rag.Pause, $"The caller waited {elapsed}, which is the Library's own time rather than the budget.");
        Assert.True(elapsed < KnowledgeOrchestrator.Budget, $"The caller waited {elapsed}, which is the whole attempt doing one step's job.");
        Assert.Contains(rig.Lines, line => line.Contains("Searching the business's information took longer", StringComparison.Ordinal));
        Assert.Equal(0, rig.Composer.Calls);
    }

    /// <summary>
    /// The call ending is not a refusal. A caller who hung up is not a caller being told their question will be passed on, and returning
    /// a refusal for one would put a message in front of an operator that nobody asked for.
    /// </summary>
    [Fact]
    public async Task A_call_that_has_already_ended_throws_rather_than_refusing()
    {
        var rig = Given();
        using var ended = new CancellationTokenSource();
        await ended.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.AnswerAsync(ended.Token));

        Assert.Empty(rig.Rag.Queries);
    }

    // ----- Zero Data Retention -----

    /// <summary>
    /// Across an answer, every refusal and a failure: no line any of the five loggers wrote carries what the caller said, the question
    /// that was worked out from it, a passage the Library returned, or either draft. The attempt holds all of it; the log holds none.
    /// </summary>
    [Fact]
    public async Task No_log_line_carries_the_callers_words_the_question_a_passage_or_a_draft()
    {
        var answered = Given();
        Assert.True((await answered.AnswerAsync()).Answered);

        var nothingAsked = Given(formulator: Saying(NothingToLookUp));
        await nothingAsked.AnswerAsync();

        var formulatorFailed = Given(formulator: Saying(Unreadable));
        await formulatorFailed.AnswerAsync();

        var libraryFailed = Given().Finding(SearchFailed());
        await libraryFailed.AnswerAsync();

        var libraryGone = Given().Finding((GeekCrawlerRagQueryResult?)null);
        await libraryGone.AnswerAsync();

        var broadened = Given(
            formulator: Saying(Formulates(Formulated), Formulates(Broader)),
            judge: Saying(KeepNothing, KeepTheOne));
        await broadened.AnswerAsync();

        var notPublished = Given(formulator: Saying(Formulates(Formulated), Formulates(Formulated)), judge: Saying(KeepNothing));
        await notPublished.AnswerAsync();

        var composerHasNothing = Given(composer: Saying(PassagesDoNotAnswerIt));
        await composerHasNothing.AnswerAsync();

        var unsupported = Given(composer: Saying(Writes(Draft), Writes(Rewritten)), grounding: Saying(NotSupported));
        await unsupported.AnswerAsync();

        var rigs = new[]
        {
            answered, nothingAsked, formulatorFailed, libraryFailed, libraryGone, broadened, notPublished, composerHasNothing, unsupported,
        };

        foreach (var rig in rigs)
        {
            Assert.NotEmpty(rig.Lines);
            foreach (var line in rig.Lines)
            {
                foreach (var sentinel in Sentinels)
                {
                    Assert.DoesNotContain(sentinel, line, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }
}
