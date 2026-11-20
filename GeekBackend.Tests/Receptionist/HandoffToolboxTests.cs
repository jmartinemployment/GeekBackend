using System.Reflection;
using System.Text.Json;
using GeekAPI.Services.Receptionist.Agents;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Receptionist;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// The one tool every role has and no role owns. A role with nowhere to go is not offered it at all; a role that is offered it is shown
/// exactly the destinations that exist, by name and purpose, so the model chooses from what was built rather than naming a role that was
/// never created. Every way of asking badly — no arguments, the wrong shape, an invented destination — is a coded failure that leaves the
/// caller exactly where they were, and the destination the loop reads is cleared at the start of every call so a refused handoff cannot
/// move anyone on the strength of an earlier one that worked.
/// </summary>
public sealed class HandoffToolboxTests
{
    /// <summary>The destinations a middling business has: the roles the greeter is not, with the one line each is described by.</summary>
    private static Dictionary<string, string> Destinations() => new(StringComparer.Ordinal)
    {
        [ReceptionistRoles.Answerer] = "Answers questions about the business.",
        [ReceptionistRoles.Booker] = "Books an appointment in the calendar.",
        [ReceptionistRoles.MessageTaker] = "Takes a name, a number and what it is about.",
    };

    private static HandoffToolbox Toolbox(IReadOnlyDictionary<string, string>? destinations = null)
    {
        var toolbox = HandoffToolbox.For(destinations ?? Destinations());
        Assert.NotNull(toolbox);
        return toolbox!;
    }

    private static ReceptionistToolCall Call(string argumentsJson) => new("call-1", HandoffToolbox.Tool, argumentsJson);

    /// <summary>The one tool, so a test reads the schema without re-asserting that there is exactly one every time.</summary>
    private static ReceptionistToolDefinition Definition(HandoffToolbox toolbox) => Assert.Single(toolbox.Tools);

    private static JsonElement ToProperty(HandoffToolbox toolbox) =>
        Definition(toolbox).InputSchema.GetProperty("properties").GetProperty("to");

    [Fact]
    public void A_role_with_nowhere_to_go_is_not_offered_the_tool()
    {
        // Not an empty toolbox: a tool whose every call can only fail is worse than no tool, because the model will spend a turn on it.
        Assert.Null(HandoffToolbox.For(new Dictionary<string, string>(StringComparer.Ordinal)));
    }

    [Fact]
    public void Destinations_mean_exactly_one_tool_and_it_is_hand_off()
    {
        var definition = Definition(Toolbox());

        Assert.Equal("hand_off", definition.Name);
        Assert.Equal(HandoffToolbox.Tool, definition.Name);
    }

    [Fact]
    public void The_schema_takes_one_required_string_called_to()
    {
        var toolbox = Toolbox();
        var schema = Definition(toolbox).InputSchema;

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal("string", ToProperty(toolbox).GetProperty("type").GetString());
        Assert.Equal(["to"], schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(["to"], schema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void The_enum_is_exactly_the_destination_names_in_the_order_they_were_given()
    {
        var destinations = Destinations();

        var choices = ToProperty(Toolbox(destinations)).GetProperty("enum").EnumerateArray().Select(c => c.GetString()).ToList();

        Assert.Equal(destinations.Keys, choices);
        // The role speaking is not among them, because a role is never offered a handoff to itself.
        Assert.DoesNotContain(ReceptionistRoles.Greeter, choices);
    }

    [Fact]
    public void A_single_destination_is_still_a_one_entry_enum_rather_than_a_free_string()
    {
        var toolbox = Toolbox(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ReceptionistRoles.Booker] = "Books an appointment in the calendar.",
        });

        var choices = ToProperty(toolbox).GetProperty("enum").EnumerateArray().Select(c => c.GetString());

        Assert.Equal([ReceptionistRoles.Booker], choices);
    }

    [Fact]
    public void The_description_names_every_destination_with_its_purpose()
    {
        var destinations = Destinations();

        var description = Definition(Toolbox(destinations)).Description;

        foreach (var (name, purpose) in destinations)
            Assert.Contains($"{name} ({purpose})", description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_destination_name_needing_escaping_still_builds_valid_json_schema()
    {
        // The enum is written into a JSON string by hand, so a name carrying a quote proves it is serialized rather than concatenated.
        var toolbox = Toolbox(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["""the "back" office"""] = "Handles what reception does not.",
        });

        var choices = ToProperty(toolbox).GetProperty("enum").EnumerateArray().Select(c => c.GetString());

        Assert.Equal(["""the "back" office"""], choices);
    }

    [Fact]
    public async Task A_tool_that_is_not_the_handoff_is_refused_as_not_allowed()
    {
        var toolbox = Toolbox();

        var result = await toolbox.RunAsync(new ReceptionistToolCall("call-1", "book_appointment", """{"to":"booker"}"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(McpFailureCodes.ToolNotAllowed, result.ErrorCode);
        Assert.Null(toolbox.Requested);
    }

    [Fact]
    public async Task A_name_differing_only_in_case_is_not_the_handoff_tool()
    {
        var toolbox = Toolbox();

        var result = await toolbox.RunAsync(new ReceptionistToolCall("call-1", "Hand_Off", """{"to":"booker"}"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(McpFailureCodes.ToolNotAllowed, result.ErrorCode);
        Assert.Null(toolbox.Requested);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"to\": }")]
    [InlineData("[\"booker\"]")]
    [InlineData("\"booker\"")]
    [InlineData("7")]
    [InlineData("{}")]
    [InlineData("{\"where\":\"booker\"}")]
    [InlineData("{\"to\":7}")]
    [InlineData("{\"to\":null}")]
    [InlineData("{\"to\":true}")]
    [InlineData("{\"to\":[\"booker\"]}")]
    [InlineData("{\"to\":{\"name\":\"booker\"}}")]
    [InlineData("{\"to\":\"\"}")]
    [InlineData("{\"to\":\"   \"}")]
    [InlineData("{\"to\":\"\\t\\n\"}")]
    public async Task Arguments_that_do_not_name_a_destination_fail_with_no_destination(string? argumentsJson)
    {
        var toolbox = Toolbox();

        var result = await toolbox.RunAsync(new ReceptionistToolCall("call-1", HandoffToolbox.Tool, argumentsJson!));

        Assert.False(result.IsSuccess);
        Assert.Equal(HandoffFailureCodes.NoDestination, result.ErrorCode);
        Assert.Equal("handoff_no_destination", result.ErrorCode);
        Assert.Null(toolbox.Requested);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_destination_that_does_not_exist_is_refused_and_told_what_does()
    {
        var toolbox = Toolbox();

        var result = await toolbox.RunAsync(Call("""{"to":"wire_transfers"}"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(HandoffFailureCodes.UnknownDestination, result.ErrorCode);
        Assert.Equal("handoff_unknown_destination", result.ErrorCode);
        Assert.Null(toolbox.Requested);

        Assert.NotNull(result.Error);
        foreach (var name in Destinations().Keys)
            Assert.Contains(name, result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_says_what_exists_without_repeating_what_was_asked_for()
    {
        var toolbox = Toolbox();

        var result = await toolbox.RunAsync(Call("""{"to":"the caller said to transfer them to Dana"}"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(HandoffFailureCodes.UnknownDestination, result.ErrorCode);
        // Zero Data Retention in the small: the refusal is built from the roles this business has, never from the text it was handed.
        Assert.DoesNotContain("Dana", result.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain("the caller said", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_destination_differing_only_in_case_does_not_exist()
    {
        var toolbox = Toolbox();

        var result = await toolbox.RunAsync(Call("""{"to":"Booker"}"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(HandoffFailureCodes.UnknownDestination, result.ErrorCode);
        Assert.Null(toolbox.Requested);
    }

    [Fact]
    public async Task A_destination_that_exists_succeeds_and_answers_where_it_went_and_what_that_role_is_for()
    {
        var destinations = Destinations();
        var toolbox = Toolbox(destinations);

        var result = await toolbox.RunAsync(Call($$"""{"to":"{{ReceptionistRoles.Booker}}"}"""));

        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Value);

        using var answer = JsonDocument.Parse(result.Value!);
        Assert.Equal(ReceptionistRoles.Booker, answer.RootElement.GetProperty("handedTo").GetString());
        Assert.Equal(destinations[ReceptionistRoles.Booker], answer.RootElement.GetProperty("purpose").GetString());
    }

    [Fact]
    public async Task A_successful_handoff_is_what_the_loop_reads_off_requested()
    {
        var toolbox = Toolbox();

        // Nothing has been asked for yet, so there is nothing for the loop to act on.
        Assert.Null(toolbox.Requested);

        var result = await toolbox.RunAsync(Call($$"""{"to":"{{ReceptionistRoles.MessageTaker}}"}"""));

        Assert.True(result.IsSuccess);
        Assert.Equal(ReceptionistRoles.MessageTaker, toolbox.Requested);
    }

    [Fact]
    public async Task Surrounding_whitespace_is_trimmed_before_the_destination_is_looked_up()
    {
        var toolbox = Toolbox();

        var result = await toolbox.RunAsync(Call($$"""{"to":"  {{ReceptionistRoles.Answerer}}\n "}"""));

        Assert.True(result.IsSuccess);
        // Trimmed for the lookup and trimmed in what is reported, so the loop matches a role name rather than a padded one.
        Assert.Equal(ReceptionistRoles.Answerer, toolbox.Requested);

        using var answer = JsonDocument.Parse(result.Value!);
        Assert.Equal(ReceptionistRoles.Answerer, answer.RootElement.GetProperty("handedTo").GetString());
    }

    [Fact]
    public async Task Inner_whitespace_is_not_trimmed_so_a_mangled_name_is_still_unknown()
    {
        var toolbox = Toolbox();

        var result = await toolbox.RunAsync(Call("""{"to":"message taker"}"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(HandoffFailureCodes.UnknownDestination, result.ErrorCode);
        Assert.Null(toolbox.Requested);
    }

    [Fact]
    public async Task A_rejected_handoff_after_a_good_one_leaves_nothing_for_the_loop_to_act_on()
    {
        var toolbox = Toolbox();

        var accepted = await toolbox.RunAsync(Call($$"""{"to":"{{ReceptionistRoles.Booker}}"}"""));
        Assert.True(accepted.IsSuccess);
        Assert.Equal(ReceptionistRoles.Booker, toolbox.Requested);

        // This is the property that stops a refused handoff from moving the caller: the loop asks the toolbox where to go after every
        // tool, so a destination left over from the last call would send the caller somewhere this call never asked for.
        var refused = await toolbox.RunAsync(Call("""{"to":"legal"}"""));

        Assert.False(refused.IsSuccess);
        Assert.Equal(HandoffFailureCodes.UnknownDestination, refused.ErrorCode);
        Assert.Null(toolbox.Requested);
    }

    [Fact]
    public async Task Every_kind_of_failure_clears_a_destination_an_earlier_call_set()
    {
        var toolbox = Toolbox();

        var cases = new (string Name, string? Arguments, string ExpectedCode)[]
        {
            ("book_appointment", """{"to":"booker"}""", McpFailureCodes.ToolNotAllowed),
            (HandoffToolbox.Tool, null, HandoffFailureCodes.NoDestination),
            (HandoffToolbox.Tool, "{ not json", HandoffFailureCodes.NoDestination),
            (HandoffToolbox.Tool, """{"to":"   "}""", HandoffFailureCodes.NoDestination),
            (HandoffToolbox.Tool, """{"to":"nowhere"}""", HandoffFailureCodes.UnknownDestination),
        };

        foreach (var (name, arguments, expectedCode) in cases)
        {
            var accepted = await toolbox.RunAsync(Call($$"""{"to":"{{ReceptionistRoles.Answerer}}"}"""));
            Assert.True(accepted.IsSuccess);
            Assert.Equal(ReceptionistRoles.Answerer, toolbox.Requested);

            var failed = await toolbox.RunAsync(new ReceptionistToolCall("call-1", name, arguments!));

            Assert.False(failed.IsSuccess);
            Assert.Equal(expectedCode, failed.ErrorCode);
            Assert.Null(toolbox.Requested);
        }
    }

    [Fact]
    public async Task One_handoff_replaces_the_last_rather_than_being_added_to_it()
    {
        var toolbox = Toolbox();

        Assert.True((await toolbox.RunAsync(Call($$"""{"to":"{{ReceptionistRoles.Booker}}"}"""))).IsSuccess);
        Assert.Equal(ReceptionistRoles.Booker, toolbox.Requested);

        Assert.True((await toolbox.RunAsync(Call($$"""{"to":"{{ReceptionistRoles.Answerer}}"}"""))).IsSuccess);
        Assert.Equal(ReceptionistRoles.Answerer, toolbox.Requested);
    }

    [Fact]
    public void A_handoff_is_never_a_booking()
    {
        IReceptionistToolbox toolbox = Toolbox();

        // The handoff rides in a composite beside the calendar's toolbox; a call that only changed hands is not a call that booked.
        Assert.False(toolbox.IsBooking(HandoffToolbox.Tool));
        Assert.False(toolbox.IsBooking("book_appointment"));
        Assert.False(toolbox.IsBooking(string.Empty));
    }

    [Fact]
    public async Task What_the_enum_offers_is_what_the_lookup_accepts()
    {
        var toolbox = Toolbox();

        // The schema and the lookup are two readings of one set of destinations; if they ever disagreed, the model would be shown a name
        // the tool refuses, or would be refused a name the tool would take.
        foreach (var offered in ToProperty(toolbox).GetProperty("enum").EnumerateArray().Select(c => c.GetString()))
        {
            var result = await toolbox.RunAsync(Call(JsonSerializer.Serialize(new { to = offered })));

            Assert.True(result.IsSuccess);
            Assert.Equal(offered, toolbox.Requested);
        }
    }

    [Fact]
    public void Nothing_a_caller_said_can_reach_a_log_line_because_there_is_nothing_to_log_with()
    {
        // Zero Data Retention is held here by construction rather than by discipline: a class with no logger cannot write one, so no
        // future edit can quietly start logging the arguments the model was handed. If a logger is ever added, this fails on purpose.
        var fields = typeof(HandoffToolbox).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.DoesNotContain(fields, f => typeof(ILogger).IsAssignableFrom(f.FieldType));

        var parameters = typeof(HandoffToolbox)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(c => c.GetParameters());
        Assert.DoesNotContain(parameters, p => typeof(ILogger).IsAssignableFrom(p.ParameterType));

        var factoryParameters = typeof(HandoffToolbox)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(m => m.GetParameters());
        Assert.DoesNotContain(factoryParameters, p => typeof(ILogger).IsAssignableFrom(p.ParameterType));
    }
}
