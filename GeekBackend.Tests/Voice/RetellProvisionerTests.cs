using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.Services.Voice.Telephony;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// A tenant's Retell side: a language model on RETELL_LLM_MODEL at temperature 0.2 with the tenant's prompt and end_call, an agent on it
/// whose webhook is the tenant's Retell route, and the tenant's Twilio number imported over its SIP trunk with that agent answering.
/// Every call is made once, a failure at any step deletes what was created (number, agent, LLM) once and names the step, and no log
/// line carries the key, a password, a prompt or a body.
/// </summary>
public sealed class RetellProvisionerTests
{
    private static readonly Guid Tenant = Guid.Parse("3f2c1b7a-0d4e-4f6a-9b8c-1a2b3c4d5e6f");
    private const string Key = "key_retell_secret_value";
    private const string SipPassword = "SipPassword1Secret";
    private const string PromptText = "## Role the secret prompt text";

    private sealed record Seen(HttpMethod Method, string Url, string Authorization, string Body)
    {
        public bool Ends(string suffix) => Url.EndsWith(suffix, StringComparison.Ordinal);
    }

    private sealed class StubHandler(Func<Seen, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var seen = new Seen(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization!.ToString(), body);
            Requests.Add(seen);
            return respond(seen);
        }
    }

    private sealed class CapturingLogger : ILogger<RetellProvisioner>
    {
        public List<(LogLevel Level, string Line)> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception) + (exception?.ToString() ?? string.Empty)));
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.Created) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Happy(Seen seen)
    {
        if (seen.Ends("/create-retell-llm")) return Json("""{"llm_id":"llm_1"}""");
        if (seen.Ends("/create-agent")) return Json("""{"agent_id":"agent_1"}""");
        if (seen.Ends("/import-phone-number")) return Json("""{"phone_number":"+15615550123"}""");
        if (seen.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NoContent);
        throw new InvalidOperationException("unexpected request " + seen.Url);
    }

    private static readonly RetellAgentScript Script = new("Thank you for calling Boca Cool HVAC.", PromptText, "retell-Lily");

    private static readonly ProvisionedTelephony Telephony = new(
        "ACsub1", "sub-token", "TKtrunk1", $"gr-{Tenant:N}.pstn.twilio.com", "gruser", SipPassword, "+15615550123", "PNphone1");

    private static (RetellProvisioner Provisioner, StubHandler Handler, CapturingLogger Log) Make(Func<Seen, HttpResponseMessage>? respond = null, RetellOptions? options = null)
    {
        var handler = new StubHandler(respond ?? Happy);
        var log = new CapturingLogger();
        var opts = options ?? new RetellOptions(Key, "gpt-4.1-mini", "https://api.example.test", "webhook_key");
        return (new RetellProvisioner(new HttpClient(handler), opts, log), handler, log);
    }

    [Fact]
    public async Task A_tenant_gets_a_model_an_agent_and_its_number_imported_with_that_agent_answering()
    {
        var (provisioner, handler, _) = Make();

        var result = await provisioner.ProvisionAsync(Tenant, Script, Telephony);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ProvisionedRetell("llm_1", "agent_1", "+15615550123"), result.Value);
        Assert.Equal(
            ["create-retell-llm", "create-agent", "import-phone-number"],
            handler.Requests.Select(r => r.Url["https://api.retellai.com/".Length..]));
        Assert.All(handler.Requests, r => Assert.Equal("Bearer " + Key, r.Authorization));
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Post, r.Method));

        using var llm = JsonDocument.Parse(handler.Requests[0].Body);
        var model = llm.RootElement.GetProperty("model_config");
        Assert.Equal("cascading", model.GetProperty("type").GetString());
        Assert.Equal("gpt-4.1-mini", model.GetProperty("primary").GetProperty("model").GetString());
        Assert.Equal(0.2, model.GetProperty("temperature").GetDouble());
        Assert.Equal(PromptText, llm.RootElement.GetProperty("general_prompt").GetString());
        Assert.Equal(Script.Greeting, llm.RootElement.GetProperty("begin_message").GetString());
        Assert.Equal("agent", llm.RootElement.GetProperty("start_speaker").GetString());
        var tool = Assert.Single(llm.RootElement.GetProperty("general_tools").EnumerateArray());
        Assert.Equal("end_call", tool.GetProperty("type").GetString());
        Assert.Equal("end_call", tool.GetProperty("name").GetString());

        using var agent = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.Equal("retell-llm", agent.RootElement.GetProperty("response_engine").GetProperty("type").GetString());
        Assert.Equal("llm_1", agent.RootElement.GetProperty("response_engine").GetProperty("llm_id").GetString());
        Assert.Equal("retell-Lily", agent.RootElement.GetProperty("voice_id").GetString());
        Assert.Equal($"https://api.example.test/api/receptionist/retell/webhook/{Tenant}", agent.RootElement.GetProperty("webhook_url").GetString());

        using var number = JsonDocument.Parse(handler.Requests[2].Body);
        Assert.Equal("+15615550123", number.RootElement.GetProperty("phone_number").GetString());
        Assert.Equal($"gr-{Tenant:N}.pstn.twilio.com", number.RootElement.GetProperty("termination_uri").GetString());
        Assert.Equal("gruser", number.RootElement.GetProperty("sip_trunk_auth_username").GetString());
        Assert.Equal(SipPassword, number.RootElement.GetProperty("sip_trunk_auth_password").GetString());
        var inbound = Assert.Single(number.RootElement.GetProperty("inbound_agents").EnumerateArray());
        Assert.Equal("agent_1", inbound.GetProperty("agent_id").GetString());
        Assert.Equal(1, inbound.GetProperty("weight").GetInt32());
    }

    [Fact]
    public async Task Unset_variables_refuse_before_anything_is_created_and_the_log_names_them_without_a_value()
    {
        var (provisioner, handler, log) = Make(options: new RetellOptions(Key, null, " ", "webhook_key"));

        var result = await provisioner.ProvisionAsync(Tenant, Script, Telephony);

        Assert.False(result.IsSuccess);
        Assert.Equal(RetellFailureCodes.NotConfigured, result.ErrorCode);
        Assert.Empty(handler.Requests);
        var line = Assert.Single(log.Lines, l => l.Level == LogLevel.Error).Line;
        Assert.Contains("RETELL_LLM_MODEL", line);
        Assert.Contains("PUBLIC_BASE_URL", line);
        Assert.DoesNotContain("RETELL_API_KEY", line);
        Assert.DoesNotContain(Key, line);
    }

    [Fact]
    public void The_missing_list_is_in_the_plans_order_and_empty_when_all_are_set()
    {
        Assert.Equal(["RETELL_API_KEY", "RETELL_LLM_MODEL", "PUBLIC_BASE_URL", "RETELL_WEBHOOK_SECRET"], new RetellOptions(null, "", null, " ").Missing());
        Assert.Empty(new RetellOptions("k", "m", "https://x.test", "w").Missing());
    }

    [Fact]
    public void Options_are_read_from_the_variables_and_the_address_loses_a_trailing_slash()
    {
        var vars = new Dictionary<string, string?>
        {
            ["RETELL_API_KEY"] = " key ",
            ["RETELL_LLM_MODEL"] = " gpt-4.1-mini ",
            ["PUBLIC_BASE_URL"] = " https://api.example.test/ ",
            ["RETELL_WEBHOOK_SECRET"] = " webhook_key ",
        };

        Assert.Equal(new RetellOptions("key", "gpt-4.1-mini", "https://api.example.test", "webhook_key"), RetellOptions.From(name => vars.GetValueOrDefault(name)));
    }

    [Theory]
    [InlineData("create-retell-llm", new string[0])]
    [InlineData("create-agent", new[] { "delete-retell-llm/llm_1" })]
    [InlineData("import-phone-number", new[] { "delete-agent/agent_1", "delete-retell-llm/llm_1" })]
    public async Task A_refused_step_deletes_what_was_created_once_and_answers_with_the_failure(string failing, string[] expectedDeletes)
    {
        var (provisioner, handler, _) = Make(seen => seen.Ends(failing) ? Json("""{"status":"error"}""", HttpStatusCode.BadRequest) : Happy(seen));

        var result = await provisioner.ProvisionAsync(Tenant, Script, Telephony);

        Assert.False(result.IsSuccess);
        Assert.Equal(RetellFailureCodes.ProvisioningFailed, result.ErrorCode);
        Assert.Equal(
            expectedDeletes,
            handler.Requests.Where(r => r.Method == HttpMethod.Delete).Select(r => r.Url["https://api.retellai.com/".Length..]));
        Assert.Single(handler.Requests, r => r.Ends(failing));
    }

    [Theory]
    [InlineData("create-retell-llm", "language model")]
    [InlineData("create-agent", "agent")]
    [InlineData("import-phone-number", "phone number")]
    public async Task A_failure_names_the_step(string failing, string step)
    {
        var (provisioner, _, _) = Make(seen => seen.Ends(failing) ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Happy(seen));

        var result = await provisioner.ProvisionAsync(Tenant, Script, Telephony);

        Assert.Contains(step, result.Error);
    }

    [Theory]
    [InlineData("create-retell-llm", """{"nothing":"here"}""")]
    [InlineData("create-agent", """{"agent_id":""}""")]
    [InlineData("import-phone-number", """[]""")]
    public async Task An_answer_without_the_documented_field_is_a_failure_and_undoes_what_exists(string step, string body)
    {
        var (provisioner, handler, log) = Make(seen => seen.Ends(step) ? Json(body) : Happy(seen));

        var result = await provisioner.ProvisionAsync(Tenant, Script, Telephony);

        Assert.False(result.IsSuccess);
        Assert.Equal(RetellFailureCodes.ProvisioningFailed, result.ErrorCode);
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Error);
        Assert.Equal(step switch { "create-retell-llm" => 0, "create-agent" => 1, _ => 2 }, handler.Requests.Count(r => r.Method == HttpMethod.Delete));
    }

    [Fact]
    public async Task A_delete_that_fails_is_logged_by_its_id_and_the_original_failure_is_what_is_answered()
    {
        var (provisioner, handler, log) = Make(seen =>
            seen.Ends("import-phone-number") ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : seen.Method == HttpMethod.Delete && seen.Ends("delete-agent/agent_1") ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Happy(seen));

        var result = await provisioner.ProvisionAsync(Tenant, Script, Telephony);

        Assert.Equal(RetellFailureCodes.ProvisioningFailed, result.ErrorCode);
        Assert.Contains("import the phone number", result.Error);
        Assert.Equal(2, handler.Requests.Count(r => r.Method == HttpMethod.Delete));
        var line = Assert.Single(log.Lines, l => l.Level == LogLevel.Error && l.Line.Contains("agent_1", StringComparison.Ordinal)).Line;
        Assert.Contains("by hand", line);
    }

    [Fact]
    public async Task An_unreachable_retell_is_a_failure_that_names_the_step_and_leaves_nothing_to_delete()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var log = new CapturingLogger();
        var provisioner = new RetellProvisioner(new HttpClient(handler), new RetellOptions(Key, "gpt-4.1-mini", "https://api.example.test", "webhook_key"), log);

        var result = await provisioner.ProvisionAsync(Tenant, Script, Telephony);

        Assert.Equal(RetellFailureCodes.ProvisioningFailed, result.ErrorCode);
        Assert.Contains("create the language model", result.Error);
        Assert.Single(handler.Requests);
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Error);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_is_a_failure()
    {
        var (provisioner, _, _) = Make(seen => seen.Ends("create-retell-llm") ? Json("<html>", HttpStatusCode.Created) : Happy(seen));

        var result = await provisioner.ProvisionAsync(Tenant, Script, Telephony);

        Assert.Equal(RetellFailureCodes.ProvisioningFailed, result.ErrorCode);
    }

    [Fact]
    public async Task Abandoning_deletes_the_number_then_the_agent_then_the_model_each_once()
    {
        var (provisioner, handler, _) = Make();

        var all = await provisioner.AbandonAsync(new ProvisionedRetell("llm_1", "agent_1", "+15615550123"), Tenant);

        Assert.True(all);
        Assert.Equal(
            ["delete-phone-number/%2B15615550123", "delete-agent/agent_1", "delete-retell-llm/llm_1"],
            handler.Requests.Select(r => r.Url["https://api.retellai.com/".Length..]));
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Delete, r.Method));
    }

    [Fact]
    public async Task Abandoning_skips_what_was_never_created_and_reports_a_delete_that_failed()
    {
        var (provisioner, handler, _) = Make(seen => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var all = await provisioner.AbandonAsync(new ProvisionedRetell("llm_1", "", ""), Tenant);

        Assert.False(all);
        var only = Assert.Single(handler.Requests);
        Assert.True(only.Ends("delete-retell-llm/llm_1"));
    }

    [Fact]
    public async Task No_log_line_carries_the_key_a_password_a_prompt_or_a_body()
    {
        var (provisioner, _, log) = Make(seen => seen.Ends("import-phone-number") ? Json("""{"error":"bad body marker"}""", HttpStatusCode.BadRequest) : Happy(seen));

        await provisioner.ProvisionAsync(Tenant, Script, Telephony);

        Assert.NotEmpty(log.Lines);
        Assert.All(log.Lines, l =>
        {
            Assert.DoesNotContain(Key, l.Line);
            Assert.DoesNotContain(SipPassword, l.Line);
            Assert.DoesNotContain("secret prompt", l.Line);
            Assert.DoesNotContain("bad body marker", l.Line);
        });
    }
}
