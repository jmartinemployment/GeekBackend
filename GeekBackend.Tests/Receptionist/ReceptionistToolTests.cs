using System.Net;
using System.Text.Json;
using GeekAPI.Services.Receptionist;
using GeekAPI.Services.Receptionist.Relay;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// A receptionist that can use tools in a call: the model is told about them and shown what they answered, in each provider's own
/// shape; the session runs what the model asks for, tells it only a failure's code, stops a model that never stops asking, and ends
/// the call of a model that asks for tools the business never gave it.
/// </summary>
public sealed class ReceptionistToolTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""{"type":"object","properties":{"day":{"type":"string"}}}""").RootElement.Clone();
    private static readonly ReceptionistToolDefinition Check = new("check_day", "Look up a day.", Schema);
    private static readonly ReceptionistToolCall Ask = new("c1", "check_day", """{"day":"Monday"}""");

    private static readonly IReadOnlyList<ReceptionistTurn> Turns =
    [
        new(ReceptionistSpeaker.Caller, "Are you open Monday"),
        new(ReceptionistSpeaker.Receptionist, "Let me check."),
        new(ReceptionistSpeaker.ToolCall, """{"day":"Monday"}""", "c1", "check_day"),
        new(ReceptionistSpeaker.ToolResult, """{"open":true}""", "c1", "check_day"),
    ];

    private sealed class Handler(string body) : HttpMessageHandler
    {
        public string? Sent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private static LlmReceptionistModel Model(string provider, Handler handler)
    {
        var env = new Dictionary<string, string>
        {
            ["LlmProviders__DefaultProvider"] = provider,
            ["receptionist"] = "key-1",
            [$"LlmProviders__{provider}__Model"] = "model-1",
        };
        return new LlmReceptionistModel(new HttpClient(handler), ReceptionistModelSettings.From(k => env.GetValueOrDefault(k)), NullLogger<LlmReceptionistModel>.Instance);
    }

    [Fact]
    public async Task Anthropic_is_told_the_tool_and_shown_the_call_and_its_result_as_blocks()
    {
        var handler = new Handler("""{"content":[{"type":"text","text":"Yes."}]}""");
        await Model("Anthropic", handler).ReplyAsync("p", Turns, [Check]);

        using var sent = JsonDocument.Parse(handler.Sent!);
        Assert.Equal(["end_call", "check_day"], sent.RootElement.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal("object", sent.RootElement.GetProperty("tools")[1].GetProperty("input_schema").GetProperty("type").GetString());

        var messages = sent.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());
        Assert.Equal("Are you open Monday", messages[0].GetProperty("content").GetString());
        var assistant = messages[1].GetProperty("content");
        Assert.Equal("text", assistant[0].GetProperty("type").GetString());
        Assert.Equal("tool_use", assistant[1].GetProperty("type").GetString());
        Assert.Equal("c1", assistant[1].GetProperty("id").GetString());
        Assert.Equal("Monday", assistant[1].GetProperty("input").GetProperty("day").GetString());
        Assert.Equal("user", messages[2].GetProperty("role").GetString());
        Assert.Equal("tool_result", messages[2].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("c1", messages[2].GetProperty("content")[0].GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public async Task OpenAi_is_told_the_tool_and_shown_the_call_and_its_result_as_tool_messages()
    {
        var handler = new Handler("""{"choices":[{"message":{"role":"assistant","content":"Yes."}}]}""");
        await Model("OpenAi", handler).ReplyAsync("p", Turns, [Check]);

        using var sent = JsonDocument.Parse(handler.Sent!);
        Assert.Equal(["end_call", "check_day"], sent.RootElement.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("function").GetProperty("name").GetString()));

        var messages = sent.RootElement.GetProperty("messages");
        Assert.Equal(["system", "user", "assistant", "tool"], messages.EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("Let me check.", messages[2].GetProperty("content").GetString());
        var call = messages[2].GetProperty("tool_calls")[0];
        Assert.Equal("c1", call.GetProperty("id").GetString());
        Assert.Equal("check_day", call.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("""{"day":"Monday"}""", call.GetProperty("function").GetProperty("arguments").GetString());
        Assert.Equal("c1", messages[3].GetProperty("tool_call_id").GetString());
        Assert.Equal("""{"open":true}""", messages[3].GetProperty("content").GetString());
    }

    [Fact]
    public async Task A_tool_the_business_gave_is_a_request_and_not_a_hang_up_in_both_providers()
    {
        var anthropic = new Handler("""{"content":[{"type":"tool_use","id":"c1","name":"check_day","input":{"day":"Monday"}}]}""");
        var reply = await Model("Anthropic", anthropic).ReplyAsync("p", Turns, [Check]);
        Assert.False(reply.EndCall);
        Assert.Equal(Ask, Assert.Single(reply.ToolCalls!));

        var openAi = new Handler("""{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"c1","type":"function","function":{"name":"check_day","arguments":"{\"day\":\"Monday\"}"}}]}}]}""");
        reply = await Model("OpenAi", openAi).ReplyAsync("p", Turns, [Check]);
        Assert.False(reply.EndCall);
        Assert.Equal(Ask, Assert.Single(reply.ToolCalls!));
    }

    [Fact]
    public async Task A_tool_that_was_not_given_still_throws_when_others_were()
    {
        var handler = new Handler("""{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"c1","type":"function","function":{"name":"book","arguments":"{}"}}]}}]}""");
        await Assert.ThrowsAsync<ReceptionistModelException>(() => Model("OpenAi", handler).ReplyAsync("p", Turns, [Check]));
    }

    private sealed class Toolbox(Func<ReceptionistToolCall, Task<Result<string>>> run) : IReceptionistToolbox
    {
        public IReadOnlyList<ReceptionistToolDefinition> Tools { get; } = [Check];
        public List<ReceptionistToolCall> Ran { get; } = new();

        public Task<Result<string>> RunAsync(ReceptionistToolCall call, CancellationToken ct = default)
        {
            Ran.Add(call);
            return run(call);
        }
    }

    private const string Prompt = """{"type":"prompt","voicePrompt":"Are you open Monday","last":true}""";

    private static (ReceptionistRelaySession Session, FakeReceptionistModel Model, CapturingLogger Log) Make(IReceptionistToolbox? toolbox, params ReceptionistReply[] script)
    {
        var model = new FakeReceptionistModel();
        foreach (var reply in script) model.Script.Enqueue(reply);
        var log = new CapturingLogger();
        return (new ReceptionistRelaySession(model, "p", log) { Toolbox = toolbox }, model, log);
    }

    [Fact]
    public async Task A_requested_tool_is_run_its_result_goes_back_to_the_model_and_the_caller_hears_the_answer()
    {
        var toolbox = new Toolbox(_ => Task.FromResult(Result<string>.Success("""{"open":true}""")));
        var (session, model, _) = Make(toolbox, new ReceptionistReply("Let me check.", false, [Ask]), new ReceptionistReply("Yes, we are open.", false));

        var sent = await session.HandleAsync(Prompt, CancellationToken.None);

        Assert.Equal(Ask, Assert.Single(toolbox.Ran));
        Assert.Equal([ReceptionistSpeaker.Caller, ReceptionistSpeaker.Receptionist, ReceptionistSpeaker.ToolCall, ReceptionistSpeaker.ToolResult, ReceptionistSpeaker.Receptionist],
            session.Turns.Select(t => t.Speaker));
        Assert.Equal("""{"open":true}""", session.Turns[3].Text);
        Assert.Equal(2, model.Calls.Count);
        Assert.Equal(toolbox.Tools, model.ToolsSeen[0]);
        Assert.Equal([ReceptionistRelaySession.Text("Let me check."), ReceptionistRelaySession.Text("Yes, we are open.")], sent);
        Assert.False(session.Ended);
    }

    [Fact]
    public async Task A_failed_tool_tells_the_model_its_code_only_and_the_log_carries_no_arguments()
    {
        var toolbox = new Toolbox(_ => Task.FromResult(Result<string>.Failure("Monday is secret", "calendar_not_connected")));
        var (session, _, log) = Make(toolbox, new ReceptionistReply("", false, [Ask]), new ReceptionistReply("I cannot check.", false));

        await session.HandleAsync(Prompt, CancellationToken.None);

        Assert.Equal("""{"error":"calendar_not_connected"}""", session.Turns.Single(t => t.Speaker == ReceptionistSpeaker.ToolResult).Text);
        Assert.Contains(log.Lines, l => l.Contains("calendar_not_connected", StringComparison.Ordinal) && l.Contains("check_day", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, l => l.Contains("Monday", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tool_that_throws_is_a_tool_failed_result_and_the_call_goes_on()
    {
        var toolbox = new Toolbox(_ => throw new InvalidOperationException("Monday boom"));
        var (session, _, log) = Make(toolbox, new ReceptionistReply("", false, [Ask]), new ReceptionistReply("Sorry.", false));

        var sent = await session.HandleAsync(Prompt, CancellationToken.None);

        Assert.Equal("""{"error":"tool_failed"}""", session.Turns.Single(t => t.Speaker == ReceptionistSpeaker.ToolResult).Text);
        Assert.Equal([ReceptionistRelaySession.Text("Sorry.")], sent);
        Assert.DoesNotContain(log.Lines, l => l.Contains("Monday", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_that_asks_for_tools_the_business_has_none_of_is_told_so_and_the_call_ends()
    {
        var (session, model, log) = Make(null, new ReceptionistReply("", false, [Ask]));

        var sent = await session.HandleAsync(Prompt, CancellationToken.None);

        Assert.True(session.Ended);
        Assert.Equal(
            [ReceptionistRelaySession.Text(ReceptionistLanguages.DefaultLanguage.Failure), """{"type":"end"}"""],
            sent);
        Assert.Single(model.Calls);
        Assert.Contains(log.Lines, l => l.Contains("may not use", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_that_never_stops_asking_is_ended_after_the_allowed_rounds()
    {
        var toolbox = new Toolbox(_ => Task.FromResult(Result<string>.Success("{}")));
        var (session, model, _) = Make(toolbox, Enumerable.Repeat(new ReceptionistReply("", false, [Ask]), 10).ToArray());

        await session.HandleAsync(Prompt, CancellationToken.None);

        Assert.True(session.Ended);
        Assert.Equal(ReceptionistRelaySession.MaxToolRounds + 1, model.Calls.Count);
        Assert.Equal(ReceptionistRelaySession.MaxToolRounds, toolbox.Ran.Count);
    }

    [Fact]
    public async Task A_tool_and_a_hang_up_in_one_reply_runs_the_tool_then_ends_the_call()
    {
        var toolbox = new Toolbox(_ => Task.FromResult(Result<string>.Success("{}")));
        var (session, model, _) = Make(toolbox, new ReceptionistReply("Goodbye.", true, [Ask]));

        var sent = await session.HandleAsync(Prompt, CancellationToken.None);

        Assert.Single(toolbox.Ran);
        Assert.Single(model.Calls);
        Assert.True(session.Ended);
        Assert.Equal([ReceptionistRelaySession.Text("Goodbye."), """{"type":"end"}"""], sent);
    }

    // ----- The plan's spoken mid-call failure fallbacks -----

    /// <summary>
    /// One line, whatever the business has stored, because one thing happens: the caller is asked for a message and the call ends.
    ///
    /// <para>
    /// This test used to assert the opposite - that a business with a transfer number stored had its caller told "please hold for just
    /// a moment while I connect you directly with our team" - and then asserted the very next thing sent was <c>{"type":"end"}</c>. It
    /// was pinning a promise the product could not keep: nothing in this codebase dials, so the caller held and was hung up on. A
    /// transfer number stored changes nothing a caller hears until something dials.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_model_failure_never_promises_a_transfer_this_product_cannot_make()
    {
        var model = new FakeReceptionistModel { Fail = true };
        var session = new ReceptionistRelaySession(model, "p", new CapturingLogger());

        var sent = await session.HandleAsync(Prompt, CancellationToken.None);

        Assert.Equal([ReceptionistRelaySession.Text(ReceptionistLanguages.DefaultLanguage.Failure), """{"type":"end"}"""], sent);
        Assert.DoesNotContain("connect you", string.Join(" ", sent), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hold", string.Join(" ", sent), StringComparison.OrdinalIgnoreCase);
        Assert.True(session.Ended);
    }

    [Fact]
    public async Task A_model_failure_asks_a_caller_with_nobody_to_transfer_to_for_a_message()
    {
        var model = new FakeReceptionistModel { Fail = true };
        var session = new ReceptionistRelaySession(model, "p", new CapturingLogger());

        var sent = await session.HandleAsync(Prompt, CancellationToken.None);

        Assert.Equal([ReceptionistRelaySession.Text(ReceptionistLanguages.DefaultLanguage.Failure), """{"type":"end"}"""], sent);
    }

    [Fact]
    public async Task An_empty_reply_and_a_runaway_tool_loop_both_say_the_failure_line_too()
    {
        var empty = new ReceptionistRelaySession(new FakeReceptionistModel { Reply = "" }, "p", new CapturingLogger());
        var sentEmpty = await empty.HandleAsync(Prompt, CancellationToken.None);
        Assert.Contains(ReceptionistRelaySession.Text(ReceptionistLanguages.DefaultLanguage.Failure), sentEmpty);

        var toolbox = new Toolbox(_ => Task.FromResult(Result<string>.Success("{}")));
        var (runaway, _, _) = Make(toolbox, Enumerable.Repeat(new ReceptionistReply("", false, [Ask]), 10).ToArray());
        var sentRunaway = await runaway.HandleAsync(Prompt, CancellationToken.None);
        Assert.Contains(ReceptionistRelaySession.Text(ReceptionistLanguages.DefaultLanguage.Failure), sentRunaway);
    }

    [Fact]
    public async Task What_the_caller_was_told_is_part_of_the_call_so_the_record_says_the_receptionist_failed()
    {
        var session = new ReceptionistRelaySession(new FakeReceptionistModel { Fail = true }, "p", new CapturingLogger());

        await session.HandleAsync(Prompt, CancellationToken.None);

        Assert.Equal(ReceptionistLanguages.DefaultLanguage.Failure, session.Turns[^1].Text);
    }

}
