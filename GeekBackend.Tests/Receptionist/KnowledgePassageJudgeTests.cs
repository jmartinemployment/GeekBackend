using GeekAPI.Services.Receptionist.Knowledge;
using GeekApplication.Interfaces.Receptionist;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// Which retrieved passages actually answer the caller, and what happens when that cannot be decided. The second question is the one
/// that matters: a judge that fails keeps nothing, because passing the passages through unjudged is the salvage-with-unverified-data
/// path this codebase forbids, and because the thing on the other side of it is a receptionist quoting a price.
/// </summary>
public sealed class KnowledgePassageJudgeTests
{
    /// <summary>Every line this class logged, so the zero-retention assertion has something to read.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    private sealed record Passage(string Text);

    private sealed class Model(Func<string, Task<ReceptionistReply>> reply) : IReceptionistModel
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
    }

    private static Model Answering(string text) => new(_ => Task.FromResult(new ReceptionistReply(text, false)));

    private static (KnowledgePassageJudge Judge, CapturingLogger<KnowledgePassageJudge> Log) Judge(IReceptionistModel model)
    {
        var log = new CapturingLogger<KnowledgePassageJudge>();
        return (new KnowledgePassageJudge(model, log), log);
    }

    private static readonly IReadOnlyList<Passage> Three =
    [
        new("We repair tankless water heaters."),
        new("Our office is on Dixie Highway."),
        new("Tankless service starts at ninety dollars."),
    ];

    private static Task<AnswerStepResult<IReadOnlyList<Passage>>> Keep(KnowledgePassageJudge judge, IReadOnlyList<Passage> passages) =>
        judge.KeepAsync("Do you fix tankless heaters?", passages, p => p.Text);

    [Fact]
    public async Task Nothing_retrieved_is_nothing_judged_and_the_model_is_not_called()
    {
        var model = Answering("""{"keep": [0]}""");
        var (judge, _) = Judge(model);

        Assert.Equal(AnswerStepOutcome.Nothing, (await Keep(judge, [])).Outcome);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task The_passages_the_judge_keeps_come_back_in_the_order_they_were_given()
    {
        var (judge, _) = Judge(Answering("""{"keep": [2, 0]}"""));

        var kept = await Keep(judge, Three);

        Assert.Equal(AnswerStepOutcome.Done, kept.Outcome);
        Assert.Equal([Three[0], Three[2]], kept.Value);
    }

    [Fact]
    public async Task Keeping_nothing_is_a_verdict_and_not_a_failure()
    {
        var (judge, log) = Judge(Answering("""{"keep": []}"""));

        Assert.Equal(AnswerStepOutcome.Nothing, (await Keep(judge, Three)).Outcome);
        Assert.DoesNotContain(log.Lines, line => line.Contains("not a verdict", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_question_and_every_passage_are_put_to_the_judge_numbered()
    {
        var model = Answering("""{"keep": [0]}""");
        var (judge, _) = Judge(model);

        await Keep(judge, Three);

        Assert.Contains("Do you fix tankless heaters?", model.Asked!, StringComparison.Ordinal);
        Assert.Contains("[0] We repair tankless water heaters.", model.Asked!, StringComparison.Ordinal);
        Assert.Contains("[2] Tankless service starts at ninety dollars.", model.Asked!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("I think the first one is best.")]
    [InlineData("")]
    [InlineData("{ not json at all")]
    [InlineData("""{"kept": [0]}""")]
    [InlineData("""{"keep": "0"}""")]
    public async Task An_answer_that_is_not_a_verdict_keeps_nothing_and_is_logged(string answer)
    {
        var (judge, log) = Judge(Answering(answer));

        Assert.Equal(AnswerStepOutcome.Failed, (await Keep(judge, Three)).Outcome);
        Assert.Contains(log.Lines, line => line.Contains("not a verdict", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_verdict_the_model_wrapped_in_prose_or_a_fence_is_still_read()
    {
        var (judge, _) = Judge(Answering("""
            Here you go:
            ```json
            {"keep": [1]}
            ```
            """));

        Assert.Equal([Three[1]], (await Keep(judge, Three)).Value);
    }

    [Fact]
    public async Task A_passage_number_that_does_not_exist_is_dropped_and_the_rest_of_the_verdict_stands()
    {
        var (judge, _) = Judge(Answering("""{"keep": [0, 9, -1, 2]}"""));

        Assert.Equal([Three[0], Three[2]], (await Keep(judge, Three)).Value);
    }

    [Fact]
    public async Task A_passage_named_twice_is_kept_once()
    {
        var (judge, _) = Judge(Answering("""{"keep": [1, 1, 1]}"""));

        Assert.Equal([Three[1]], (await Keep(judge, Three)).Value);
    }

    [Fact]
    public async Task A_judge_that_fails_keeps_nothing_rather_than_letting_the_passages_through()
    {
        var (judge, log) = Judge(new Model(_ => throw new HttpRequestException("the provider is down")));

        Assert.Equal(AnswerStepOutcome.Failed, (await Keep(judge, Three)).Outcome);
        Assert.Contains(log.Lines, line => line.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_judge_that_is_too_slow_for_a_caller_keeps_nothing()
    {
        var (judge, log) = Judge(new Model(async _ =>
        {
            await Task.Delay(KnowledgePassageJudge.Budget + TimeSpan.FromSeconds(5), CancellationToken.None);
            return new ReceptionistReply("""{"keep": [0, 1, 2]}""", false);
        }));

        Assert.Equal(AnswerStepOutcome.Failed, (await Keep(judge, Three)).Outcome);
        Assert.Contains(log.Lines, line => line.Contains("longer than", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_call_ending_is_not_treated_as_a_judge_failure()
    {
        var (judge, log) = Judge(Answering("""{"keep": [0]}"""));
        using var ended = new CancellationTokenSource();
        await ended.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => judge.KeepAsync("Do you fix tankless heaters?", Three, p => p.Text, ended.Token));

        Assert.DoesNotContain(log.Lines, line => line.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_log_line_carries_the_question_or_a_passage()
    {
        foreach (var answer in new[] { """{"keep": [0]}""", "not a verdict at all", """{"keep": []}""" })
        {
            var (judge, log) = Judge(Answering(answer));
            await Keep(judge, Three);

            foreach (var line in log.Lines)
            {
                Assert.DoesNotContain("tankless", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Dixie", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ninety dollars", line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
