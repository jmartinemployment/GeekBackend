using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Receptionist;

namespace GeekAPI.Services.Receptionist.Knowledge;

/// <summary>
/// The second half of retrieval: deciding which of the passages the Library found actually answer what the caller asked.
///
/// <para>
/// <b>Why this is a separate step and not a better prompt.</b> A vector search returns what is nearest, not what is right. A caller
/// asking "do you work on tankless heaters?" matches a page about water heater repair whether or not the word tankless appears on it,
/// and the nearest four passages are returned either way because four were asked for. Handing those to the receptionist and relying on
/// it to notice is asking one model call to retrieve, judge and speak at once — and the step it drops is the judging, because the
/// passages look relevant and a caller is waiting. Judged separately, the question is the only thing being asked, and a passage that
/// does not answer it never reaches the turn where an answer gets spoken.
/// </para>
///
/// <para>
/// <b>What this does not do.</b> It judges the passages, not the answer. A receptionist can still overstate a passage it was correctly
/// given — read "we service most tankless models" and say "yes, we do yours". Catching that needs the drafted answer judged against the
/// passages before it is spoken, which the call loop cannot do today because text goes to the caller as soon as the model produces it.
/// That is a real remaining gap and is named here rather than implied away.
/// </para>
///
/// <para>
/// <b>It fails closed.</b> A judge that times out, fails or answers unreadably yields no passages, which the toolbox reports as nothing
/// found and the receptionist turns into taking a message. Passing the passages through unjudged would be a fallback path that salvages
/// an operation with unverified data, which is the one thing this codebase forbids outright.
/// </para>
///
/// <para>Zero Data Retention is unchanged: the question and the passages go to the model to be judged and are held only for the length
/// of the call, and no log line carries either.</para>
/// </summary>
public sealed class KnowledgePassageJudge(IReceptionistModel model, ILogger<KnowledgePassageJudge> logger)
{
    /// <summary>
    /// How long a caller will wait for this. A judge is one extra model round trip in the middle of a telephone call, so it is bounded
    /// tightly and a slow one is treated as a failed one rather than held for.
    /// </summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(4);

    private const string Instruction = """
        You decide which of several passages from a business's own published information actually answer a caller's question.

        You are not answering the caller. You are not summarising. You judge, and nothing else.

        Keep a passage only if someone reading it alone could answer the question from it. Drop a passage that is merely about the same
        subject, mentions the same words, or would need a guess to connect it to what was asked. When in doubt, drop it: a caller being
        told their question will be passed on is a good outcome, and a caller being told something the business never said is not.

        Answer with a JSON object and nothing else: {"keep": [0, 2]} naming the numbers of the passages to keep, in the order given.
        Keep nothing by answering {"keep": []}.
        """;

    /// <summary>
    /// The passages that answer the question, in the order they were given, and which of three things happened. A judge that could not be
    /// run reports <see cref="AnswerStepOutcome.Failed"/> rather than an empty list: "none of these answer it" and "I could not tell" are
    /// opposite facts, and returning the same empty list for both made a timed-out judge indistinguishable from a business that has
    /// published nothing on the subject.
    /// </summary>
    public async Task<AnswerStepResult<IReadOnlyList<T>>> KeepAsync<T>(
        string question,
        IReadOnlyList<T> passages,
        Func<T, string> textOf,
        CancellationToken ct = default)
    {
        if (passages.Count == 0) return AnswerStepResult<IReadOnlyList<T>>.Nothing();

        var asked = new StringBuilder();
        asked.Append("Caller's question: ").Append(question).Append("\n\nPassages:\n");
        for (var i = 0; i < passages.Count; i++)
            asked.Append('[').Append(i).Append("] ").Append(textOf(passages[i])).Append('\n');

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);

        var started = Stopwatch.GetTimestamp();
        ReceptionistReply reply;
        try
        {
            var judging = model
                .ReplyAsync(Instruction, [new ReceptionistTurn(ReceptionistSpeaker.Caller, asked.ToString())], null, budget.Token);

            // The budget is held here rather than trusted to the provider. Cancelling the token only works if the implementation honours
            // it, and a provider that does not would hold a caller on the line for as long as it liked. Racing it means the caller waits
            // the budget at most, whatever the implementation does; the losing request is cancelled on its way out, and the timer is
            // cancelled too so a fast answer does not leave one running.
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var finished = await Task.WhenAny(judging, Task.Delay(Budget, timer.Token)).ConfigureAwait(false);
            await timer.CancelAsync().ConfigureAwait(false);

            // Asked before the timeout is read. The call ending cancels the timer as well, so without this an ended call would be logged
            // as a judge that ran out of time, which never happened.
            ct.ThrowIfCancellationRequested();

            if (finished != judging)
            {
                await budget.CancelAsync().ConfigureAwait(false);
                Abandon(judging);
                logger.LogError("Judging which passages answer a caller's question took longer than {Budget}; none are used.", Budget);
                return AnswerStepResult<IReadOnlyList<T>>.Failed();
            }

            reply = await judging.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The call itself ended. That is not a judge failure and is not logged as one.
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Judging which passages answer a caller's question took longer than {Budget}; none are used.", Budget);
            return AnswerStepResult<IReadOnlyList<T>>.Failed();
        }
        catch (Exception ex)
        {
            logger.LogError("Judging which passages answer a caller's question failed ({Failure}); none are used.", ex.GetType().Name);
            return AnswerStepResult<IReadOnlyList<T>>.Failed();
        }

        var kept = Kept(reply.Text, passages.Count);
        if (kept is null)
        {
            logger.LogError("The passage judge answered with something that is not a verdict; no passages are used.");
            return AnswerStepResult<IReadOnlyList<T>>.Failed();
        }

        logger.LogInformation(
            "{Kept} of {Found} passages answer what was asked, judged in {Elapsed}ms.",
            kept.Count, passages.Count, (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        var answering = kept.Select(i => passages[i]).ToList();
        return answering.Count == 0
            ? AnswerStepResult<IReadOnlyList<T>>.Nothing()
            : AnswerStepResult<IReadOnlyList<T>>.Done(answering);
    }

    /// <summary>
    /// <summary>
    /// Reads whatever becomes of a request nobody is waiting for any more, so a provider that fails after the budget expired does not
    /// raise an unobserved task exception on a background thread.
    /// </summary>
    private static void Abandon(Task task) =>
        _ = task.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    /// <summary>
    /// The passage numbers the judge kept, in the order the passages were given rather than the order it named them. Retrieval ranked
    /// them and the judge was not asked to re-rank; taking its ordering would silently promote whichever it happened to mention first.
    /// Null when its answer was not a verdict. A number outside the range is dropped rather than taken as a reason to discard the whole
    /// verdict: it is the one error here that is safe to narrow, because narrowing keeps fewer passages and keeping fewer is always the
    /// safer direction.
    /// </summary>
    private static List<int>? Kept(string? answer, int count)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;

        // Models fence JSON even when told not to. The object is taken from the first brace to the last.
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var parsed = JsonDocument.Parse(answer[start..(end + 1)]);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!parsed.RootElement.TryGetProperty("keep", out var keep) || keep.ValueKind != JsonValueKind.Array) return null;

            var kept = new List<int>();
            foreach (var item in keep.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var index)) continue;
                if (index < 0 || index >= count) continue;
                if (!kept.Contains(index)) kept.Add(index);
            }
            kept.Sort();
            return kept;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
