using GeekAPI.Services.Receptionist.Knowledge;
using GeekApplication.Interfaces.Receptionist;

namespace GeekAPI.Services.Receptionist.Agents;

/// <summary>
/// The roles one business's reception is made of, and which of them is speaking to the caller now.
///
/// <para>
/// This owns the handoff, because the conversation is one thing: the roles share the caller's turns so nobody is asked to repeat
/// themselves, and exactly one role holds the turn at a time. A toolbox that swapped the role itself would make two things responsible
/// for who is speaking.
/// </para>
///
/// <para>
/// <b>One role is not a special case.</b> A business given a single role gets that role with no handoff tool at all, and the call loop
/// runs as it did before roles existed. There is no mode and no flag: the roster is simply short.
/// </para>
/// </summary>
public sealed class ReceptionistAgents
{
    /// <summary>
    /// How many times one call may change hands. A caller who has been passed four times is not being helped by a fifth, and an
    /// unbounded handoff is a pair of roles that can hand to each other until the call ends. Past this the caller stays where they are
    /// and the role is told so.
    /// </summary>
    public const int MaxHandoffs = 4;

    private readonly IReadOnlyList<ReceptionistAgent> _agents;
    private readonly Dictionary<string, HandoffToolbox?> _handoffs;
    private readonly Dictionary<string, IReceptionistToolbox?> _toolboxes;
    private readonly ILogger _logger;
    private int _handoffsMade;

    private ReceptionistAgents(
        IReadOnlyList<ReceptionistAgent> agents,
        Dictionary<string, HandoffToolbox?> handoffs,
        Dictionary<string, IReceptionistToolbox?> toolboxes,
        ReceptionistAgent current,
        ILogger logger)
    {
        _agents = agents;
        _handoffs = handoffs;
        _toolboxes = toolboxes;
        Current = current;
        _logger = logger;
    }

    /// <summary>The role speaking to the caller now. Every call starts with the first of the roster, which is the one that greets.</summary>
    public ReceptionistAgent Current { get; private set; }

    /// <summary>What <see cref="Current"/> may do: its own tools and, when there is anywhere to go, the handoff.</summary>
    public IReceptionistToolbox? Toolbox => _toolboxes[Current.Name];

    /// <summary>How many times this call has changed hands. Metadata about the call, never about what was said in it.</summary>
    public int HandoffsMade => _handoffsMade;

    /// <summary>
    /// A reception made of these roles, in order — the first greets. Null when there are none, which is a business whose receptionist
    /// cannot be built and is the caller's problem to be told about, not a thing to paper over with an empty roster.
    /// </summary>
    public static ReceptionistAgents? Of(IReadOnlyList<ReceptionistAgent> agents, ILogger logger)
    {
        if (agents.Count == 0) return null;

        var byName = new Dictionary<string, ReceptionistAgent>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            // Two roles of one name would make a handoff ambiguous, and the second would be unreachable. The first wins and the
            // duplicate is dropped rather than silently shadowing it.
            if (!byName.TryAdd(agent.Name, agent))
                logger.LogError("Two reception roles are both named {Role}; the second is not used.", agent.Name);
        }

        var kept = byName.Values.ToList();
        var handoffs = new Dictionary<string, HandoffToolbox?>(StringComparer.Ordinal);
        var toolboxes = new Dictionary<string, IReceptionistToolbox?>(StringComparer.Ordinal);

        foreach (var agent in kept)
        {
            // A role is never offered a handoff to itself: it is already there, and offering it is an invitation to spend a turn on it.
            var destinations = kept
                .Where(other => !string.Equals(other.Name, agent.Name, StringComparison.Ordinal))
                .ToDictionary(other => other.Name, other => other.Purpose, StringComparer.Ordinal);

            var handoff = HandoffToolbox.For(destinations);
            handoffs[agent.Name] = handoff;
            toolboxes[agent.Name] = CompositeToolbox.Of(agent.Toolbox, handoff);
        }

        return new ReceptionistAgents(kept, handoffs, toolboxes, kept[0], logger);
    }

    /// <summary>
    /// Whether the tool just run was a handoff that asked for somewhere real, and if so, moves the caller there. Called after every tool,
    /// so the loop never has to know which toolbox a tool came from.
    /// </summary>
    public bool TookOver(string toolName)
    {
        if (!string.Equals(toolName, HandoffToolbox.Tool, StringComparison.Ordinal)) return false;
        if (_handoffs[Current.Name] is not { Requested: { } requested }) return false;

        if (_handoffsMade >= MaxHandoffs)
        {
            _logger.LogWarning("A call has already changed hands {Count} times; it stays with {Role}.", _handoffsMade, Current.Name);
            return false;
        }

        var next = _agents.FirstOrDefault(a => string.Equals(a.Name, requested, StringComparison.Ordinal));
        if (next is null) return false;

        _handoffsMade++;
        _logger.LogInformation("A call changed hands from {From} to {To}.", Current.Name, next.Name);
        Current = next;
        return true;
    }
}
