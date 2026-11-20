using System.Text.Json;
using GeekAPI.Services.Receptionist.Agents;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Results;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// The roles one business's reception is made of, and which of them holds the caller: a roster of none is no reception at all, a roster
/// of one is that role with nothing to hand to, a roster of several greets with its first, and every role is offered a handoff to the
/// others and never to itself. A duplicate name is dropped loudly. A role <i>asks</i> — a successful handoff leaves a request the
/// director reads once and that is then gone — and the director <i>moves</i>, to a real role that is not the one already speaking, as
/// many times as it decides to. Nothing any of that logs is anything a caller said.
/// </summary>
public sealed class ReceptionistAgentsTests
{
    private static readonly JsonElement Schema =
        JsonDocument.Parse("""{"type":"object","properties":{"q":{"type":"string"}}}""").RootElement.Clone();

    /// <summary>
    /// One role's own tools: the names it offers and every call it was handed, so a test can say which box a call reached and which
    /// never heard of it. It answers the name it ran, so one box's answer is told from another's.
    /// </summary>
    private sealed class Box : IReceptionistToolbox
    {
        public Box(params string[] toolNames) =>
            Tools = toolNames.Select(name => new ReceptionistToolDefinition(name, $"does {name}", Schema)).ToList();

        public IReadOnlyList<ReceptionistToolDefinition> Tools { get; }

        /// <summary>Every call this box was asked to run, in order.</summary>
        public List<ReceptionistToolCall> Ran { get; } = new();

        public Task<Result<string>> RunAsync(ReceptionistToolCall call, CancellationToken ct = default)
        {
            Ran.Add(call);
            return Task.FromResult(Result<string>.Success(JsonSerializer.Serialize(new { ran = call.Name })));
        }
    }

    private static ReceptionistAgent Role(string name, IReceptionistToolbox? toolbox = null) =>
        new(name, $"what the {name} is for", $"You are the {name}.", toolbox);

    private static ReceptionistAgents Roster(ILogger logger, params ReceptionistAgent[] agents)
    {
        var roster = ReceptionistAgents.Of(agents, logger);
        Assert.NotNull(roster);
        return roster!;
    }

    /// <summary>The names the role now holding the caller may hand to, read off the handoff tool's own schema rather than its prose.</summary>
    private static IReadOnlyList<string> Destinations(ReceptionistAgents roster)
    {
        var toolbox = roster.Toolbox;
        Assert.NotNull(toolbox);
        var handoff = Assert.Single(toolbox!.Tools, t => t.Name == HandoffToolbox.Tool);
        return handoff.InputSchema
            .GetProperty("properties")
            .GetProperty("to")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .ToList();
    }

    /// <summary>Runs the handoff tool as the call loop does — through the role's own toolbox — so the roster reads a real request.</summary>
    private static async Task<Result<string>> HandOff(ReceptionistAgents roster, string to, string? callerSaid = null)
    {
        var toolbox = roster.Toolbox;
        Assert.NotNull(toolbox);

        var arguments = callerSaid is null
            ? JsonSerializer.Serialize(new { to })
            : JsonSerializer.Serialize(new { to, caller_said = callerSaid });

        return await toolbox!.RunAsync(new ReceptionistToolCall("call-1", HandoffToolbox.Tool, arguments));
    }

    /// <summary>
    /// One whole turn of the real loop: the speaking role asks for somewhere, the director reads the ask, and the director moves the
    /// caller. Returns where they ended up, so a test can bounce a caller without ever asserting on the director's own decision.
    /// </summary>
    private static async Task<string> HandOffAndMove(ReceptionistAgents roster, string to)
    {
        Assert.True((await HandOff(roster, to)).IsSuccess);

        var requested = roster.TakeRequest();
        Assert.Equal(to, requested);
        Assert.True(roster.MoveTo(requested!));

        return roster.Current.Name;
    }

    /// <summary>The other role of a two-role roster, so a test can bounce the caller without naming a side.</summary>
    private static string Other(ReceptionistAgents roster) =>
        roster.Current.Name == ReceptionistRoles.Greeter ? ReceptionistRoles.Answerer : ReceptionistRoles.Greeter;

    // ----- Building a roster -----

    [Fact]
    public void A_business_with_no_roles_has_no_reception_at_all()
    {
        var log = new CapturingLogger();

        // Null rather than an empty roster: a receptionist that cannot be built is the caller's problem to be told about.
        Assert.Null(ReceptionistAgents.Of([], log));
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void One_role_is_the_whole_reception_and_is_offered_no_handoff()
    {
        var log = new CapturingLogger();
        var only = new Box("book_now", "check_hours");
        var greeter = Role(ReceptionistRoles.Greeter, only);

        var roster = Roster(log, greeter);

        Assert.Same(greeter, roster.Current);
        // One role has nowhere to hand to, so there is no handoff tool and nothing to compose: the role's own toolbox is handed back
        // as itself. No mode, no flag — the roster is simply short.
        Assert.Same(only, roster.Toolbox);
        Assert.DoesNotContain(HandoffToolbox.Tool, roster.Toolbox!.Tools.Select(t => t.Name));
        Assert.Equal(0, roster.HandoffsMade);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void One_role_that_can_only_talk_has_no_toolbox_rather_than_an_empty_one()
    {
        var log = new CapturingLogger();

        var roster = Roster(log, Role(ReceptionistRoles.Greeter));

        Assert.Equal(ReceptionistRoles.Greeter, roster.Current.Name);
        Assert.Null(roster.Toolbox);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void Several_roles_start_with_the_first_one_which_is_the_greeter()
    {
        var log = new CapturingLogger();
        var greeter = Role(ReceptionistRoles.Greeter);
        var answerer = Role(ReceptionistRoles.Answerer);
        var booker = Role(ReceptionistRoles.Booker);

        var roster = Roster(log, greeter, answerer, booker);

        Assert.Same(greeter, roster.Current);
        Assert.Equal(0, roster.HandoffsMade);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void A_role_with_no_tools_of_its_own_is_offered_the_handoff_alone()
    {
        var log = new CapturingLogger();

        var roster = Roster(log, Role(ReceptionistRoles.Greeter), Role(ReceptionistRoles.Answerer));

        // The right toolbox for a greeter: it can work out what the caller wants and hand them on, and nothing else.
        Assert.NotNull(roster.Toolbox);
        Assert.Equal([HandoffToolbox.Tool], roster.Toolbox!.Tools.Select(t => t.Name));
    }

    [Fact]
    public async Task Every_role_is_offered_its_own_tools_and_a_handoff_to_the_others_but_never_to_itself()
    {
        var log = new CapturingLogger();
        var roster = Roster(
            log,
            Role(ReceptionistRoles.Greeter, new Box("transfer_to_human")),
            Role(ReceptionistRoles.Answerer, new Box("look_up", "opening_hours")),
            Role(ReceptionistRoles.Booker, new Box("book_now")));

        Assert.Equal(["transfer_to_human", HandoffToolbox.Tool], roster.Toolbox!.Tools.Select(t => t.Name));
        Assert.Equal([ReceptionistRoles.Answerer, ReceptionistRoles.Booker], Destinations(roster));

        Assert.Equal(ReceptionistRoles.Answerer, await HandOffAndMove(roster, ReceptionistRoles.Answerer));

        Assert.Equal(["look_up", "opening_hours", HandoffToolbox.Tool], roster.Toolbox!.Tools.Select(t => t.Name));
        Assert.Equal([ReceptionistRoles.Greeter, ReceptionistRoles.Booker], Destinations(roster));

        Assert.Equal(ReceptionistRoles.Booker, await HandOffAndMove(roster, ReceptionistRoles.Booker));

        Assert.Equal(["book_now", HandoffToolbox.Tool], roster.Toolbox!.Tools.Select(t => t.Name));
        Assert.Equal([ReceptionistRoles.Greeter, ReceptionistRoles.Answerer], Destinations(roster));
    }

    [Fact]
    public async Task A_role_runs_its_own_tools_and_the_other_roles_boxes_never_hear_of_them()
    {
        var log = new CapturingLogger();
        var greeterBox = new Box("transfer_to_human");
        var bookerBox = new Box("book_now");
        var roster = Roster(
            log,
            Role(ReceptionistRoles.Greeter, greeterBox),
            Role(ReceptionistRoles.Booker, bookerBox));

        var ran = await roster.Toolbox!.RunAsync(new ReceptionistToolCall("call-1", "transfer_to_human", """{"q":"x"}"""));

        Assert.True(ran.IsSuccess);
        Assert.Equal("""{"ran":"transfer_to_human"}""", ran.Value);
        Assert.Equal("transfer_to_human", Assert.Single(greeterBox.Ran).Name);
        Assert.Empty(bookerBox.Ran);

        // Running a role's own tool asks for nothing, so there is nothing for the director to read and the caller does not move.
        Assert.Null(roster.TakeRequest());
        Assert.Equal(ReceptionistRoles.Greeter, roster.Current.Name);
        Assert.Equal(0, roster.HandoffsMade);
    }

    [Fact]
    public void A_duplicate_role_name_is_dropped_the_first_wins_and_an_error_names_the_role()
    {
        var log = new CapturingLogger();
        var first = Role(ReceptionistRoles.Greeter, new Box("first_tool"));
        var second = Role(ReceptionistRoles.Greeter, new Box("second_tool"));
        var answerer = Role(ReceptionistRoles.Answerer);

        var roster = Roster(log, first, answerer, second);

        Assert.Same(first, roster.Current);
        Assert.Equal(["first_tool", HandoffToolbox.Tool], roster.Toolbox!.Tools.Select(t => t.Name));
        // The second is not merely shadowed — it is not in the roster, so no handoff can name it twice.
        Assert.Equal([ReceptionistRoles.Answerer], Destinations(roster));

        var line = Assert.Single(log.Lines);
        Assert.StartsWith("Error ", line, StringComparison.Ordinal);
        Assert.Contains(ReceptionistRoles.Greeter, line, StringComparison.Ordinal);
    }

    // ----- What the speaking role asked for -----

    [Fact]
    public void A_role_that_has_run_no_handoff_has_asked_for_nothing()
    {
        var log = new CapturingLogger();
        var roster = Roster(log, Role(ReceptionistRoles.Greeter), Role(ReceptionistRoles.Answerer));

        // Nothing has been run at all, so there is no ask. The director's ordinary turn is this one.
        Assert.Null(roster.TakeRequest());

        Assert.Equal(ReceptionistRoles.Greeter, roster.Current.Name);
        Assert.Equal(0, roster.HandoffsMade);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task A_handoff_that_named_somewhere_real_is_the_request_the_director_reads()
    {
        var log = new CapturingLogger();
        var greeter = Role(ReceptionistRoles.Greeter);
        var roster = Roster(log, greeter, Role(ReceptionistRoles.Booker, new Box("book_now")));

        var asked = await HandOff(roster, ReceptionistRoles.Booker);

        Assert.True(asked.IsSuccess);
        Assert.Equal(ReceptionistRoles.Booker, roster.TakeRequest());

        // An ask is not a move: the toolbox reports where to go and the director is what puts the caller there.
        Assert.Same(greeter, roster.Current);
        Assert.Equal(0, roster.HandoffsMade);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task A_request_is_read_once_and_then_it_is_gone()
    {
        var log = new CapturingLogger();
        var roster = Roster(log, Role(ReceptionistRoles.Greeter), Role(ReceptionistRoles.Answerer));

        Assert.True((await HandOff(roster, ReceptionistRoles.Answerer)).IsSuccess);

        Assert.Equal(ReceptionistRoles.Answerer, roster.TakeRequest());
        // An ask that has been considered must not be considered twice: a request left standing would route the turn after it too.
        Assert.Null(roster.TakeRequest());
        Assert.Null(roster.TakeRequest());

        Assert.Equal(ReceptionistRoles.Greeter, roster.Current.Name);
        Assert.Equal(0, roster.HandoffsMade);
    }

    [Fact]
    public async Task A_handoff_to_a_role_this_business_does_not_have_asks_for_nothing()
    {
        var log = new CapturingLogger();
        var greeter = Role(ReceptionistRoles.Greeter);
        var roster = Roster(log, greeter, Role(ReceptionistRoles.Answerer));

        var asked = await HandOff(roster, ReceptionistRoles.MessageTaker);

        // The tool failed loudly with a code, so nothing was requested and the director has nothing to honour.
        Assert.False(asked.IsSuccess);
        Assert.Equal(HandoffFailureCodes.UnknownDestination, asked.ErrorCode);
        Assert.Null(roster.TakeRequest());
        Assert.Same(greeter, roster.Current);
        Assert.Equal(0, roster.HandoffsMade);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task A_handoff_that_named_nowhere_asks_for_nothing()
    {
        var log = new CapturingLogger();
        var greeter = Role(ReceptionistRoles.Greeter);
        var roster = Roster(log, greeter, Role(ReceptionistRoles.Answerer));

        var asked = await roster.Toolbox!.RunAsync(new ReceptionistToolCall("call-1", HandoffToolbox.Tool, "{}"));

        Assert.False(asked.IsSuccess);
        Assert.Equal(HandoffFailureCodes.NoDestination, asked.ErrorCode);
        Assert.Null(roster.TakeRequest());
        Assert.Same(greeter, roster.Current);
        Assert.Equal(0, roster.HandoffsMade);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task A_refused_handoff_after_a_good_one_asks_for_nothing_and_the_good_one_does_not_stand_in_for_it()
    {
        var log = new CapturingLogger();
        var roster = Roster(log, Role(ReceptionistRoles.Greeter), Role(ReceptionistRoles.Answerer));

        Assert.Equal(ReceptionistRoles.Answerer, await HandOffAndMove(roster, ReceptionistRoles.Answerer));

        // The same toolbox is asked again for somewhere that does not exist: the earlier request must not stand in for this one.
        var asked = await HandOff(roster, ReceptionistRoles.Booker);

        Assert.False(asked.IsSuccess);
        Assert.Equal(HandoffFailureCodes.UnknownDestination, asked.ErrorCode);
        Assert.Null(roster.TakeRequest());
        Assert.Equal(ReceptionistRoles.Answerer, roster.Current.Name);
        Assert.Equal(1, roster.HandoffsMade);
    }

    [Fact]
    public void A_role_with_nowhere_to_hand_to_is_asked_what_it_wants_and_answers_nothing()
    {
        var log = new CapturingLogger();
        var roster = Roster(log, Role(ReceptionistRoles.Greeter, new Box("book_now")));

        // One role is offered no handoff at all, so there is no handoff to read a request off. The director asks every roster the same
        // question and a short one answers it rather than throwing at it.
        Assert.Null(roster.TakeRequest());
        Assert.Null(roster.TakeRequest());

        Assert.Equal(ReceptionistRoles.Greeter, roster.Current.Name);
        Assert.Equal(0, roster.HandoffsMade);
        Assert.Empty(log.Lines);
    }

    // ----- Changing hands, as the director decides -----

    [Fact]
    public void Moving_a_caller_to_a_real_role_swaps_the_role_and_its_toolbox_and_counts_and_says_so()
    {
        var log = new CapturingLogger();
        var greeter = Role(ReceptionistRoles.Greeter);
        var booker = Role(ReceptionistRoles.Booker, new Box("book_now"));
        var roster = Roster(log, greeter, booker);

        Assert.True(roster.MoveTo(ReceptionistRoles.Booker));

        Assert.Same(booker, roster.Current);
        Assert.Equal(["book_now", HandoffToolbox.Tool], roster.Toolbox!.Tools.Select(t => t.Name));
        Assert.Equal(1, roster.HandoffsMade);

        // One line, naming where the caller came from and where they went, and nothing else about the call.
        var line = Assert.Single(log.Lines);
        Assert.StartsWith("Information ", line, StringComparison.Ordinal);
        Assert.Contains(ReceptionistRoles.Greeter, line, StringComparison.Ordinal);
        Assert.Contains(ReceptionistRoles.Booker, line, StringComparison.Ordinal);
    }

    [Fact]
    public void Moving_a_caller_to_a_role_this_business_does_not_have_moves_nobody()
    {
        var log = new CapturingLogger();
        var greeter = Role(ReceptionistRoles.Greeter);
        var roster = Roster(log, greeter, Role(ReceptionistRoles.Answerer));

        // A name that is not a role leaves the caller where they are rather than anywhere invented — including a role this product
        // knows about that this business was not given, and a name matched on case alone.
        Assert.False(roster.MoveTo(ReceptionistRoles.MessageTaker));
        Assert.False(roster.MoveTo("concierge"));
        Assert.False(roster.MoveTo("ANSWERER"));
        Assert.False(roster.MoveTo(string.Empty));

        Assert.Same(greeter, roster.Current);
        Assert.Equal(0, roster.HandoffsMade);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void Moving_a_caller_to_the_role_already_speaking_is_not_a_move()
    {
        var log = new CapturingLogger();
        var greeter = Role(ReceptionistRoles.Greeter);
        var roster = Roster(log, greeter, Role(ReceptionistRoles.Answerer));

        // Moving somebody to where they already are is not a move: counting it would make an ordinary turn look like a handoff.
        Assert.False(roster.MoveTo(ReceptionistRoles.Greeter));

        Assert.Same(greeter, roster.Current);
        Assert.Equal(0, roster.HandoffsMade);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task A_call_changes_hands_as_often_as_the_director_decides_and_there_is_no_bound_at_all()
    {
        const int Times = 10;

        var log = new CapturingLogger();
        var roster = Roster(log, Role(ReceptionistRoles.Greeter), Role(ReceptionistRoles.Answerer));

        // The two roles pass the caller back and forth, which is exactly what the old counter existed to stop. One director decides
        // every turn and cannot volley with itself, so the counter is gone and nothing here is refused for being the eleventh.
        for (var made = 1; made <= Times; made++)
        {
            var destination = Other(roster);

            Assert.Equal(destination, await HandOffAndMove(roster, destination));
            Assert.Equal(made, roster.HandoffsMade);
        }

        Assert.Equal(Times, roster.HandoffsMade);
        // Every handoff that happened was told, and no more than happened — no warning, because nothing was refused.
        Assert.Equal(Times, log.Lines.Count(l => l.StartsWith("Information ", StringComparison.Ordinal)));
        Assert.Equal(Times, log.Lines.Count);
    }

    // ----- Zero Data Retention -----

    [Fact]
    public async Task Nothing_a_caller_said_reaches_a_log_line_only_role_names_and_counts()
    {
        const string Said =
            "Hi, this is Dana Whitfield, my callback number is 561 555 0199, and my dog swallowed a wedding ring this morning.";

        var log = new CapturingLogger();
        var greeterBox = new Box("take_a_message");
        var roster = Roster(
            log,
            Role(ReceptionistRoles.Greeter, greeterBox),
            Role(ReceptionistRoles.Answerer),
            // A duplicate, so the error line is in the sample this test reads too.
            Role(ReceptionistRoles.Greeter, new Box("take_a_message")));

        // What the caller said goes through a role's own tool…
        var ran = await roster.Toolbox!.RunAsync(new ReceptionistToolCall("call-1", "take_a_message", JsonSerializer.Serialize(new { q = Said })));
        Assert.True(ran.IsSuccess);
        Assert.Null(roster.TakeRequest());

        // …and rides along on every handoff, so every line this class can write is in the sample.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var destination = Other(roster);
            Assert.True((await HandOff(roster, destination, Said)).IsSuccess);
            Assert.Equal(destination, roster.TakeRequest());
            Assert.True(roster.MoveTo(destination));
        }

        // A move that is refused writes nothing at all, which is the other half of the sample.
        Assert.False(roster.MoveTo(roster.Current.Name));
        Assert.False(roster.MoveTo(ReceptionistRoles.Booker));

        Assert.Equal(4, roster.HandoffsMade);

        string[] forbidden = ["Dana", "Whitfield", "561", "555", "0199", "swallowed", "wedding", "callback", "take_a_message", Said];
        string[] roles = [ReceptionistRoles.Greeter, ReceptionistRoles.Answerer];

        Assert.NotEmpty(log.Lines);
        Assert.All(log.Lines, line =>
        {
            foreach (var word in forbidden)
                Assert.DoesNotContain(word, line, StringComparison.OrdinalIgnoreCase);

            // What is left is the names of the roles the caller passed between.
            Assert.Contains(roles, role => line.Contains(role, StringComparison.Ordinal));
        });
    }
}
