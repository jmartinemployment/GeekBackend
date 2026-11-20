using GeekApplication.Interfaces.Receptionist;

namespace GeekAPI.Services.Receptionist.Agents;

/// <summary>
/// One role a receptionist plays during a call — greeting and routing, answering from the business's own information, booking,
/// taking a message, handing to a person — with the instructions and the tools for that role and nothing else.
///
/// <para>
/// <b>Why roles rather than one receptionist.</b> A single prompt carrying every rule for every part of a call is the thing that goes
/// wrong on the turn that matters: the booking rules are in context while the caller is asking a price, the message-taking script is in
/// context while the caller is booking, and the model picks the wrong one. A role carries only what its part of the call needs, so there
/// is less to pick wrongly from, and the business can be given a part without being given the rest.
/// </para>
///
/// <para>
/// <b>What it costs.</b> One model round trip at the moment of handoff, with nothing spoken. The call loop already continues after a
/// tool call without speaking, so a handoff rides that path and the caller hears no pause for it. A handoff is not free and is therefore
/// not a thing to do twice in one turn — <see cref="ReceptionistAgents.MaxHandoffs"/> bounds it.
/// </para>
///
/// <para>Zero Data Retention is unchanged: a role is instructions and tools, the turns stay in RAM and are shared across roles so a
/// caller never repeats themselves, and nothing a role was told is written anywhere.</para>
/// </summary>
/// <param name="Name">The role's own name, which is what a handoff names and what a log line may carry. Never a caller's words.</param>
/// <param name="Purpose">What this role is for, in one line, as the other roles are told it when deciding where to hand off.</param>
/// <param name="SystemPrompt">Everything this role is told, including the business's own script. It never sees another role's rules.</param>
/// <param name="Toolbox">What this role may do. Null is a role that can only talk, which is the right toolbox for a greeter.</param>
public sealed record ReceptionistAgent(
    string Name,
    string Purpose,
    string SystemPrompt,
    IReceptionistToolbox? Toolbox);

/// <summary>The roles this product knows how to play. A business is given some of them; the names are fixed so a handoff cannot name a role that does not exist.</summary>
public static class ReceptionistRoles
{
    /// <summary>Opens the call, works out what the caller wants, and hands to whoever does it. Every call starts here.</summary>
    public const string Greeter = "greeter";

    /// <summary>Answers what the caller asks about the business, from the business's own published information and nothing else.</summary>
    public const string Answerer = "answerer";

    /// <summary>Qualifies and books an appointment in the business's calendar.</summary>
    public const string Booker = "booker";

    /// <summary>Takes a name, a number and what it is about, for a business that will call back.</summary>
    public const string MessageTaker = "message_taker";

    public static readonly IReadOnlyList<string> All = [Greeter, Answerer, Booker, MessageTaker];
}
