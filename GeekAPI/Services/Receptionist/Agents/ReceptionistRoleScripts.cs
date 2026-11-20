using GeekAPI.Services.Receptionist.Relay;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Receptionist.Agents;

/// <summary>
/// The roles one business's reception is actually made of, built from what that business has: its own script, its indexed site if it has
/// one, its calendar if it has one and asked for booking.
///
/// <para>
/// <b>There is no fixed roster.</b> A role exists because the business has the thing that role needs — the answerer because there is
/// something to look up, the booker because there is a calendar and booking was turned on — so a role is never offered a tool it does not
/// have, and a caller is never handed to a part of reception that cannot do anything. A business with neither gets two roles: the one that
/// opens the call and the one that takes a message. The greeter is first because the roster's first role is the one that is speaking when
/// the call connects (<see cref="ReceptionistAgents.Of"/>), and the message taker is last because it is the floor: every part of a call
/// that nobody else here handles ends there.
/// </para>
///
/// <para>
/// <b>Every role carries the whole of the business's own prompt.</b> A role sees only its own instructions — never another role's, and
/// never a shared preamble the loop might have added — so the business's script, the greeting the caller already heard, the opening hours
/// and the never-invent rule are assembled into each one. They are assembled by <see cref="ReceptionistConsent.SystemPrompt"/>, the one
/// place that assembles them, rather than copied here: a second assembler would drift from the first, and the half that drifted would be
/// the half that tells a receptionist not to invent a price. What this class adds is the one paragraph that says which part of the call
/// this role is handling.
/// </para>
///
/// <para>
/// <b>Why each role's paragraph is short.</b> The point of a role is that there is less for the model to pick wrongly from on the turn
/// that matters. A role paragraph that listed every rule for every part of the call would put the booking rules back in context while the
/// caller is asking a price, which is the failure roles exist to remove.
/// </para>
///
/// <para>
/// Zero Data Retention is untouched: this builds instructions and hands over toolboxes that were already built. Nothing here reads, writes
/// or logs anything, so nothing a caller says can reach a log line through it.
/// </para>
/// </summary>
public static class ReceptionistRoleScripts
{
    /// <summary>What the other roles are told the greeter is for, when they are offered it as a handoff destination.</summary>
    public const string GreeterPurpose = "opens the call and works out what the caller wants";

    /// <summary>What the other roles are told the answerer is for.</summary>
    public const string AnswererPurpose = "answers questions about this business from its own published information";

    /// <summary>What the other roles are told the booker is for.</summary>
    public const string BookerPurpose = "checks this business's calendar and books an appointment";

    /// <summary>What the other roles are told the message taker is for.</summary>
    public const string MessageTakerPurpose = "takes a name, a callback number and what the call is about, for someone to call back";

    /// <summary>
    /// The greeter's own paragraph. It is given no toolbox, so what it is told it cannot do is also what it has no way to do: the
    /// instruction and the capability agree, and a greeter that tried anyway would be refused by a toolbox that was never offered.
    /// </summary>
    internal const string GreeterScript = """
        Your part of this call is the opening. The caller has already heard the greeting above, so do not greet them again. Find out in one
        or two short questions what they are calling about, then hand them to the part of reception that handles it, using the hand off
        tool. Hand off as soon as you know what they want. The caller hears nothing while you do it and does not have to repeat anything
        they have already told you.

        You do not answer questions about this business, you do not quote a price, you do not check a calendar and you do not take a
        message yourself. You have no tools for any of that, and the part you hand to does it properly. If what the caller wants is not
        something a part of reception here handles, hand them to the message taker. Keep what you say to a sentence or two.
        """;

    /// <summary>
    /// The answerer's own paragraph. The lookup tool is named because it is the only tool this role has, and the rule that it may say
    /// nothing a lookup did not return is the whole reason the role exists: a caller on a phone cannot see a citation and has no way to
    /// tell an invented price from a real one.
    /// </summary>
    internal const string AnswererScript = """
        Your part of this call is answering what the caller asks about this business. You have one tool, look_up_business_info. Use it for
        every question about this business - what it offers, what it charges, where it works, how it works, its policies - and answer only
        from what it gives back. Every fact you say about this business must come from a lookup you have just made or from what you were
        given above. Never fill a gap with what a business like this one usually does.

        If a lookup finds nothing, or says the information could not be searched, say plainly that you do not have that to hand and that
        you will take a message, then hand to the message taker. Do the same if the caller wants an appointment, wants a person, or asks
        for something that is not about this business. Answer in a sentence or two, the way someone at the front desk would.
        """;

    /// <summary>
    /// The booker's own paragraph. Booking spends the caller's time and puts a row in the owner's calendar, so the confirmation is read
    /// back before it is made, and every way the calendar can fail ends the same way: the caller is not told it is booked.
    /// </summary>
    internal const string BookerScript = """
        Your part of this call is booking an appointment in this business's calendar. Find out what the appointment is for and when suits
        the caller, then check the calendar before you offer a time. Never offer a time you have not checked, and never offer one outside
        the opening hours above.

        Take the caller's name and the best number to reach them on. Read the day, the date and the time back to them and get a yes before
        you book anything. Book only the time they agreed to, and book it once.

        If the calendar cannot be read, if the booking does not go through, or if you are told the outcome could not be established, do not
        tell the caller the appointment is booked. Say that you will have someone call them back to confirm the time, and hand to the
        message taker. Do not quote prices or answer questions about this business beyond the appointment itself.
        """;

    /// <summary>
    /// The message taker's own paragraph. This role is the floor, so it is told not to hand on: a caller passed from the last role that
    /// can help them is a caller going in a circle until the handoff bound stops it.
    /// </summary>
    internal const string MessageTakerScript = """
        Your part of this call is taking a message for someone at this business to act on. Collect three things, asking for one at a time:
        the caller's name, the best number to call them back on, and what the call is about in their own words. Read the number back to
        them digit by digit and get a yes. If the caller has already given you any of it earlier in this call, do not ask for it again -
        say it back and confirm it.

        Do not promise when someone will call back, do not quote a price, and do not say anything about this business you were not given
        above. Tell the caller the message is going to the team and that someone will get back to them, then thank them and end the call.

        Everything this reception does not otherwise handle ends with you, so do not hand the caller on for something you can take a
        message about.
        """;

    /// <summary>
    /// The roles for this business, greeter first and message taker last, or the failure that stopped the business's own prompt being
    /// assembled - a missing script or greeting, or hours or qualifying questions in a shape the receptionist does not read. The failure
    /// is passed on rather than worked around: a reception built on half a prompt is the receptionist answering from nothing, which is the
    /// one thing this product may not do.
    /// </summary>
    /// <param name="config">The business's own receptionist settings, which carry its script, its hours and whether it books.</param>
    /// <param name="knowledge">The lookup toolbox, or null for a business with no indexed site; null is what removes the answerer.</param>
    /// <param name="calendar">The calendar toolbox, or null for a business with no connected calendar; null is what removes the booker.</param>
    /// <param name="spokenGreeting">What the caller actually heard as the call connected, when that is not the stored greeting.</param>
    public static Result<IReadOnlyList<ReceptionistAgent>> For(
        VoiceReceptionistConfig config,
        IReceptionistToolbox? knowledge,
        IReceptionistToolbox? calendar,
        string? spokenGreeting = null)
    {
        var assembled = ReceptionistConsent.SystemPrompt(config, spokenGreeting);
        if (!assembled.IsSuccess)
            return Result<IReadOnlyList<ReceptionistAgent>>.Failure(assembled.Error!, assembled.ErrorCode!);

        var business = assembled.Value!;

        var roles = new List<ReceptionistAgent>(ReceptionistRoles.All.Count)
        {
            new(ReceptionistRoles.Greeter, GreeterPurpose, PromptFor(business, GreeterScript), null),
        };

        // A toolbox with nothing in it is the same as no toolbox: a role told it has a lookup tool that is not offered would be a role
        // whose first instruction is a claim the call cannot honour.
        if (Offers(knowledge))
            roles.Add(new ReceptionistAgent(ReceptionistRoles.Answerer, AnswererPurpose, PromptFor(business, AnswererScript), knowledge));

        // Booking is the one thing a receptionist does that spends the owner's calendar, so it takes both the business asking for it and
        // a calendar to do it in. Either one missing is a business whose callers are offered a message instead.
        if (config.BookingEnabled && Offers(calendar))
            roles.Add(new ReceptionistAgent(ReceptionistRoles.Booker, BookerPurpose, PromptFor(business, BookerScript), calendar));

        roles.Add(new ReceptionistAgent(ReceptionistRoles.MessageTaker, MessageTakerPurpose, PromptFor(business, MessageTakerScript), null));

        return Result<IReadOnlyList<ReceptionistAgent>>.Success(roles);
    }

    /// <summary>Whether a toolbox is worth giving a role: present, and offering at least one tool.</summary>
    private static bool Offers(IReceptionistToolbox? toolbox) => toolbox is { Tools.Count: > 0 };

    /// <summary>
    /// One role's whole prompt: everything the business said, then the one paragraph saying which part of the call this role is handling.
    /// The business's own text comes first so that a role's part is read as the job inside that business, not as a replacement for it.
    /// </summary>
    private static string PromptFor(string business, string role) => $"{business}\n\n{role}";
}
