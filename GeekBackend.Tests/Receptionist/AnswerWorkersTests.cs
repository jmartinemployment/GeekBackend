using GeekAPI.Services.Receptionist.Knowledge;
using GeekApplication.Interfaces.Receptionist;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Receptionist;

/// <summary>Every line a worker logged, so the zero-retention assertions have something to read.</summary>
internal sealed class AnswerWorkerLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add(formatter(state, exception));
}

/// <summary>
/// The model a worker calls, recording what it was asked and how often. The slow one delays on <see cref="CancellationToken.None"/> on
/// purpose: a worker that only stops because the provider honoured a token is not holding a budget of its own, and a caller is on the line.
/// </summary>
internal sealed class AnswerWorkerModel(Func<string, Task<ReceptionistReply>> reply) : IReceptionistModel
{
    public string? Asked { get; private set; }
    public int Calls { get; private set; }

    public bool IsConfigured => true;

    public Task<ReceptionistReply> ReplyAsync(
        string systemPrompt, IReadOnlyList<ReceptionistTurn> turns, IReadOnlyList<ReceptionistToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        Calls++;
        Asked = turns[^1].Text;
        ct.ThrowIfCancellationRequested();
        return reply(Asked);
    }

    public Task<ReceptionistCallSummary> SummarizeAsync(IReadOnlyList<ReceptionistTurn> turns, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public static AnswerWorkerModel Answering(string text) => new(_ => Task.FromResult(new ReceptionistReply(text, false)));

    public static AnswerWorkerModel Throwing() => new(_ => throw new HttpRequestException("the provider is down"));

    public static AnswerWorkerModel SlowerThan(TimeSpan budget, string text) => new(async _ =>
    {
        await Task.Delay(budget + TimeSpan.FromSeconds(5), CancellationToken.None);
        return new ReceptionistReply(text, false);
    });
}

/// <summary>
/// Turning what a caller said into something worth looking up, and - the part that matters - which of the three things happened while
/// doing it. "There is honestly nothing to look up" and "I could not work out what to look up" are opposite facts about a call, and a
/// worker that returns the same answer for both leaves the orchestrator unable to tell a greeting from a broken model.
/// </summary>
public sealed class QuestionFormulatorTests
{
    private static (QuestionFormulator Formulator, AnswerWorkerLogger<QuestionFormulator> Log) Formulator(IReceptionistModel model)
    {
        var log = new AnswerWorkerLogger<QuestionFormulator>();
        return (new QuestionFormulator(model, log), log);
    }

    private static readonly IReadOnlyList<ReceptionistTurn> Said =
    [
        new(ReceptionistSpeaker.Caller, "My kitchen sink at Pelican Harbour is backing up."),
        new(ReceptionistSpeaker.Receptionist, "I can send someone out for that."),
        new(ReceptionistSpeaker.Caller, "And how much is that?"),
    ];

    [Fact]
    public async Task A_question_the_model_wrote_is_done_and_comes_back_trimmed()
    {
        var (formulator, log) = Formulator(AnswerWorkerModel.Answering("""{"question": "   What does clearing a blocked kitchen drain cost?   "}"""));

        var result = await formulator.QuestionAsync(Said);

        Assert.Equal(AnswerStepOutcome.Done, result.Outcome);
        Assert.Equal("What does clearing a blocked kitchen drain cost?", result.Value);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task A_greeting_with_nothing_to_look_up_is_nothing_and_not_a_failure()
    {
        var (formulator, log) = Formulator(AnswerWorkerModel.Answering("""{"question": null}"""));

        var result = await formulator.QuestionAsync(Said);

        Assert.Equal(AnswerStepOutcome.Nothing, result.Outcome);
        Assert.Null(result.Value);
        Assert.Empty(log.Lines);
    }

    [Theory]
    [InlineData("I think they want a price for the drain.")]
    [InlineData("")]
    [InlineData("{ not json at all")]
    [InlineData("[\"a question\"]")]
    public async Task An_answer_that_is_not_a_json_object_is_a_failure_and_is_logged(string answer)
    {
        var (formulator, log) = Formulator(AnswerWorkerModel.Answering(answer));

        var result = await formulator.QuestionAsync(Said);

        Assert.Equal(AnswerStepOutcome.Failed, result.Outcome);
        Assert.Null(result.Value);
        Assert.Contains(log.Lines, line => line.Contains("could not be worked out", StringComparison.Ordinal));
    }

    /// <summary>
    /// A JSON object that is not an answer to the question asked is a failure, never nothing. Only an explicit null means "nothing to look
    /// up"; a different key, a number, or a string of spaces is the model not having done the job, and reading any of them as a greeting
    /// would hang up on a caller who asked something.
    /// </summary>
    [Theory]
    [InlineData("""{"q": "What does drain clearing cost?"}""")]
    [InlineData("""{"question": 7}""")]
    [InlineData("""{"question": "   "}""")]
    [InlineData("{}")]
    public async Task A_json_object_with_no_usable_question_is_a_failure_and_never_nothing(string answer)
    {
        var (formulator, _) = Formulator(AnswerWorkerModel.Answering(answer));

        var result = await formulator.QuestionAsync(Said);

        Assert.Equal(AnswerStepOutcome.Failed, result.Outcome);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_call_with_nothing_said_yet_is_nothing_and_the_model_is_not_called()
    {
        var model = AnswerWorkerModel.Answering("""{"question": "What does drain clearing cost?"}""");
        var (formulator, _) = Formulator(model);

        var result = await formulator.QuestionAsync([]);

        Assert.Equal(AnswerStepOutcome.Nothing, result.Outcome);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task A_call_of_tool_traffic_alone_is_nothing_and_the_model_is_not_called()
    {
        var model = AnswerWorkerModel.Answering("""{"question": "What does drain clearing cost?"}""");
        var (formulator, _) = Formulator(model);

        var result = await formulator.QuestionAsync(
        [
            new(ReceptionistSpeaker.ToolCall, """{"tool": "lookup_sentinel"}""", "call-1", "lookup_sentinel"),
            new(ReceptionistSpeaker.ToolResult, """{"result": "result_sentinel"}""", "call-1", "lookup_sentinel"),
        ]);

        Assert.Equal(AnswerStepOutcome.Nothing, result.Outcome);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Tool_calls_and_tool_results_are_not_shown_to_the_model()
    {
        var model = AnswerWorkerModel.Answering("""{"question": "What does drain clearing cost?"}""");
        var (formulator, _) = Formulator(model);

        await formulator.QuestionAsync(
        [
            new(ReceptionistSpeaker.Caller, "Do you clear drains?"),
            new(ReceptionistSpeaker.ToolCall, """{"tool": "lookup_sentinel"}""", "call-1", "lookup_sentinel"),
            new(ReceptionistSpeaker.ToolResult, """{"result": "result_sentinel"}""", "call-1", "lookup_sentinel"),
            new(ReceptionistSpeaker.Receptionist, "We do."),
        ]);

        Assert.Contains("Do you clear drains?", model.Asked!, StringComparison.Ordinal);
        Assert.Contains("We do.", model.Asked!, StringComparison.Ordinal);
        Assert.DoesNotContain("lookup_sentinel", model.Asked!, StringComparison.Ordinal);
        Assert.DoesNotContain("result_sentinel", model.Asked!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task At_most_the_turns_it_reads_are_shown_and_they_are_the_most_recent()
    {
        var model = AnswerWorkerModel.Answering("""{"question": "What does drain clearing cost?"}""");
        var (formulator, _) = Formulator(model);

        var turns = new List<ReceptionistTurn>();
        for (var i = 1; i <= QuestionFormulator.TurnsRead + 2; i++)
            turns.Add(new ReceptionistTurn(i % 2 == 1 ? ReceptionistSpeaker.Caller : ReceptionistSpeaker.Receptionist, $"Utterance number {i}."));

        await formulator.QuestionAsync(turns);

        Assert.Contains($"Utterance number {QuestionFormulator.TurnsRead + 2}.", model.Asked!, StringComparison.Ordinal);
        Assert.Contains($"Utterance number {QuestionFormulator.TurnsRead - 1}.", model.Asked!, StringComparison.Ordinal);
        Assert.DoesNotContain("Utterance number 1.", model.Asked!, StringComparison.Ordinal);
        Assert.DoesNotContain("Utterance number 2.", model.Asked!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_attempt_is_told_what_the_first_one_asked_and_to_ask_it_differently()
    {
        var model = AnswerWorkerModel.Answering("""{"question": "What do plumbing call-outs cost?"}""");
        var (formulator, _) = Formulator(model);

        await formulator.QuestionAsync(Said, "What does clearing a blocked kitchen drain cost?");

        Assert.Contains("What does clearing a blocked kitchen drain cost?", model.Asked!, StringComparison.Ordinal);
        Assert.Contains("different", model.Asked!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_first_attempt_is_not_told_to_ask_anything_differently()
    {
        var model = AnswerWorkerModel.Answering("""{"question": "What does drain clearing cost?"}""");
        var (formulator, _) = Formulator(model);

        await formulator.QuestionAsync(Said);

        Assert.DoesNotContain("different", model.Asked!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("broader", model.Asked!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_model_that_throws_is_a_failure_and_the_step_is_named()
    {
        var (formulator, log) = Formulator(AnswerWorkerModel.Throwing());

        var result = await formulator.QuestionAsync(Said);

        Assert.Equal(AnswerStepOutcome.Failed, result.Outcome);
        Assert.Contains(log.Lines, line => line.Contains("Working out what a caller is asking", StringComparison.Ordinal)
                                           && line.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_slower_than_the_budget_is_a_failure_on_the_workers_own_clock()
    {
        var (formulator, log) = Formulator(
            AnswerWorkerModel.SlowerThan(QuestionFormulator.Budget, """{"question": "What does drain clearing cost?"}"""));

        var result = await formulator.QuestionAsync(Said);

        Assert.Equal(AnswerStepOutcome.Failed, result.Outcome);
        Assert.Contains(log.Lines, line => line.Contains("longer than", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_call_ending_is_not_a_worker_failure()
    {
        var (formulator, log) = Formulator(AnswerWorkerModel.Answering("""{"question": "What does drain clearing cost?"}"""));
        using var ended = new CancellationTokenSource();
        await ended.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => formulator.QuestionAsync(Said, null, ended.Token));

        Assert.DoesNotContain(log.Lines, line => line.Contains("failed", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, line => line.Contains("longer than", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_question_the_model_wrapped_in_prose_or_a_fence_is_still_read()
    {
        var (formulator, _) = Formulator(AnswerWorkerModel.Answering("""
            Here you go:
            ```json
            {"question": "What does clearing a blocked kitchen drain cost?"}
            ```
            """));

        var result = await formulator.QuestionAsync(Said);

        Assert.Equal(AnswerStepOutcome.Done, result.Outcome);
        Assert.Equal("What does clearing a blocked kitchen drain cost?", result.Value);
    }

    [Fact]
    public async Task No_log_line_carries_what_the_caller_said_or_the_question_it_became()
    {
        string[] answers =
        [
            """{"question": "What does clearing a blocked kitchen drain cost?"}""",
            """{"question": null}""",
            "not a question at all",
            """{"q": "What does clearing a blocked kitchen drain cost?"}""",
        ];

        foreach (var answer in answers)
        {
            var (formulator, log) = Formulator(AnswerWorkerModel.Answering(answer));
            await formulator.QuestionAsync(Said, "Drain unblocking fee at Pelican Harbour");

            foreach (var line in log.Lines)
            {
                Assert.DoesNotContain("Pelican", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("kitchen sink", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("backing up", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("blocked kitchen drain", line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

/// <summary>
/// Writing what the receptionist says from the passages and nothing else, and reporting which of the three things happened. "The passages
/// do not answer it" is a result, not a failure: the orchestrator says so to the caller. "I could not write anything" is a failure, and
/// confusing the two either hangs a working call on a provider hiccup or tells a caller the business has no answer when nobody looked.
/// </summary>
public sealed class AnswerComposerTests
{
    private static (AnswerComposer Composer, AnswerWorkerLogger<AnswerComposer> Log) Composer(IReceptionistModel model)
    {
        var log = new AnswerWorkerLogger<AnswerComposer>();
        return (new AnswerComposer(model, log), log);
    }

    private const string Question = "What does clearing a blocked kitchen drain cost?";

    private static readonly IReadOnlyList<string> Three =
    [
        "Drain clearing at Pelican Harbour starts at ninety dollars.",
        "Our office is on Dixie Highway.",
        "We service tankless water heaters.",
    ];

    [Fact]
    public async Task An_answer_the_model_wrote_is_done_and_comes_back_trimmed()
    {
        var (composer, log) = Composer(AnswerWorkerModel.Answering("""{"answer": "  Drain clearing starts at ninety dollars.  "}"""));

        var result = await composer.AnswerAsync(Question, Three);

        Assert.Equal(AnswerStepOutcome.Done, result.Outcome);
        Assert.Equal("Drain clearing starts at ninety dollars.", result.Value);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task Passages_that_do_not_answer_the_question_are_nothing_and_not_a_failure()
    {
        var (composer, log) = Composer(AnswerWorkerModel.Answering("""{"answer": null}"""));

        var result = await composer.AnswerAsync(Question, Three);

        Assert.Equal(AnswerStepOutcome.Nothing, result.Outcome);
        Assert.Null(result.Value);
        Assert.Empty(log.Lines);
    }

    [Theory]
    [InlineData("It costs ninety dollars, I think.")]
    [InlineData("")]
    [InlineData("{ not json at all")]
    [InlineData("[\"ninety dollars\"]")]
    public async Task An_answer_that_is_not_a_json_object_is_a_failure_and_is_logged(string answer)
    {
        var (composer, log) = Composer(AnswerWorkerModel.Answering(answer));

        var result = await composer.AnswerAsync(Question, Three);

        Assert.Equal(AnswerStepOutcome.Failed, result.Outcome);
        Assert.Null(result.Value);
        Assert.Contains(log.Lines, line => line.Contains("could not be written", StringComparison.Ordinal));
    }

    /// <summary>
    /// Only an explicit null is "the passages do not answer it". A different key, a number or a string of spaces is the model not having
    /// done the job, and reading it as a considered no would tell a caller the business cannot answer when nothing ever decided that.
    /// </summary>
    [Theory]
    [InlineData("""{"a": "Ninety dollars."}""")]
    [InlineData("""{"answer": 90}""")]
    [InlineData("""{"answer": "   "}""")]
    [InlineData("{}")]
    public async Task A_json_object_with_no_usable_answer_is_a_failure_and_never_nothing(string answer)
    {
        var (composer, _) = Composer(AnswerWorkerModel.Answering(answer));

        var result = await composer.AnswerAsync(Question, Three);

        Assert.Equal(AnswerStepOutcome.Failed, result.Outcome);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Nothing_retrieved_is_nothing_to_answer_from_and_the_model_is_not_called()
    {
        var model = AnswerWorkerModel.Answering("""{"answer": "Drain clearing starts at ninety dollars."}""");
        var (composer, _) = Composer(model);

        var result = await composer.AnswerAsync(Question, []);

        Assert.Equal(AnswerStepOutcome.Nothing, result.Outcome);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task The_question_and_every_passage_are_put_to_the_model()
    {
        var model = AnswerWorkerModel.Answering("""{"answer": "Drain clearing starts at ninety dollars."}""");
        var (composer, _) = Composer(model);

        await composer.AnswerAsync(Question, Three);

        Assert.Contains(Question, model.Asked!, StringComparison.Ordinal);
        foreach (var passage in Three) Assert.Contains(passage, model.Asked!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rewrite_is_shown_the_draft_that_was_rejected_and_told_to_stay_closer_to_the_passages()
    {
        var model = AnswerWorkerModel.Answering("""{"answer": "Drain clearing starts at ninety dollars."}""");
        var (composer, _) = Composer(model);

        await composer.AnswerAsync(Question, Three, "We can clear your drain for sixty dollars.");

        Assert.Contains("We can clear your drain for sixty dollars.", model.Asked!, StringComparison.Ordinal);
        Assert.Contains("Stay closer to what they actually say", model.Asked!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_first_draft_is_not_told_to_stay_closer_to_anything()
    {
        var model = AnswerWorkerModel.Answering("""{"answer": "Drain clearing starts at ninety dollars."}""");
        var (composer, _) = Composer(model);

        await composer.AnswerAsync(Question, Three);

        Assert.DoesNotContain("Stay closer", model.Asked!, StringComparison.Ordinal);
        Assert.DoesNotContain("sixty dollars", model.Asked!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_model_that_throws_is_a_failure_and_the_step_is_named()
    {
        var (composer, log) = Composer(AnswerWorkerModel.Throwing());

        var result = await composer.AnswerAsync(Question, Three);

        Assert.Equal(AnswerStepOutcome.Failed, result.Outcome);
        Assert.Contains(log.Lines, line => line.Contains("Writing an answer for a caller", StringComparison.Ordinal)
                                           && line.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_slower_than_the_budget_is_a_failure_on_the_workers_own_clock()
    {
        var (composer, log) = Composer(
            AnswerWorkerModel.SlowerThan(AnswerComposer.Budget, """{"answer": "Drain clearing starts at ninety dollars."}"""));

        var result = await composer.AnswerAsync(Question, Three);

        Assert.Equal(AnswerStepOutcome.Failed, result.Outcome);
        Assert.Contains(log.Lines, line => line.Contains("longer than", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_call_ending_is_not_a_worker_failure()
    {
        var (composer, log) = Composer(AnswerWorkerModel.Answering("""{"answer": "Drain clearing starts at ninety dollars."}"""));
        using var ended = new CancellationTokenSource();
        await ended.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => composer.AnswerAsync(Question, Three, null, ended.Token));

        Assert.DoesNotContain(log.Lines, line => line.Contains("failed", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, line => line.Contains("longer than", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_answer_the_model_wrapped_in_prose_or_a_fence_is_still_read()
    {
        var (composer, _) = Composer(AnswerWorkerModel.Answering("""
            Here you go:
            ```json
            {"answer": "Drain clearing starts at ninety dollars."}
            ```
            """));

        var result = await composer.AnswerAsync(Question, Three);

        Assert.Equal(AnswerStepOutcome.Done, result.Outcome);
        Assert.Equal("Drain clearing starts at ninety dollars.", result.Value);
    }

    [Fact]
    public async Task No_log_line_carries_the_question_a_passage_or_the_answer()
    {
        string[] answers =
        [
            """{"answer": "Drain clearing starts at ninety dollars."}""",
            """{"answer": null}""",
            "not an answer at all",
            """{"a": "Ninety dollars."}""",
        ];

        foreach (var answer in answers)
        {
            var (composer, log) = Composer(AnswerWorkerModel.Answering(answer));
            await composer.AnswerAsync(Question, Three, "We can clear your drain for sixty dollars.");

            foreach (var line in log.Lines)
            {
                Assert.DoesNotContain("Pelican", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ninety dollars", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("sixty dollars", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Dixie", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("tankless", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("blocked kitchen drain", line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

/// <summary>
/// Checking that what the receptionist is about to say is in the passages it was given, and reporting which of the three things happened.
/// Here the distinction has teeth: a considered <c>false</c> is <see cref="AnswerStepOutcome.Done"/>, because the orchestrator answers it
/// by sending the composer back for a closer draft, while <see cref="AnswerStepOutcome.Failed"/> means nothing established anything and
/// the answer is refused outright. A check that reported "no" when it had simply fallen over would retry against a model that is down;
/// one that reported "could not" for a real no would throw away the retry that exists to save the call.
/// </summary>
public sealed class AnswerGroundingTests
{
    private static (AnswerGrounding Grounding, AnswerWorkerLogger<AnswerGrounding> Log) Grounding(IReceptionistModel model)
    {
        var log = new AnswerWorkerLogger<AnswerGrounding>();
        return (new AnswerGrounding(model, log), log);
    }

    private const string Statement = "We can service your tankless heater on Thursday.";

    private static readonly IReadOnlyList<string> Three =
    [
        "We service most tankless water heaters.",
        "Our office at Pelican Harbour is on Dixie Highway.",
        "Tankless service starts at ninety dollars.",
    ];

    [Fact]
    public async Task A_statement_the_passages_support_is_done_and_true()
    {
        var (grounding, log) = Grounding(AnswerWorkerModel.Answering("""{"supported": true}"""));

        var result = await grounding.SupportsAsync(Statement, Three);

        Assert.Equal(AnswerStepOutcome.Done, result.Outcome);
        Assert.True(result.Value);
        Assert.Empty(log.Lines);
    }

    /// <summary>
    /// The one that would be easiest to get wrong: a check that read the passages and decided they do not say it has done its job, so the
    /// verdict is <see cref="AnswerStepOutcome.Done"/> carrying <c>false</c>. Reporting it as nothing would lose the difference between a
    /// draft that can be rewritten and a check that never ran.
    /// </summary>
    [Fact]
    public async Task A_considered_no_is_done_and_false_rather_than_nothing()
    {
        var (grounding, log) = Grounding(AnswerWorkerModel.Answering("""{"supported": false}"""));

        var result = await grounding.SupportsAsync(Statement, Three);

        Assert.Equal(AnswerStepOutcome.Done, result.Outcome);
        Assert.False(result.Value);
        Assert.Empty(log.Lines);
    }

    [Theory]
    [InlineData("""{"supported": "yes"}""")]
    [InlineData("""{"supported": null}""")]
    [InlineData("""{"supported": 1}""")]
    [InlineData("""{"grounded": true}""")]
    [InlineData("{}")]
    public async Task A_verdict_that_is_not_a_yes_or_a_no_is_a_failure_and_is_logged(string verdict)
    {
        var (grounding, log) = Grounding(AnswerWorkerModel.Answering(verdict));

        var result = await grounding.SupportsAsync(Statement, Three);

        Assert.Equal(AnswerStepOutcome.Failed, result.Outcome);
        Assert.Contains(log.Lines, line => line.Contains("not a verdict", StringComparison.Ordinal));
    }

    /// <summary>
    /// An answer that is not JSON at all is a failure like any other unusable one. Nothing is logged for it today - the gap is noted here
    /// rather than pinned, because a check that falls silent is a thing to fix, not a behaviour to hold.
    /// </summary>
    [Theory]
    [InlineData("I would say that is supported.")]
    [InlineData("")]
    [InlineData("{ not json at all")]
    [InlineData("[true]")]
    public async Task An_answer_that_is_not_a_json_object_is_a_failure(string verdict)
    {
        var (grounding, _) = Grounding(AnswerWorkerModel.Answering(verdict));

        var result = await grounding.SupportsAsync(Statement, Three);

        Assert.Equal(AnswerStepOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task Nothing_retrieved_is_nothing_to_check_against_and_the_model_is_not_called()
    {
        var model = AnswerWorkerModel.Answering("""{"supported": true}""");
        var (grounding, _) = Grounding(model);

        var result = await grounding.SupportsAsync(Statement, []);

        Assert.Equal(AnswerStepOutcome.Nothing, result.Outcome);
        Assert.Equal(0, model.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_statement_is_nothing_to_check_and_the_model_is_not_called(string answer)
    {
        var model = AnswerWorkerModel.Answering("""{"supported": true}""");
        var (grounding, _) = Grounding(model);

        var result = await grounding.SupportsAsync(answer, Three);

        Assert.Equal(AnswerStepOutcome.Nothing, result.Outcome);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task The_statement_and_every_passage_are_put_to_the_model()
    {
        var model = AnswerWorkerModel.Answering("""{"supported": true}""");
        var (grounding, _) = Grounding(model);

        await grounding.SupportsAsync(Statement, Three);

        Assert.Contains(Statement, model.Asked!, StringComparison.Ordinal);
        foreach (var passage in Three) Assert.Contains(passage, model.Asked!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_model_that_throws_is_a_failure_and_the_step_is_named()
    {
        var (grounding, log) = Grounding(AnswerWorkerModel.Throwing());

        var result = await grounding.SupportsAsync(Statement, Three);

        Assert.Equal(AnswerStepOutcome.Failed, result.Outcome);
        Assert.Contains(log.Lines, line => line.Contains("Checking an answer against the business's own words", StringComparison.Ordinal)
                                           && line.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_slower_than_the_budget_is_a_failure_on_the_workers_own_clock()
    {
        var (grounding, log) = Grounding(AnswerWorkerModel.SlowerThan(AnswerGrounding.Budget, """{"supported": true}"""));

        var result = await grounding.SupportsAsync(Statement, Three);

        Assert.Equal(AnswerStepOutcome.Failed, result.Outcome);
        Assert.Contains(log.Lines, line => line.Contains("longer than", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_call_ending_is_not_a_worker_failure()
    {
        var (grounding, log) = Grounding(AnswerWorkerModel.Answering("""{"supported": true}"""));
        using var ended = new CancellationTokenSource();
        await ended.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => grounding.SupportsAsync(Statement, Three, ended.Token));

        Assert.DoesNotContain(log.Lines, line => line.Contains("failed", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, line => line.Contains("longer than", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, line => line.Contains("not a verdict", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_verdict_the_model_wrapped_in_prose_or_a_fence_is_still_read()
    {
        var (grounding, _) = Grounding(AnswerWorkerModel.Answering("""
            Here you go:
            ```json
            {"supported": false}
            ```
            """));

        var result = await grounding.SupportsAsync(Statement, Three);

        Assert.Equal(AnswerStepOutcome.Done, result.Outcome);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task No_log_line_carries_the_statement_or_a_passage()
    {
        string[] verdicts = ["""{"supported": true}""", """{"supported": false}""", "not a verdict at all", """{"supported": "yes"}"""];

        foreach (var verdict in verdicts)
        {
            var (grounding, log) = Grounding(AnswerWorkerModel.Answering(verdict));
            await grounding.SupportsAsync(Statement, Three);

            foreach (var line in log.Lines)
            {
                Assert.DoesNotContain("tankless", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Thursday", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Pelican", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Dixie", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ninety dollars", line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
