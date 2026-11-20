using System.Text.Json;
using GeekAPI.Services.Receptionist.Knowledge;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Results;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// One receptionist's tools drawn from more than one toolbox, shown to the model as a single set: nothing to compose is no toolbox,
/// one toolbox is itself, and two are their tools in order with a name offered twice shown once. A call goes to the box that offered
/// the tool and to no other, a name no box offered is refused rather than guessed at, and only the booking tool of the box that owns
/// it counts as booking.
/// </summary>
public sealed class CompositeToolboxTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""{"type":"object","properties":{"q":{"type":"string"}}}""").RootElement.Clone();

    /// <summary>
    /// One source's toolbox: the names it offers, what running any of them answers, the one tool it books with (when it has one), and
    /// every call it was handed, so a test can say which box a call reached and which never heard of it.
    /// </summary>
    private sealed class Box : IReceptionistToolbox
    {
        public Box(string label, params string[] toolNames) =>
            Tools = toolNames.Select(name => new ReceptionistToolDefinition(name, $"{label}: {name}", Schema)).ToList();

        public IReadOnlyList<ReceptionistToolDefinition> Tools { get; }

        /// <summary>The one tool this box books with. Null is a box that books nothing, which is the ordinary case.</summary>
        public string? BookingTool { get; init; }

        /// <summary>What every tool of this box answers, so a test can tell one box's answer from another's.</summary>
        public string Answer { get; init; } = "{}";

        /// <summary>Every call this box was asked to run, in order.</summary>
        public List<ReceptionistToolCall> Ran { get; } = new();

        public bool IsBooking(string toolName) =>
            BookingTool is not null && string.Equals(toolName, BookingTool, StringComparison.Ordinal);

        public Task<Result<string>> RunAsync(ReceptionistToolCall call, CancellationToken ct = default)
        {
            Ran.Add(call);
            return Task.FromResult(Result<string>.Success(Answer));
        }
    }

    private static IReceptionistToolbox Composite(params IReceptionistToolbox?[] boxes)
    {
        var composed = CompositeToolbox.Of(boxes);
        Assert.NotNull(composed);
        return composed!;
    }

    private static ReceptionistToolCall Call(string toolName) => new("c1", toolName, """{"q":"Dana"}""");

    [Fact]
    public void Nothing_to_compose_is_no_toolbox()
    {
        Assert.Null(CompositeToolbox.Of());
        Assert.Null(CompositeToolbox.Of(null, null, null));
    }

    [Fact]
    public void Toolboxes_that_offer_nothing_are_no_toolbox()
    {
        Assert.Null(CompositeToolbox.Of(new Box("first"), new Box("second")));
        Assert.Null(CompositeToolbox.Of(null, new Box("only"), null));
    }

    [Fact]
    public void One_toolbox_is_returned_as_itself_and_not_wrapped()
    {
        var only = new Box("only", "hours");

        Assert.Same(only, CompositeToolbox.Of(null, only, null));
        // An empty box beside it is dropped rather than composed with, so there is still nothing to wrap.
        Assert.Same(only, CompositeToolbox.Of(new Box("empty"), only));
    }

    [Fact]
    public void Two_toolboxes_are_one_set_of_tools_with_the_first_boxs_first()
    {
        var first = new Box("first", "hours", "address");
        var second = new Box("second", "directions", "parking");

        var composite = Composite(first, second);

        Assert.Equal(["hours", "address", "directions", "parking"], composite.Tools.Select(t => t.Name));
    }

    [Fact]
    public void A_name_both_boxes_offer_is_shown_once_and_it_is_the_first_boxs_definition()
    {
        var first = new Box("first", "hours", "shared");
        var second = new Box("second", "shared", "parking");

        var composite = Composite(first, second);

        Assert.Equal(["hours", "shared", "parking"], composite.Tools.Select(t => t.Name));
        var shared = Assert.Single(composite.Tools, t => t.Name == "shared");
        Assert.Equal("first: shared", shared.Description);
    }

    [Fact]
    public async Task A_call_goes_to_the_box_that_offered_the_tool_and_nowhere_else()
    {
        var first = new Box("first", "hours") { Answer = """{"from":"first"}""" };
        var second = new Box("second", "parking") { Answer = """{"from":"second"}""" };
        var composite = Composite(first, second);

        var result = await composite.RunAsync(Call("parking"));

        Assert.True(result.IsSuccess);
        Assert.Equal("""{"from":"second"}""", result.Value);
        Assert.Equal("parking", Assert.Single(second.Ran).Name);
        Assert.Empty(first.Ran);
    }

    [Fact]
    public async Task A_name_both_boxes_offer_is_run_by_the_first_box()
    {
        var first = new Box("first", "shared") { Answer = """{"from":"first"}""" };
        var second = new Box("second", "shared") { Answer = """{"from":"second"}""" };
        var composite = Composite(first, second);

        var result = await composite.RunAsync(Call("shared"));

        Assert.True(result.IsSuccess);
        Assert.Equal("""{"from":"first"}""", result.Value);
        Assert.Equal("shared", Assert.Single(first.Ran).Name);
        Assert.Empty(second.Ran);
    }

    [Fact]
    public async Task A_name_no_box_offered_is_refused_as_not_allowed_and_reaches_no_box()
    {
        var first = new Box("first", "hours");
        var second = new Box("second", "parking");
        var composite = Composite(first, second);

        var result = await composite.RunAsync(Call("wire_the_money"));

        Assert.False(result.IsSuccess);
        Assert.Equal(McpFailureCodes.ToolNotAllowed, result.ErrorCode);
        // It is refused here, not handed to a box to answer for a tool that is not its own.
        Assert.Empty(first.Ran);
        Assert.Empty(second.Ran);
    }

    [Fact]
    public void Only_the_booking_tool_of_the_box_that_owns_it_is_booking()
    {
        var first = new Box("first", "hours", "book_now", "shared_book") { BookingTool = "book_now" };
        var second = new Box("second", "address", "shared_book") { BookingTool = "shared_book" };
        var composite = Composite(first, second);

        Assert.True(composite.IsBooking("book_now"));
        Assert.False(composite.IsBooking("hours"));
        Assert.False(composite.IsBooking("address"));
        // The second box books with this name, but the first box offered it, so the first box's tool is the one that would run — and it
        // does not book.
        Assert.False(composite.IsBooking("shared_book"));
    }

    [Fact]
    public void An_unknown_tool_is_not_booking_rather_than_an_exception()
    {
        var first = new Box("first", "hours") { BookingTool = "hours" };
        var second = new Box("second", "parking");
        var composite = Composite(first, second);

        Assert.False(composite.IsBooking("book_a_flight"));
        Assert.False(composite.IsBooking(string.Empty));
    }
}
