using System.Diagnostics;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.Receptionist.Knowledge;

/// <summary>Why a caller is not being answered from the business's own information. Every one of them means the same thing to the receptionist: take a message.</summary>
public static class AnswerRefusals
{
    /// <summary>What the caller said was not a question about the business - a greeting, a name, small talk.</summary>
    public const string NothingAsked = "nothing_asked";

    /// <summary>The business has not published anything that answers it.</summary>
    public const string NotPublished = "not_published";

    /// <summary>An answer was written and the business's own words did not support it. The answer is discarded, not spoken.</summary>
    public const string Unsupported = "unsupported";

    /// <summary>A step could not be run - the library or a model failed or was too slow. Nothing was established either way.</summary>
    public const string CouldNotCheck = "could_not_check";
}

/// <summary>What came of trying to answer one caller's question: the words to say, or why there are none.</summary>
public readonly record struct KnowledgeAnswer(string? Answer, string? Refusal)
{
    public static KnowledgeAnswer Say(string answer) => new(answer, null);
    public static KnowledgeAnswer Refuse(string refusal) => new(null, refusal);
    public bool Answered => Answer is not null;
}

/// <summary>
/// The coordinator for answering a caller's question. It plans, delegates, holds the state of the attempt, and decides every step.
///
/// <para>
/// <b>Why a coordinator and not a chain.</b> Chaining the workers in code puts the decisions inside them: a formulator returning
/// nothing ends the attempt, a judge keeping nothing ends the attempt, and each worker's return value quietly becomes the next move.
/// Then nobody can plan. Nothing can search a second time with a broader question when the first one was too narrow, nothing can ask
/// for a rewrite when the grounding check rejects an overreach, and nothing can tell "the business has not published this" apart from
/// "a model timed out" - which are opposite facts that deserve opposite handling.
/// </para>
///
/// <para>
/// <b>The workers decide nothing.</b> Each does one job and reports which of three things happened: it has a result, it honestly has
/// nothing, or it could not do its job. Every branch below is taken here.
/// </para>
///
/// <para>
/// <b>Two retries, each for a different reason, each once.</b> A search that comes back with nothing worth keeping is asked again with a
/// broader question, because the commonest cause is a question too narrow to match anything. An answer the grounding check rejects is
/// rewritten once, told what was rejected, because the commonest cause is one sentence that reached past the passages. Neither is a
/// fallback that salvages an operation with worse data: both end at the same refusal if they fail again.
/// </para>
///
/// <para>
/// <b>It fails closed and it is bounded.</b> Anything unestablished is a refusal, never a guess. The whole attempt is held to
/// <see cref="Budget"/>, because every branch here is a model call and a caller is on the line.
/// </para>
///
/// <para>Zero Data Retention is unchanged: the question, the passages and the draft exist for the length of the attempt, and no log line
/// carries any of them.</para>
/// </summary>
public sealed class KnowledgeOrchestrator(
    QuestionFormulator formulator,
    KnowledgePassageJudge judge,
    AnswerComposer composer,
    AnswerGrounding grounding,
    ILogger<KnowledgeOrchestrator> logger)
{
    /// <summary>
    /// The longest a caller waits for all of this together. It is the backstop, not the plan: the steps' own budgets add up to less than
    /// this, so a run that reaches here has something genuinely wrong rather than merely slow.
    /// </summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(15);

    /// <summary>The longest one search may take. The library is the only step here that is not a model call, and it needs a bound too.</summary>
    public static readonly TimeSpan SearchBudget = TimeSpan.FromSeconds(3);

    /// <summary>Passages asked of the library per search.</summary>
    internal const int TopK = 4;

    /// <summary>Paragraphs kept from each page. Enough to answer from, few enough to stay inside a spoken reply.</summary>
    internal const int ParagraphsPerPage = 3;

    /// <summary>
    /// What to say to this caller, or why there is nothing to say. The only entry point; every decision about this attempt is made
    /// inside it.
    /// </summary>
    public async Task<KnowledgeAnswer> AnswerAsync(
        IReadOnlyList<ReceptionistTurn> turns, Guid runId, IGeekCrawlerRagClient rag, CancellationToken ct = default)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attempt.CancelAfter(Budget);
        var started = Stopwatch.GetTimestamp();

        try
        {
            return await PlanAsync(turns, runId, rag, attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("Answering a caller took longer than {Budget} altogether; a message is taken instead.", Budget);
            return KnowledgeAnswer.Refuse(AnswerRefusals.CouldNotCheck);
        }
        finally
        {
            logger.LogInformation("Answering a caller took {Elapsed}ms.", (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private async Task<KnowledgeAnswer> PlanAsync(
        IReadOnlyList<ReceptionistTurn> turns, Guid runId, IGeekCrawlerRagClient rag, CancellationToken ct)
    {
        // 1. What is actually being asked.
        var asked = await formulator.QuestionAsync(turns, null, ct).ConfigureAwait(false);
        switch (asked.Outcome)
        {
            case AnswerStepOutcome.Nothing:
                return KnowledgeAnswer.Refuse(AnswerRefusals.NothingAsked);
            case AnswerStepOutcome.Failed:
                return KnowledgeAnswer.Refuse(AnswerRefusals.CouldNotCheck);
        }

        var question = asked.Value!;

        // 2 and 3. Search, and keep only what answers it. A search that keeps nothing is asked again, once, more broadly: the usual
        // cause is a question narrower than anything the business wrote, and that is worth one more round trip rather than a message.
        var passages = await FindAsync(question, runId, rag, ct).ConfigureAwait(false);
        if (passages.Outcome == AnswerStepOutcome.Failed) return KnowledgeAnswer.Refuse(AnswerRefusals.CouldNotCheck);

        if (passages.Outcome == AnswerStepOutcome.Nothing)
        {
            var broader = await formulator.QuestionAsync(turns, question, ct).ConfigureAwait(false);
            if (broader.Outcome != AnswerStepOutcome.Done
                || string.Equals(broader.Value, question, StringComparison.OrdinalIgnoreCase))
                return KnowledgeAnswer.Refuse(AnswerRefusals.NotPublished);

            logger.LogInformation("A caller's question matched nothing; it is being asked of the library a second, broader way.");
            passages = await FindAsync(broader.Value!, runId, rag, ct).ConfigureAwait(false);
            if (passages.Outcome != AnswerStepOutcome.Done)
                return KnowledgeAnswer.Refuse(
                    passages.Outcome == AnswerStepOutcome.Failed ? AnswerRefusals.CouldNotCheck : AnswerRefusals.NotPublished);

            question = broader.Value!;
        }

        var kept = passages.Value!;

        // 4 and 5. Write it, then check it against the same passages with a different call. A rejected answer is rewritten once, told
        // what was rejected; the usual cause is one sentence reaching past the passages, not a wrong retrieval.
        string? rejected = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var draft = await composer.AnswerAsync(question, kept, rejected, ct).ConfigureAwait(false);
            if (draft.Outcome == AnswerStepOutcome.Nothing) return KnowledgeAnswer.Refuse(AnswerRefusals.NotPublished);
            if (draft.Outcome == AnswerStepOutcome.Failed) return KnowledgeAnswer.Refuse(AnswerRefusals.CouldNotCheck);

            var supported = await grounding.SupportsAsync(draft.Value!, kept, ct).ConfigureAwait(false);
            if (supported.Outcome != AnswerStepOutcome.Done) return KnowledgeAnswer.Refuse(AnswerRefusals.CouldNotCheck);
            if (supported.Value) return KnowledgeAnswer.Say(draft.Value!);

            logger.LogWarning("An answer was not supported by the business's own words; it is not said to the caller.");
            rejected = draft.Value;
        }

        return KnowledgeAnswer.Refuse(AnswerRefusals.Unsupported);
    }

    /// <summary>
    /// One search and its judging, as one fact for the plan above: passages worth answering from, honestly nothing, or a step that
    /// could not be run. The library failing and the library finding nothing are different, and only the first is a failure.
    /// </summary>
    private async Task<AnswerStepResult<IReadOnlyList<string>>> FindAsync(
        string question, Guid runId, IGeekCrawlerRagClient rag, CancellationToken ct)
    {
        // Bounded here for the same reason every model call is: a client that does not honour the token, or an HttpClient with no
        // timeout, would hold a caller for as long as it liked and the attempt's own budget would never get a turn. This was the one
        // step in the plan with nothing holding it.
        GeekCrawlerRagQueryResult? found;
        using (var timer = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            var searching = rag.QueryAsync(new GeekCrawlerRagQuery(question, runId, TopK: TopK), ct);
            var finished = await Task.WhenAny(searching, Task.Delay(SearchBudget, timer.Token)).ConfigureAwait(false);
            await timer.CancelAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (finished != searching)
            {
                logger.LogError("Searching the business's information took longer than {Budget}.", SearchBudget);
                return AnswerStepResult<IReadOnlyList<string>>.Failed();
            }

            found = await searching.ConfigureAwait(false);
        }

        if (found is null || found.Failed)
        {
            logger.LogError("The business's information could not be searched.");
            return AnswerStepResult<IReadOnlyList<string>>.Failed();
        }

        var texts = Passages(found.Pages);
        if (texts.Count == 0) return AnswerStepResult<IReadOnlyList<string>>.Nothing();

        // The judge's own three outcomes, passed through rather than flattened. A judge that could not be run is not a business that
        // has published nothing.
        return await judge.KeepAsync(question, texts, p => p, ct).ConfigureAwait(false);
    }

    /// <summary>What the library returned, as plain text to answer from. Headings, anchors, digests and crawl timestamps are the content pipeline's provenance needs and are nothing to a caller waiting on the line.</summary>
    private static List<string> Passages(IReadOnlyList<GccQuoteablePage> pages)
    {
        var passages = new List<string>(pages.Count);
        foreach (var page in pages)
        {
            var text = string.Join(
                " ", page.Paragraphs.Where(p => !string.IsNullOrWhiteSpace(p)).Take(ParagraphsPerPage).Select(p => p.Trim()));
            if (text.Length == 0) continue;
            passages.Add(string.IsNullOrWhiteSpace(page.Title) ? text : $"{page.Title}: {text}");
        }
        return passages;
    }
}
