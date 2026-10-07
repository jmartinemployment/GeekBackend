using System.Collections.Concurrent;

namespace GeekAPI.Services.Workflow.Services;

/// <summary>One reply that needed a repair before it parsed: which call, and which named repairs.</summary>
public sealed record JsonRepairNote(string Label, IReadOnlyList<string> Repairs);

/// <summary>
/// The repairs <see cref="JsonReplySanitizer"/> applied to model replies while a flow ran, so a run's record can
/// show them.
/// </summary>
/// <remarks>
/// <para>
/// The parser is synchronous and static, so it cannot await the run log. It notes each applied repair here, and the
/// code that owns the run's record (<c>GccGenerationCoordinator.AttemptAsync</c>) drains the notes into a
/// <c>repaired</c> event. A scope is per piece: everything the piece awaits, including a tool page's partner
/// fan-out, writes into the same scope, and two pieces running side by side never see each other's notes.
/// </para>
/// <para>
/// With no scope open (the Workflow product, which has no run log) a note goes nowhere and the repair is applied
/// as it always was.
/// </para>
/// </remarks>
public static class JsonRepairTrace
{
    private static readonly AsyncLocal<Scope?> Current = new();

    /// <summary>Opens a scope for the calling flow and everything it awaits from here.</summary>
    public static Scope Begin()
    {
        var scope = new Scope(Current.Value);
        Current.Value = scope;
        return scope;
    }

    internal static void Note(string label, IReadOnlyList<string> repairs)
    {
        if (repairs.Count == 0)
        {
            return;
        }

        Current.Value?.Add(new JsonRepairNote(label, repairs));
    }

    public sealed class Scope(Scope? previous) : IDisposable
    {
        private readonly ConcurrentQueue<JsonRepairNote> _notes = new();

        internal void Add(JsonRepairNote note) => _notes.Enqueue(note);

        /// <summary>Takes every note written so far, in the order they were written.</summary>
        public IReadOnlyList<JsonRepairNote> Drain()
        {
            var taken = new List<JsonRepairNote>();
            while (_notes.TryDequeue(out var note))
            {
                taken.Add(note);
            }

            return taken;
        }

        public void Dispose() => Current.Value = previous;
    }
}
