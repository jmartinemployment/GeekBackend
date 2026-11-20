using System.Text.Json;
using GeekAPI.Services.Receptionist.Agents;
using GeekAPI.Services.Receptionist.Knowledge;
using GeekAPI.Services.Receptionist.Relay;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// The roles one business's reception is actually made of. There is no fixed roster: a role is in it because the business has the thing
/// that role needs, so a business with no indexed site has no answerer and a business that did not ask to book has no booker. What is
/// being pinned here is that composition, and the two ends of it — the greeter first, because the roster's first role is the one
/// speaking when the call connects, and the message taker last, because it is the floor every unhandled part of a call ends on.
///
/// <para>
/// The prompts are the other half. Every role carries the whole of the business's own assembled prompt, and this asserts it against
/// what <see cref="ReceptionistConsent.SystemPrompt"/> returns for the same inputs rather than against a copy of that text: a second
/// assembler would drift from the first, and the half that drifted would be the half telling a receptionist not to invent a price.
/// </para>
///
/// <para>
/// And the failure. A prompt that cannot be assembled comes back as that same failure with that same code, not as a roster built on
/// half a prompt — a receptionist answering a caller out of nothing is the one thing this product may not do.
/// </para>
/// </summary>
public sealed class ReceptionistRoleScriptsTests
{
    private static readonly JsonElement Schema =
        JsonDocument.Parse("""{"type":"object","properties":{"q":{"type":"string"}}}""").RootElement.Clone();

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static readonly JsonElement NoHours = Json("{}");
    private static readonly JsonElement NoQuestions = Json("[]");

    /// <summary>
    /// A toolbox whose offered tools a test decides. Running one throws: building a roster hands toolboxes over, it never uses them, so
    /// a test that reached <see cref="RunAsync"/> would be testing something this class does not do.
    /// </summary>
    private sealed class Box : IReceptionistToolbox
    {
        public Box(params string[] toolNames) =>
            Tools = toolNames.Select(name => new ReceptionistToolDefinition(name, $"does {name}", Schema)).ToList();

        public IReadOnlyList<ReceptionistToolDefinition> Tools { get; }

        public Task<Result<string>> RunAsync(ReceptionistToolCall call, CancellationToken ct = default) =>
            throw new NotSupportedException("Building a roster hands a toolbox over; it never runs a tool.");
    }

    /// <summary>
    /// The business's indexed site, offering the one tool the answerer's paragraph names. Named from the production constant rather
    /// than spelled out, so a rename that left the role's script claiming a tool nobody offers fails here.
    /// </summary>
    private static Box Lookup() => new(KnowledgeToolbox.LookupTool);

    /// <summary>The business's connected calendar, offering the tool the booker actually needs as well as the one it reads with.</summary>
    private static Box Calendar() => new(CalendarToolbox.ListTool, CalendarToolbox.BookingTool);

    /// <summary>A calendar connection that can be read but cannot be written to, which is not a calendar the booker can work in.</summary>
    private static Box ReadOnlyCalendar() => new(CalendarToolbox.ListTool);

    /// <summary>A toolbox that exists but offers nothing — a connection that was made and then gave no tools.</summary>
    private static Box Empty() => new();

    /// <summary>
    /// One business's settings, in the shape <c>VoiceRepositoryValidationTests.GoodConfig</c> builds: no hours given, no qualifying
    /// questions, and booking off. Each test turns on the one thing it is about.
    /// </summary>
    private static VoiceReceptionistConfig Config() => new(
        "Thanks for calling Boca Cool HVAC.",
        "You answer the phone for Boca Cool HVAC, an air-conditioning contractor in Palm Beach County.",
        NoHours, NoQuestions,
        VoiceTransferModes.WarmTransfer, "+15615550102", VoiceTransferCallerIdModes.OriginalCaller, ["flood", "gas leak"], true);

    /// <summary>What ConversationRelay actually said as the call connected, which in passive disclosure is not the stored greeting.</summary>
    private const string Spoken =
        "Hi, you've reached Boca Cool HVAC's AI assistant. Calls are processed in real time and not recorded. How can I help you today?";

    /// <summary>
    /// The never-invent rule, copied from <see cref="ReceptionistConsent.SystemPrompt"/> because that is the only place it is written.
    /// If the wording there changes this fails, which is the point: the rule reaching every role is what is being asserted, and a role
    /// that silently stopped carrying it is a role that may quote a caller a price nobody gave it.
    /// </summary>
    private const string NeverInvent =
        "Never state a fact about this business - a price, a service, an availability, a policy - that you have not been "
        + "given here or found with a tool. If you have not been given it, say you will take a message and have someone call back. "
        + "Guessing is worse than taking a message.";

    /// <summary>The paragraph each role should be given, and the ones it should not. Internal to GeekAPI, visible here through InternalsVisibleTo.</summary>
    private static readonly IReadOnlyDictionary<string, string> ScriptOf = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ReceptionistRoles.Greeter] = ReceptionistRoleScripts.GreeterScript,
        [ReceptionistRoles.Answerer] = ReceptionistRoleScripts.AnswererScript,
        [ReceptionistRoles.Booker] = ReceptionistRoleScripts.BookerScript,
        [ReceptionistRoles.MessageTaker] = ReceptionistRoleScripts.MessageTakerScript,
    };

    /// <summary>The one line the other roles are offered each role by, when deciding where to hand a caller.</summary>
    private static readonly IReadOnlyDictionary<string, string> PurposeOf = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ReceptionistRoles.Greeter] = ReceptionistRoleScripts.GreeterPurpose,
        [ReceptionistRoles.Answerer] = ReceptionistRoleScripts.AnswererPurpose,
        [ReceptionistRoles.Booker] = ReceptionistRoleScripts.BookerPurpose,
        [ReceptionistRoles.MessageTaker] = ReceptionistRoleScripts.MessageTakerPurpose,
    };

    private static IReadOnlyList<ReceptionistAgent> Roster(
        VoiceReceptionistConfig config,
        IReceptionistToolbox? knowledge = null,
        IReceptionistToolbox? calendar = null,
        string? spokenGreeting = null)
    {
        var built = ReceptionistRoleScripts.For(config, knowledge, calendar, spokenGreeting);

        Assert.True(built.IsSuccess, built.Error);
        Assert.NotNull(built.Value);
        return built.Value!;
    }

    private static IReadOnlyList<string> NamesIn(IReadOnlyList<ReceptionistAgent> roles) => roles.Select(r => r.Name).ToList();

    private static ReceptionistAgent Role(IReadOnlyList<ReceptionistAgent> roles, string name) =>
        Assert.Single(roles, r => r.Name == name);

    /// <summary>
    /// The four businesses this builder can be handed: one with nothing, one with an indexed site, one that books, and one with both.
    /// Every property that must hold "in every combination" is asserted over these rather than over the one case that is convenient.
    /// </summary>
    private static IReadOnlyList<(string What, IReadOnlyList<ReceptionistAgent> Roles)> EveryShape() =>
    [
        ("neither an indexed site nor booking", Roster(Config())),
        ("an indexed site only", Roster(Config(), knowledge: Lookup())),
        ("booking and a calendar only", Roster(Config() with { BookingEnabled = true }, calendar: Calendar())),
        ("an indexed site, booking and a calendar", Roster(Config() with { BookingEnabled = true }, Lookup(), Calendar())),
    ];

    // ----- Which roles exist -----

    /// <summary>
    /// The smallest reception there is. A business that has published nothing and books nothing still answers its phone and still takes
    /// a message, and is given neither a role that would have nothing to look things up in nor one with no calendar to write to.
    /// </summary>
    [Fact]
    public void A_business_with_no_indexed_site_and_no_booking_gets_the_greeter_and_the_message_taker_only()
    {
        var roles = Roster(Config());

        Assert.Equal([ReceptionistRoles.Greeter, ReceptionistRoles.MessageTaker], NamesIn(roles));
    }

    /// <summary>
    /// The answerer exists because there is something to look up, so the toolbox that made it exist is the toolbox it is handed — not a
    /// copy, and not a different one composed on the way.
    /// </summary>
    [Fact]
    public void A_knowledge_toolbox_adds_the_answerer_and_hands_it_that_same_toolbox()
    {
        var knowledge = Lookup();

        var roles = Roster(Config(), knowledge: knowledge);

        Assert.Equal([ReceptionistRoles.Greeter, ReceptionistRoles.Answerer, ReceptionistRoles.MessageTaker], NamesIn(roles));
        Assert.Same(knowledge, Role(roles, ReceptionistRoles.Answerer).Toolbox);
    }

    /// <summary>
    /// Booking takes both the business asking for it and a calendar to do it in. With both, the booker is there and carries the calendar
    /// it books in.
    /// </summary>
    [Fact]
    public void Booking_asked_for_with_a_calendar_adds_the_booker_and_hands_it_that_same_toolbox()
    {
        var calendar = Calendar();

        var roles = Roster(Config() with { BookingEnabled = true }, calendar: calendar);

        Assert.Equal([ReceptionistRoles.Greeter, ReceptionistRoles.Booker, ReceptionistRoles.MessageTaker], NamesIn(roles));
        Assert.Same(calendar, Role(roles, ReceptionistRoles.Booker).Toolbox);
    }

    /// <summary>
    /// Booking turned on with nothing to book in is the owner's intention without the means. The caller is offered a message instead of
    /// a role that would promise an appointment it has no calendar to put anywhere, and the rest of the reception still works.
    /// </summary>
    [Fact]
    public void Booking_asked_for_without_a_calendar_is_a_reception_with_no_booker()
    {
        var roles = Roster(Config() with { BookingEnabled = true }, knowledge: Lookup());

        Assert.DoesNotContain(ReceptionistRoles.Booker, NamesIn(roles));
        Assert.Equal([ReceptionistRoles.Greeter, ReceptionistRoles.Answerer, ReceptionistRoles.MessageTaker], NamesIn(roles));
    }

    /// <summary>
    /// A calendar connected by a business that did not ask its receptionist to book in it is the means without the intention, and is
    /// the same answer: no booker, and a reception that otherwise stands.
    /// </summary>
    [Fact]
    public void A_calendar_the_business_did_not_ask_to_book_with_is_a_reception_with_no_booker()
    {
        var roles = Roster(Config() with { BookingEnabled = false }, calendar: Calendar());

        Assert.DoesNotContain(ReceptionistRoles.Booker, NamesIn(roles));
        Assert.Equal([ReceptionistRoles.Greeter, ReceptionistRoles.MessageTaker], NamesIn(roles));
    }

    /// <summary>
    /// A toolbox that offers tools, but not the one the role's own first paragraph names, is no toolbox either. The answerer opens by
    /// saying it has look_up_business_info and the booker by saying it will put something in this business's calendar; a connection that
    /// came back with neither earns no role, because the alternative is a role whose opening instruction the call cannot honour.
    /// </summary>
    [Fact]
    public void A_toolbox_without_the_tool_the_role_names_earns_no_role()
    {
        var wrongTools = new Box("send_smoke_signal", "consult_oracle");
        Assert.DoesNotContain(Roster(Config(), knowledge: wrongTools), r => r.Name == ReceptionistRoles.Answerer);

        // A calendar that can be read but not written to cannot do the one thing the booker exists for.
        Assert.DoesNotContain(
            Roster(Config() with { BookingEnabled = true }, calendar: ReadOnlyCalendar()),
            r => r.Name == ReceptionistRoles.Booker);
    }

    /// <summary>
    /// The greeter is the only role that does not keep the caller. Its whole job is working out where they should go, so the director
    /// decides its turns; every other role has something to finish and keeps the caller until it hands them on itself. This is what stops
    /// the director spending a model call on every turn of every call to rediscover that a caller mid-booking is still booking.
    /// </summary>
    [Fact]
    public void Only_the_greeter_lets_the_director_decide_its_turns()
    {
        foreach (var role in Roster(Config() with { BookingEnabled = true }, Lookup(), Calendar()))
            Assert.Equal(role.Name != ReceptionistRoles.Greeter, role.ClaimsTheTurn);
    }

    /// <summary>
    /// A toolbox that offers no tools is no toolbox. A role told in its first paragraph that it has a lookup tool, or a calendar to
    /// check, would open with a claim the call cannot honour — the caller is told something is being looked up and nothing can be.
    /// </summary>
    [Fact]
    public void A_toolbox_that_offers_no_tools_is_the_same_as_no_toolbox()
    {
        var roles = Roster(Config() with { BookingEnabled = true }, Empty(), Empty());

        Assert.Equal([ReceptionistRoles.Greeter, ReceptionistRoles.MessageTaker], NamesIn(roles));
    }

    /// <summary>
    /// The greeter is first because the roster's first role is the one speaking when the call connects, and the message taker is last
    /// because everything no other role here handles ends with it. Both have to hold for every business, not for the convenient one.
    /// </summary>
    [Fact]
    public void The_greeter_is_first_and_the_message_taker_is_last_whatever_the_business_has()
    {
        foreach (var (what, roles) in EveryShape())
        {
            Assert.True(roles.Count >= 2, what);
            Assert.True(ReceptionistRoles.Greeter == roles[0].Name, what);
            Assert.True(ReceptionistRoles.MessageTaker == roles[^1].Name, what);
        }
    }

    /// <summary>
    /// Neither of the two roles every business has is given a tool, so what each is told it cannot do is also what it has no way of
    /// doing: the instruction and the capability agree rather than the instruction being asked to hold the line on its own.
    /// </summary>
    [Fact]
    public void The_greeter_and_the_message_taker_are_given_no_toolbox()
    {
        foreach (var (what, roles) in EveryShape())
        {
            Assert.True(Role(roles, ReceptionistRoles.Greeter).Toolbox is null, what);
            Assert.True(Role(roles, ReceptionistRoles.MessageTaker).Toolbox is null, what);
        }
    }

    /// <summary>
    /// A handoff names a role, so a roster carrying a name this product does not know is a handoff to nowhere. Every name in every
    /// roster is one of the fixed ones, and no roster names the same role twice.
    /// </summary>
    [Fact]
    public void Every_name_in_a_roster_is_one_this_product_knows_and_is_in_it_once()
    {
        foreach (var (what, roles) in EveryShape())
        {
            foreach (var name in NamesIn(roles))
                Assert.True(ReceptionistRoles.All.Contains(name), $"{what}: {name}");

            Assert.True(NamesIn(roles).Distinct(StringComparer.Ordinal).Count() == roles.Count, what);
        }
    }

    // ----- The prompts -----

    /// <summary>
    /// Every role carries the whole of the business's own prompt, and it is the prompt the one assembler produced. Asserting it against
    /// <see cref="ReceptionistConsent.SystemPrompt"/> rather than against text repeated here is what pins "assembled in one place": a
    /// second assembler would pass a test written against its own copy and still have drifted from the first.
    /// </summary>
    [Fact]
    public void Every_role_carries_the_business_prompt_the_one_assembler_produced()
    {
        var config = Config() with { BookingEnabled = true };
        var assembled = ReceptionistConsent.SystemPrompt(config, Spoken);
        Assert.True(assembled.IsSuccess, assembled.Error);

        var roles = Roster(config, Lookup(), Calendar(), Spoken);

        Assert.Equal(4, roles.Count);
        foreach (var role in roles)
            Assert.Contains(assembled.Value!, role.SystemPrompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rule that makes an invented price impossible rather than unlikely reaches every role, including the two that are given no
    /// tools at all. A caller on a phone cannot see a citation and has no way to tell a real price from one the model filled in.
    /// </summary>
    [Fact]
    public void Every_role_is_told_never_to_state_a_fact_it_was_not_given()
    {
        foreach (var (what, roles) in EveryShape())
            foreach (var role in roles)
                Assert.True(role.SystemPrompt.Contains(NeverInvent, StringComparison.Ordinal), $"{what}: {role.Name}");
    }

    /// <summary>
    /// A role sees its own part of the call and nobody else's. Carrying another role's paragraph would put the booking rules in context
    /// while the caller is asking a price, which is the failure roles exist to remove.
    /// </summary>
    [Fact]
    public void Each_role_is_told_its_own_part_of_the_call_and_not_another_roles()
    {
        var roles = Roster(Config() with { BookingEnabled = true }, Lookup(), Calendar(), Spoken);

        Assert.Equal(4, roles.Count);
        foreach (var role in roles)
        {
            Assert.Contains(ScriptOf[role.Name], role.SystemPrompt, StringComparison.Ordinal);

            foreach (var (otherName, otherScript) in ScriptOf.Where(s => s.Key != role.Name))
                Assert.True(
                    !role.SystemPrompt.Contains(otherScript, StringComparison.Ordinal),
                    $"The {role.Name} was given the {otherName}'s paragraph.");
        }
    }

    /// <summary>
    /// The business's own words come first and the role's part comes after them, so the part is read as the job inside that business
    /// rather than as a replacement for what the business said.
    /// </summary>
    [Fact]
    public void The_business_own_words_come_before_the_part_the_role_is_handling()
    {
        var config = Config() with { BookingEnabled = true };
        var assembled = ReceptionistConsent.SystemPrompt(config, Spoken);
        Assert.True(assembled.IsSuccess, assembled.Error);

        var roles = Roster(config, Lookup(), Calendar(), Spoken);

        Assert.Equal(4, roles.Count);
        foreach (var role in roles)
        {
            var businessAt = role.SystemPrompt.IndexOf(assembled.Value!, StringComparison.Ordinal);
            var roleAt = role.SystemPrompt.IndexOf(ScriptOf[role.Name], StringComparison.Ordinal);

            Assert.True(businessAt == 0, $"The {role.Name}'s prompt does not open with the business's own text.");
            Assert.True(roleAt > businessAt, $"The {role.Name}'s part is not after the business's own text.");
        }
    }

    /// <summary>
    /// What the caller actually heard as the call connected is what every role is told they heard. In passive disclosure that is the
    /// disclosure and not the stored greeting, and a role told the stored greeting would open by greeting a caller twice.
    /// </summary>
    [Fact]
    public void The_greeting_the_caller_actually_heard_reaches_every_role()
    {
        var config = Config() with { BookingEnabled = true };

        var heard = Roster(config, Lookup(), Calendar(), Spoken);

        Assert.Equal(4, heard.Count);
        foreach (var role in heard)
        {
            Assert.Contains(Spoken, role.SystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain(config.GreetingMessage, role.SystemPrompt, StringComparison.Ordinal);
        }

        // With nothing spoken to pass on, the stored greeting is what the roles are told, so the thread is the spoken one being
        // preferred rather than the stored one never arriving.
        foreach (var role in Roster(config, Lookup(), Calendar()))
            Assert.Contains(config.GreetingMessage, role.SystemPrompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// A role's purpose is the line every other role is offered it by when deciding where to hand a caller, so each is the matching
    /// constant, none is blank, and no two read the same: two roles described identically is a handoff decided by a coin toss.
    /// </summary>
    [Fact]
    public void Each_role_carries_the_purpose_the_other_roles_are_offered_it_by()
    {
        var roles = Roster(Config() with { BookingEnabled = true }, Lookup(), Calendar());

        Assert.Equal(4, roles.Count);
        foreach (var role in roles)
        {
            Assert.Equal(PurposeOf[role.Name], role.Purpose);
            Assert.False(string.IsNullOrWhiteSpace(role.Purpose), role.Name);
        }

        Assert.Equal(roles.Count, roles.Select(r => r.Purpose).Distinct(StringComparer.Ordinal).Count());
    }

    // ----- The failure -----

    /// <summary>
    /// Every way the business's own prompt cannot be assembled: no script, no greeting, hours in a shape the receptionist does not
    /// read, and qualifying questions in one. Each is a refusal in <see cref="ReceptionistConsent.SystemPrompt"/>, not a skip.
    /// </summary>
    public static TheoryData<string, Func<VoiceReceptionistConfig>> PromptsThatCannotBeAssembled() => new()
    {
        { "a blank script", () => Config() with { SystemPromptTemplate = "   " } },
        { "no greeting", () => Config() with { GreetingMessage = "" } },
        { "hours that are not stored at all", () => Config() with { BusinessHours = default } },
        { "hours in a shape it does not read", () => Config() with { BusinessHours = Json("""{"days":"every day"}""") } },
        { "qualifying questions in a shape it does not read", () => Config() with { QualifyingQuestions = Json("""{"openingQuestion":7}""") } },
    };

    /// <summary>
    /// The failure is passed on whole — the same sentence and the same code the assembler refused with — and nothing is returned
    /// alongside it. A reception built on half a prompt is a receptionist answering a caller out of nothing, which is the one thing
    /// this product may not do, so there is no partial roster to fall back to and no role built from what did assemble.
    /// </summary>
    [Theory]
    [MemberData(nameof(PromptsThatCannotBeAssembled))]
    public void A_business_whose_prompt_cannot_be_assembled_gets_that_failure_and_no_roster(string why, Func<VoiceReceptionistConfig> config)
    {
        var broken = config();
        var refused = ReceptionistConsent.SystemPrompt(broken);
        Assert.False(refused.IsSuccess, why);

        var built = ReceptionistRoleScripts.For(broken, Lookup(), Calendar());

        Assert.False(built.IsSuccess, why);
        Assert.Null(built.Value);
        Assert.Equal(refused.Error, built.Error);
        Assert.Equal(refused.ErrorCode, built.ErrorCode);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, built.ErrorCode);
    }
}
