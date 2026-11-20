using GeekAPI.Services.Receptionist.Agents;
using GeekApplication.Interfaces.Receptionist;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// Who takes the caller's next turn, and what it costs to decide. Two properties carry the weight: a role that has already told the
/// caller it is putting them through is honoured without a round trip, because the caller is on the line; and every way the director can
/// fail leaves the caller with whoever is already speaking, because inventing a destination moves a caller to a part of reception that
/// cannot help them.
/// </summary>
public sealed class ReceptionistDispatcherTests
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

    private static (ReceptionistDispatcher Director, CapturingLogger<ReceptionistDispatcher> Log) Director(IReceptionistModel model)
    {
        var log = new CapturingLogger<ReceptionistDispatcher>();
        return (new ReceptionistDispatcher(model, log), log);
    }

    private static readonly ReceptionistAgent Greeter =
        new(ReceptionistRoles.Greeter, "Opens the call and works out what the caller wants.", "You greet.", null);

    private static readonly ReceptionistAgent Booker =
        new(ReceptionistRoles.Booker, "Qualifies and books an appointment in the calendar.", "You book.", null);

    private static readonly ReceptionistAgent Answerer =
        new(ReceptionistRoles.Answerer, "Answers what the caller asks about the business.", "You answer.", null);

    private static readonly IReadOnlyList<ReceptionistAgent> Roles = [Greeter, Booker, Answerer];

    /// <summary>What the caller said, in words that appear nowhere in a log format string, so the retention assertion cannot pass by luck.</summary>
    private static readonly IReadOnlyList<ReceptionistTurn> Turns =
    [
        new(ReceptionistSpeaker.Receptionist, "Thanks for calling, how can I help?"),
        new(ReceptionistSpeaker.Caller, "My name is Marguerite Pemberton and the heater at 4120 Dixie Highway is leaking."),
    ];

    private static Task<string?> Route(
        ReceptionistDispatcher director,
        string? requested = null,
        IReadOnlyList<ReceptionistAgent>? roles = null,
        IReadOnlyList<ReceptionistTurn>? turns = null,
        CancellationToken ct = default) =>
        director.RouteAsync(Greeter, roles ?? Roles, turns ?? Turns, requested, ct);

    [Fact]
    public async Task A_role_that_asked_for_another_role_is_taken_at_its_word_without_a_model_call()
    {
        var model = Answering($$"""{"role": "{{ReceptionistRoles.Answerer}}"}""");
        var (director, _) = Director(model);

        Assert.Equal(ReceptionistRoles.Booker, await Route(director, requested: ReceptionistRoles.Booker));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task A_role_that_asked_for_itself_moves_nobody_and_asks_no_model()
    {
        var model = Answering($$"""{"role": "{{ReceptionistRoles.Booker}}"}""");
        var (director, _) = Director(model);

        Assert.Null(await Route(director, requested: ReceptionistRoles.Greeter));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task A_role_that_asked_for_somewhere_this_caller_cannot_go_is_refused_and_logged()
    {
        var model = Answering($$"""{"role": "{{ReceptionistRoles.Booker}}"}""");
        var (director, log) = Director(model);

        Assert.Null(await Route(director, requested: ReceptionistRoles.MessageTaker));
        Assert.Equal(0, model.Calls);
        Assert.Contains(log.Lines, line => line.Contains("not somewhere this caller can go", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_role_with_nowhere_to_hand_to_stays_put_without_asking_a_model()
    {
        var model = Answering($$"""{"role": "{{ReceptionistRoles.Booker}}"}""");
        var (director, _) = Director(model);

        Assert.Null(await Route(director, roles: [Greeter]));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task The_director_naming_the_role_already_speaking_moves_nobody()
    {
        var model = Answering($$"""{"role": "{{ReceptionistRoles.Greeter}}"}""");
        var (director, _) = Director(model);

        Assert.Null(await Route(director));
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task The_director_naming_another_role_moves_the_caller_there()
    {
        var (director, _) = Director(Answering($$"""{"role": "{{ReceptionistRoles.Booker}}"}"""));

        Assert.Equal(ReceptionistRoles.Booker, await Route(director));
    }

    [Fact]
    public async Task Every_role_its_purpose_and_the_one_speaking_are_put_to_the_director()
    {
        var model = Answering($$"""{"role": "{{ReceptionistRoles.Booker}}"}""");
        var (director, _) = Director(model);

        await Route(director);

        Assert.Contains($"- {ReceptionistRoles.Greeter}: Opens the call and works out what the caller wants. (speaking now)", model.Asked!, StringComparison.Ordinal);
        Assert.Contains($"- {ReceptionistRoles.Booker}: Qualifies and books an appointment in the calendar.", model.Asked!, StringComparison.Ordinal);
        Assert.Contains($"- {ReceptionistRoles.Answerer}: Answers what the caller asks about the business.", model.Asked!, StringComparison.Ordinal);
        Assert.DoesNotContain($"- {ReceptionistRoles.Booker}: Qualifies and books an appointment in the calendar. (speaking now)", model.Asked!, StringComparison.Ordinal);
        Assert.Contains("Caller: My name is Marguerite Pemberton", model.Asked!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_the_caller_and_reception_said_is_put_to_the_director_and_a_tool_turn_is_not()
    {
        var model = Answering($$"""{"role": "{{ReceptionistRoles.Booker}}"}""");
        var (director, _) = Director(model);

        await Route(director, turns:
        [
            new(ReceptionistSpeaker.Caller, "The heater at 4120 Dixie Highway is leaking."),
            new(ReceptionistSpeaker.ToolCall, """{"slot": "ARGONAUT"}""", "call_1", "book_appointment"),
            new(ReceptionistSpeaker.ToolResult, """{"confirmation": "ZEBRAFISH"}""", "call_1", "book_appointment"),
            new(ReceptionistSpeaker.Receptionist, "You are booked for Thursday."),
        ]);

        Assert.Contains("Caller: The heater at 4120 Dixie Highway is leaking.", model.Asked!, StringComparison.Ordinal);
        Assert.Contains("Reception: You are booked for Thursday.", model.Asked!, StringComparison.Ordinal);
        Assert.DoesNotContain("ARGONAUT", model.Asked!, StringComparison.Ordinal);
        Assert.DoesNotContain("ZEBRAFISH", model.Asked!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_most_recent_turns_are_put_to_the_director()
    {
        var model = Answering($$"""{"role": "{{ReceptionistRoles.Booker}}"}""");
        var (director, _) = Director(model);

        var many = new List<ReceptionistTurn>();
        for (var i = 1; i <= ReceptionistDispatcher.TurnsRead + 2; i++)
            many.Add(new ReceptionistTurn(ReceptionistSpeaker.Caller, $"Utterance number {i}."));

        await Route(director, turns: many);

        Assert.DoesNotContain("Utterance number 1.", model.Asked!, StringComparison.Ordinal);
        Assert.DoesNotContain("Utterance number 2.", model.Asked!, StringComparison.Ordinal);
        Assert.Contains("Utterance number 3.", model.Asked!, StringComparison.Ordinal);
        Assert.Contains($"Utterance number {ReceptionistDispatcher.TurnsRead + 2}.", model.Asked!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("I think the booker should take this one.")]
    [InlineData("")]
    [InlineData("{ not json at all")]
    [InlineData("""{"part": "booker"}""")]
    [InlineData("""{"role": 7}""")]
    [InlineData("""{"role": "   "}""")]
    public async Task An_answer_that_is_not_a_choice_moves_nobody_and_is_logged(string answer)
    {
        var (director, log) = Director(Answering(answer));

        Assert.Null(await Route(director));
        Assert.Contains(log.Lines, line => line.Contains("not a choice", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_director_naming_a_part_of_reception_that_does_not_exist_moves_nobody_and_is_logged()
    {
        var (director, log) = Director(Answering("""{"role": "billing"}"""));

        Assert.Null(await Route(director));
        Assert.Contains(log.Lines, line => line.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_director_that_fails_moves_nobody_rather_than_guessing_a_destination()
    {
        var (director, log) = Director(new Model(_ => throw new HttpRequestException("the provider is down")));

        Assert.Null(await Route(director));
        Assert.Contains(log.Lines, line => line.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_director_that_is_too_slow_for_a_caller_moves_nobody()
    {
        var (director, log) = Director(new Model(async _ =>
        {
            await Task.Delay(ReceptionistDispatcher.Budget + TimeSpan.FromSeconds(5), CancellationToken.None);
            return new ReceptionistReply($$"""{"role": "{{ReceptionistRoles.Booker}}"}""", false);
        }));

        Assert.Null(await Route(director));
        Assert.Contains(log.Lines, line => line.Contains("longer than", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_choice_the_director_wrapped_in_prose_or_a_fence_is_still_read()
    {
        var (director, _) = Director(Answering($$"""
            Here you go:
            ```json
            {"role": "{{ReceptionistRoles.Booker}}"}
            ```
            """));

        Assert.Equal(ReceptionistRoles.Booker, await Route(director));
    }

    [Fact]
    public async Task The_call_ending_is_not_treated_as_a_routing_failure()
    {
        var (director, log) = Director(Answering($$"""{"role": "{{ReceptionistRoles.Booker}}"}"""));
        using var ended = new CancellationTokenSource();
        await ended.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Route(director, ct: ended.Token));

        Assert.DoesNotContain(log.Lines, line => line.Contains("failed", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, line => line.Contains("longer than", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_log_line_carries_anything_the_caller_said()
    {
        var answers = new[]
        {
            $$"""{"role": "{{ReceptionistRoles.Booker}}"}""",
            $$"""{"role": "{{ReceptionistRoles.Greeter}}"}""",
            "I think the booker should take this one.",
        };

        foreach (var answer in answers)
        {
            var (director, log) = Director(Answering(answer));
            await Route(director);

            foreach (var line in log.Lines)
            {
                Assert.DoesNotContain("Marguerite", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Pemberton", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Dixie", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("4120", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("leaking", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("how can I help", line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
