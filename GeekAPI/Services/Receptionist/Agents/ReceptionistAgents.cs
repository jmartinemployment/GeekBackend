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
    private readonly Dictionary<string, HandoffToolbox?> _handoffs;
    private readonly Dictionary<string, IReceptionistToolbox?> _toolboxes;
    private readonly ILogger _logger;

    private ReceptionistAgents(
        IReadOnlyList<ReceptionistAgent> agents,
        Dictionary<string, HandoffToolbox?> handoffs,
        Dictionary<string, IReceptionistToolbox?> toolboxes,
        ReceptionistAgent current,
        ILogger logger)
    {
        Roles = agents;
        _handoffs = handoffs;
        _toolboxes = toolboxes;
        Current = current;
        _logger = logger;
    }

    /// <summary>Every role this business has, in the order they were given. The first greets.</summary>
    public IReadOnlyList<ReceptionistAgent> Roles { get; }

    /// <summary>The role speaking to the caller now. Every call starts with the first of the roster, which is the one that greets.</summary>
    public ReceptionistAgent Current { get; private set; }

    /// <summary>What <see cref="Current"/> may do: its own tools and, when there is anywhere to go, the handoff.</summary>
    public IReceptionistToolbox? Toolbox => _toolboxes[Current.Name];

    /// <summary>How many times this call has changed hands. Metadata about the call, never about what was said in it.</summary>
    public int HandoffsMade { get; private set; }

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
    /// Where the speaking role has asked for this caller to go, if anywhere, and clears the ask. It is a request and not a move: the
    /// director decides, and one thing deciding is the whole reason this does not move anybody itself.
    /// </summary>
    public string? TakeRequest()
    {
        if (_handoffs[Current.Name] is not { } handoff) return null;
        var requested = handoff.Requested;
        handoff.Clear();
        return requested;
    }

    /// <summary>
    /// Puts the caller with this role, as the director decided. False when there is no such role, which leaves the caller where they
    /// are rather than anywhere invented.
    /// </summary>
    public bool MoveTo(string roleName)
    {
        var next = Roles.FirstOrDefault(a => string.Equals(a.Name, roleName, StringComparison.Ordinal));
        // By name, not by reference. One name is one instance today only because Of de-duplicates, and a check that says what it means
        // survives a change to how the roster is built.
        if (next is null || string.Equals(next.Name, Current.Name, StringComparison.Ordinal)) return false;

        HandoffsMade++;
        _logger.LogInformation("A call changed hands from {From} to {To}.", Current.Name, next.Name);
        Current = next;

        // Anything the arriving role asked for the last time it spoke is forgotten on the way in. Otherwise a caller who comes back to a
        // role would be routed by an ask that was already answered, which is closed today only by the order of two lines in the loop.
        _handoffs[Current.Name]?.Clear();
        return true;
    }
}
