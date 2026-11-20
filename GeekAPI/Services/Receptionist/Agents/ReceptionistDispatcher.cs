using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Receptionist;

namespace GeekAPI.Services.Receptionist.Agents;

/// <summary>
/// Who takes the caller's next turn. One thing decides, every turn.
///
/// <para>
/// <b>Why a director rather than roles handing to each other.</b> Peer handoff puts routing in every role's prompt - each one has to
/// know what the others are for, and judge when to let go - which is the bleed that roles existed to stop. It also lets two roles pass a
/// caller back and forth, which is why the first version of this carried a handoff counter: a bound is a counter papering over a
/// structure that permits the loop. One director cannot volley with itself, so the counter is not needed and is gone.
/// </para>
///
/// <para>
/// <b>A role asks; it does not decide.</b> <see cref="HandoffToolbox"/> validates a destination and records the request - it never
/// swapped the role, by design. The director honours an explicit request without asking a model, because a role that has just told the
/// caller it is putting them through is the best evidence available and spending a round trip to second-guess it would be latency for
/// nothing.
/// </para>
///
/// <para>
/// <b>It stays put by default.</b> A caller mid-booking saying "and what does that cost?" is still booking. The director only asks a
/// model when the speaking role has no claim on the turn, so an ordinary conversation costs nothing at all and routing is paid for only
/// where it is genuinely in doubt.
/// </para>
///
/// <para>
/// <b>It fails to the floor, not to a guess.</b> A model that times out, fails or answers with a role that does not exist leaves the
/// caller with whoever is already speaking. Nobody is moved on a failed decision, and nothing invents a destination.
/// </para>
///
/// <para>Zero Data Retention is unchanged: what the caller said goes to the model to be routed and is held only for the call, and no log
/// line carries any of it - only role names.</para>
/// </summary>
public sealed class ReceptionistDispatcher(IReceptionistModel model, ILogger<ReceptionistDispatcher> logger)
{
    /// <summary>How long a caller waits to be routed. Shorter than the judge's: this runs before anyone has said anything back.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);

    /// <summary>How many of the most recent turns the director reads. Enough for what the caller just asked and the shape of the call, and no more.</summary>
    internal const int TurnsRead = 6;

    private const string Instruction = """
        You route a telephone caller to the part of a business's reception that handles what they are asking for now.

        You are not answering the caller. You do not greet, book, or take a message. You choose, and nothing else.

        Prefer the part that is already speaking: a caller partway through one thing who asks a small question about it has not changed
        what they want. Move them only when what they are asking for plainly belongs somewhere else.

        Answer with a JSON object and nothing else: {"role": "<name>"} naming one of the parts you were given.
        """;

    /// <summary>
    /// The role that should take this turn. Returns null when the caller stays with whoever is speaking, which is the ordinary answer
    /// and the answer to every failure.
    /// </summary>
    public async Task<string?> RouteAsync(
        ReceptionistAgent current,
        IReadOnlyList<ReceptionistAgent> roles,
        IReadOnlyList<ReceptionistTurn> turns,
        string? requested,
        CancellationToken ct = default)
    {
        // A role that asked is taken at its word. It has usually just told the caller so out loud.
        if (requested is not null)
        {
            if (string.Equals(requested, current.Name, StringComparison.Ordinal))
            {
                // Asking to stay is not an error. A role repeating a handoff it already made is ordinary, and logging it like an invalid
                // destination would make a benign repeat indistinguishable from a role naming somewhere that does not exist.
                logger.LogInformation("{Role} asked for itself; the caller is already there.", current.Name);
                return null;
            }

            if (roles.Any(r => string.Equals(r.Name, requested, StringComparison.Ordinal)))
            {
                logger.LogInformation("{From} asked for {To} and the caller goes there.", current.Name, requested);
                return requested;
            }

            logger.LogError("{From} asked for {To}, which is not somewhere this caller can go; they stay where they are.", current.Name, requested);
            return null;
        }

        var others = roles.Where(r => !string.Equals(r.Name, current.Name, StringComparison.Ordinal)).ToList();
        if (others.Count == 0) return null;

        var asked = new StringBuilder();
        asked.Append("Parts of reception:\n");
        foreach (var role in roles)
            asked.Append("- ").Append(role.Name).Append(": ").Append(role.Purpose)
                 .Append(string.Equals(role.Name, current.Name, StringComparison.Ordinal) ? " (speaking now)" : string.Empty).Append('\n');

        asked.Append("\nThe call so far:\n");
        foreach (var turn in turns.TakeLast(TurnsRead))
        {
            if (turn.Speaker is ReceptionistSpeaker.ToolCall or ReceptionistSpeaker.ToolResult) continue;
            asked.Append(turn.Speaker == ReceptionistSpeaker.Caller ? "Caller: " : "Reception: ").Append(turn.Text).Append('\n');
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);

        var started = Stopwatch.GetTimestamp();
        ReceptionistReply reply;
        try
        {
            var routing = model.ReplyAsync(Instruction, [new ReceptionistTurn(ReceptionistSpeaker.Caller, asked.ToString())], null, budget.Token);

            // Held here rather than trusted to the provider, for the same reason the passage judge holds its own: cancelling a token
            // only works if the implementation honours it, and a caller is on the line. The timer is cancelled either way, so a decision
            // that came back fast does not leave one running for the rest of the budget.
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var finished = await Task.WhenAny(routing, Task.Delay(Budget, timer.Token)).ConfigureAwait(false);
            await timer.CancelAsync().ConfigureAwait(false);

            // Asked before the timeout is read. The call ending cancels the timer too, so without this an ended call would be reported as
            // a routing timeout that never happened - and a provider that returns a cancelled task rather than throwing would make which
            // of the two it looked like a race.
            ct.ThrowIfCancellationRequested();

            if (finished != routing)
            {
                await budget.CancelAsync().ConfigureAwait(false);
                Abandon(routing);
                logger.LogError("Routing a caller took longer than {Budget}; they stay with {Role}.", Budget, current.Name);
                return null;
            }

            reply = await routing.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("Routing a caller failed ({Failure}); they stay with {Role}.", ex.GetType().Name, current.Name);
            return null;
        }

        var chosen = Chosen(reply.Text);
        if (chosen is null)
        {
            logger.LogError("The director answered with something that is not a choice; the caller stays with {Role}.", current.Name);
            return null;
        }

        if (string.Equals(chosen, current.Name, StringComparison.Ordinal)) return null;

        if (!roles.Any(r => string.Equals(r.Name, chosen, StringComparison.Ordinal)))
        {
            logger.LogError("The director named a part of reception that does not exist; the caller stays with {Role}.", current.Name);
            return null;
        }

        logger.LogInformation(
            "A caller moves from {From} to {To}, decided in {Elapsed}ms.",
            current.Name, chosen, (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return chosen;
    }

    /// <summary>
    /// Reads whatever becomes of a request nobody is waiting for any more, so a provider that fails after the budget expired does not
    /// raise an unobserved task exception on a background thread.
    /// </summary>
    private static void Abandon(Task task) =>
        _ = task.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    /// <summary>The role the director named, or null when its answer was not a choice. Models fence JSON; the object is taken from the first brace to the last.</summary>
    private static string? Chosen(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;

        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var parsed = JsonDocument.Parse(answer[start..(end + 1)]);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!parsed.RootElement.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String) return null;
            var name = role.GetString();
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
