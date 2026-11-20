using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Receptionist;

namespace GeekAPI.Services.Receptionist.Knowledge;

/// <summary>
/// Why a worker has nothing to give back. A worker reports which of these happened; it never decides what the call does about it.
/// The distinction is the point: "there was nothing to look up" and "I could not work out what to look up" are different facts, and a
/// coordinator that cannot tell them apart cannot plan.
/// </summary>
public enum AnswerStepOutcome
{
    /// <summary>The worker did its job and has a result.</summary>
    Done,

    /// <summary>The worker did its job and the honest result is nothing. Not a failure.</summary>
    Nothing,

    /// <summary>The worker could not do its job: it failed, timed out, or answered with something unusable.</summary>
    Failed,
}

/// <summary>What one worker found, and which of the three things happened. Workers return this and stop.</summary>
public readonly record struct AnswerStepResult<T>(AnswerStepOutcome Outcome, T? Value)
{
    public static AnswerStepResult<T> Done(T value) => new(AnswerStepOutcome.Done, value);
    public static AnswerStepResult<T> Nothing() => new(AnswerStepOutcome.Nothing, default);
    public static AnswerStepResult<T> Failed() => new(AnswerStepOutcome.Failed, default);
}

/// <summary>
/// Shared machinery for a worker's one model call: its own budget, held on this side, and a JSON answer. Workers do not share
/// decisions, only this.
/// </summary>
internal static class AnswerStep
{
    /// <summary>
    /// One worker's model call, bounded here rather than trusted to the provider. Cancelling a token only works if the implementation
    /// honours it, and a caller is on the line. Null means no usable answer came back.
    /// </summary>
    public static async Task<string?> AskAsync(
        IReceptionistModel model, string instruction, string input, TimeSpan budget, ILogger logger, string step, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(budget);

        try
        {
            var asking = model.ReplyAsync(instruction, [new ReceptionistTurn(ReceptionistSpeaker.Caller, input)], null, deadline.Token);

            using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var finished = await Task.WhenAny(asking, Task.Delay(budget, timer.Token)).ConfigureAwait(false);
            await timer.CancelAsync().ConfigureAwait(false);

            // Before the timeout is read: the call ending cancels the timer too, and reporting that as a step that ran out of time
            // would be an error that never happened.
            ct.ThrowIfCancellationRequested();

            if (finished != asking)
            {
                await deadline.CancelAsync().ConfigureAwait(false);
                Abandon(asking);
                logger.LogError("{Step} took longer than {Budget} and the caller is not kept waiting for it.", step, budget);
                return null;
            }

            return (await asking.ConfigureAwait(false)).Text;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // The provider honoured the deadline and threw before the race above noticed. Same overrun, so it reads the same way:
            // otherwise one slow step reported itself two different ways depending on which timer won by a millisecond.
            logger.LogError("{Step} took longer than {Budget} and the caller is not kept waiting for it.", step, budget);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError("{Step} failed ({Failure}).", step, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Reads whatever becomes of a request nobody waits for any more, so a late failure is not an unobserved task exception.</summary>
    private static void Abandon(Task task) =>
        _ = task.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    /// <summary>The JSON object a worker answered with, or null. Models fence JSON whatever they are told, so it is taken from the first brace to the last.</summary>
    public static JsonDocument? Object(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            var parsed = JsonDocument.Parse(answer[start..(end + 1)]);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object) return parsed;
            parsed.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether the property is present and explicitly null, which a worker uses to mean "nothing", not "I could not".</summary>
    public static bool ExplicitlyNull(JsonDocument document, string name) =>
        document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Null;

    /// <summary>A non-empty string property, trimmed, or null.</summary>
    public static string? Text(JsonDocument document, string name) =>
        document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    /// <summary>The turns a worker is shown, most recent last, with tool traffic left out. Only what the caller and the receptionist said.</summary>
    public static string Spoken(IReadOnlyList<ReceptionistTurn> turns, int take)
    {
        var said = new StringBuilder();
        foreach (var turn in turns.TakeLast(take))
        {
            if (turn.Speaker is ReceptionistSpeaker.ToolCall or ReceptionistSpeaker.ToolResult) continue;
            said.Append(turn.Speaker == ReceptionistSpeaker.Caller ? "Caller: " : "Reception: ").Append(turn.Text).Append('\n');
        }
        return said.ToString();
    }
}

/// <summary>
/// Turns what a caller said into something worth searching for. One job: it does not search, and it does not decide whether searching
/// is worth it.
///
/// <para>
/// A caller does not ask a search engine. They say "do you guys do the thing with the pipes under the sink", or they ask a follow-up -
/// "and how much is that?" - which means nothing on its own and everything after the two turns before it. Embedding the raw utterance
/// searches for the words rather than the question, and the follow-up searches for nothing.
/// </para>
///
/// <para><see cref="AnswerStepOutcome.Nothing"/> is a greeting, a name, small talk, or a request to be put through - there is honestly
/// nothing to look up. That is not the same as failing, and the orchestrator does different things about each.</para>
/// </summary>
public sealed class QuestionFormulator(IReceptionistModel model, ILogger<QuestionFormulator> logger)
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);

    /// <summary>How much of the call is read. A follow-up needs what it follows; more than that is not what was asked.</summary>
    internal const int TurnsRead = 4;

    private const string Instruction = """
        You turn what a caller said on the telephone into one question to look up in a business's own published information.

        Write the question the caller means, not the words they used. Resolve anything they referred to without naming - "that", "it",
        "the same thing" - from what was said earlier in the call. Write it as a complete question that stands on its own.

        If what they said is not a question about this business - a greeting, a name, a phone number, small talk, or a request to be put
        through to someone - there is nothing to look up.

        Answer with a JSON object and nothing else: {"question": "..."} or {"question": null} when there is nothing to look up.
        """;

    /// <summary>What to search for, with which of the three things happened.</summary>
    public async Task<AnswerStepResult<string>> QuestionAsync(
        IReadOnlyList<ReceptionistTurn> turns, string? broadenedFrom = null, CancellationToken ct = default)
    {
        var said = AnswerStep.Spoken(turns, TurnsRead);
        if (said.Length == 0) return AnswerStepResult<string>.Nothing();

        // A second attempt is told what the first one tried, so it writes a different question rather than the same one again.
        var input = broadenedFrom is null
            ? said
            : $"{said}\nA search for this found nothing useful, so ask it a different and broader way: {broadenedFrom}";

        var answer = await AnswerStep
            .AskAsync(model, Instruction, input, Budget, logger, "Working out what a caller is asking", ct).ConfigureAwait(false);

        using var document = AnswerStep.Object(answer);
        if (document is null)
        {
            logger.LogError("What the caller is asking could not be worked out.");
            return AnswerStepResult<string>.Failed();
        }

        if (AnswerStep.Text(document, "question") is { } question) return AnswerStepResult<string>.Done(question);
        if (AnswerStep.ExplicitlyNull(document, "question")) return AnswerStepResult<string>.Nothing();

        // Parseable JSON with the wrong key or the wrong type. The likelier drift in practice than prose, and it left no trace at all.
        logger.LogError("What the caller is asking came back in a shape that is not a question.");
        return AnswerStepResult<string>.Failed();
    }
}

/// <summary>
/// Writes what the receptionist says, from the passages it is given and nothing else. One job: it does not retrieve, does not check
/// itself, and does not decide whether what it wrote gets said.
///
/// <para>
/// Separate from the call's own model because the job is different: it is not holding a conversation, not deciding what to do next, and
/// not allowed to know anything that is not in front of it. Given only the passages, there is nothing else to draw on - which is most
/// of the protection, before anything checks it.
/// </para>
/// </summary>
public sealed class AnswerComposer(IReceptionistModel model, ILogger<AnswerComposer> logger)
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    private const string Instruction = """
        You answer a caller's question using only the passages given to you, which are a business's own published words about itself.

        Everything you say must be supported by those passages. Do not add, round, average, generalise or guess - no price, no timing,
        no availability, no promise that is not there in front of you. If the passages do not answer the question, say so.

        You are speaking aloud on a telephone. Write one or two plain sentences a person would say. No lists, no headings, no symbols,
        no web addresses, no "according to our website".

        Answer with a JSON object and nothing else:
        {"answer": "..."} with what to say, or {"answer": null} when the passages do not answer the question.
        """;

    /// <summary>What to say, with which of the three things happened. <see cref="AnswerStepOutcome.Nothing"/> is the passages not answering it.</summary>
    public async Task<AnswerStepResult<string>> AnswerAsync(
        string question, IReadOnlyList<string> passages, string? rejected = null, CancellationToken ct = default)
    {
        if (passages.Count == 0) return AnswerStepResult<string>.Nothing();

        var input = new StringBuilder();
        input.Append("Question: ").Append(question).Append("\n\nPassages:\n");
        foreach (var passage in passages) input.Append("- ").Append(passage).Append('\n');

        // A rewrite is told what was rejected, so it writes something closer to the passages rather than the same claim again.
        if (rejected is not null)
            input.Append("\nThis was checked against the passages and was not supported by them. Stay closer to what they actually say: ")
                 .Append(rejected).Append('\n');

        var answer = await AnswerStep
            .AskAsync(model, Instruction, input.ToString(), Budget, logger, "Writing an answer for a caller", ct).ConfigureAwait(false);

        using var document = AnswerStep.Object(answer);
        if (document is null)
        {
            logger.LogError("An answer for a caller could not be written.");
            return AnswerStepResult<string>.Failed();
        }

        if (AnswerStep.Text(document, "answer") is { } said) return AnswerStepResult<string>.Done(said);
        if (AnswerStep.ExplicitlyNull(document, "answer")) return AnswerStepResult<string>.Nothing();

        logger.LogError("An answer for a caller came back in a shape that is not an answer.");
        return AnswerStepResult<string>.Failed();
    }
}

/// <summary>
/// Checks that what the receptionist is about to say is actually in the passages it was given. One job, and the job is doubt.
///
/// <para>
/// This is the step that stops an invented price. The judge before it decided the passages were about the right thing; it did not and
/// could not decide the answer stays inside them. A composer handed "we service most tankless models" can still say "yes, we do yours",
/// and every earlier step passes it: the passage was retrieved correctly, judged relevant correctly, and the sentence reads perfectly
/// well. Only a reader holding both can see that one does not follow from the other.
/// </para>
///
/// <para>It is a different model call from the one that wrote the answer. Asking a writer to mark its own work is not a check.</para>
/// </summary>
public sealed class AnswerGrounding(IReceptionistModel model, ILogger<AnswerGrounding> logger)
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(4);

    private const string Instruction = """
        You check whether a statement is supported by the passages beneath it. The passages are the only evidence there is.

        Supported means a careful reader could point at the passages and show the statement follows from them. It is not supported if it
        names a number, a time, a price or a guarantee the passages do not; if it turns "most", "some" or "usually" into a yes; if it
        applies something general to the caller's particular case; or if it is merely plausible for this kind of business.

        You are not judging whether the statement is helpful, well written, or likely to be true in the world. Only whether these
        passages say it.

        Answer with a JSON object and nothing else: {"supported": true} or {"supported": false}.
        """;

    /// <summary>Whether the passages say it. <see cref="AnswerStepOutcome.Nothing"/> is a considered no; failing to establish it is <see cref="AnswerStepOutcome.Failed"/>.</summary>
    public async Task<AnswerStepResult<bool>> SupportsAsync(string answer, IReadOnlyList<string> passages, CancellationToken ct = default)
    {
        if (passages.Count == 0 || string.IsNullOrWhiteSpace(answer)) return AnswerStepResult<bool>.Nothing();

        var input = new StringBuilder();
        input.Append("Statement: ").Append(answer).Append("\n\nPassages:\n");
        foreach (var passage in passages) input.Append("- ").Append(passage).Append('\n');

        var verdict = await AnswerStep
            .AskAsync(model, Instruction, input.ToString(), Budget, logger, "Checking an answer against the business's own words", ct)
            .ConfigureAwait(false);

        using var document = AnswerStep.Object(verdict);
        if (document is null)
        {
            // The other two workers log here; this one did not, so the step that exists to stop an invented price was the one that
            // could fail without saying so.
            logger.LogError("Whether an answer is supported could not be established; it is treated as unsupported.");
            return AnswerStepResult<bool>.Failed();
        }

        if (!document.RootElement.TryGetProperty("supported", out var supported)
            || supported.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            logger.LogError("The grounding check answered with something that is not a verdict.");
            return AnswerStepResult<bool>.Failed();
        }

        return AnswerStepResult<bool>.Done(supported.GetBoolean());
    }
}
