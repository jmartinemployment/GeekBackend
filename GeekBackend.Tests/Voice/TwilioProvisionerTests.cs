using System.Net;
using System.Text;
using GeekAPI.Services.Voice.Telephony;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// A tenant's Twilio side: a subaccount and a number in the tenant's area code whose calls and call statuses Twilio posts to GeekAPI's
/// tenant routes, with no SIP trunk. Every call is made once, the parent credentials are used only for the subaccount itself, a failure at any
/// step closes the subaccount once and names the step, and no log line carries a token or a body.
/// </summary>
public sealed class TwilioProvisionerTests
{
    private static readonly Guid Tenant = Guid.Parse("3f2c1b7a-0d4e-4f6a-9b8c-1a2b3c4d5e6f");
    private const string MasterSid = "ACmaster";
    private const string MasterToken = "master-token-secret";
    private const string SubSid = "ACsub1";
    private const string SubToken = "sub-token-secret";

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
            var seen = new Seen(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization!.Parameter!, body);
            Requests.Add(seen);
            return respond(seen);
        }
    }

    private sealed class CapturingLogger : ILogger<TwilioProvisioner>
    {
        public List<(LogLevel Level, string Line)> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception) + (exception?.ToString() ?? string.Empty)));
    }

    private static string Basic(string sid, string token) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:{token}"));

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.Created) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Happy(Seen seen)
    {
        if (seen.Ends("/Accounts.json")) return Json($$"""{"sid":"{{SubSid}}","auth_token":"{{SubToken}}","status":"active"}""");
        if (seen.Ends("/Accounts/" + SubSid + ".json")) return Json("""{"status":"closed"}""", HttpStatusCode.OK);
        if (seen.Ends("/IncomingPhoneNumbers.json")) return Json("""{"sid":"PNphone1","phone_number":"+15615550123"}""");
        throw new InvalidOperationException("unexpected request " + seen.Url);
    }

    private static (TwilioProvisioner Provisioner, StubHandler Handler, CapturingLogger Log) Make(Func<Seen, HttpResponseMessage>? respond = null, TwilioOptions? options = null)
    {
        var handler = new StubHandler(respond ?? Happy);
        var log = new CapturingLogger();
        var opts = options ?? new TwilioOptions(MasterSid, MasterToken, "https://api.example.test");
        return (new TwilioProvisioner(new HttpClient(handler), opts, log), handler, log);
    }

    [Fact]
    public async Task A_signup_gets_a_subaccount_and_a_number_whose_calls_and_statuses_go_to_the_tenants_routes()
    {
        var (provisioner, handler, _) = Make();

        var result = await provisioner.ProvisionAsync(Tenant, "561");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new ProvisionedTelephony(SubSid, SubToken, "+15615550123", "PNphone1"), result.Value);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Post, r.Method));
        Assert.EndsWith($"/Accounts/{SubSid}/IncomingPhoneNumbers.json", handler.Requests[1].Url);

        var body = handler.Requests[1].Body;
        Assert.Contains("AreaCode=561", body);
        Assert.Contains($"VoiceUrl={Uri.EscapeDataString($"https://api.example.test/api/receptionist/tenant/{Tenant}/voice")}", body);
        Assert.Contains("VoiceMethod=POST", body);
        Assert.Contains($"StatusCallback={Uri.EscapeDataString($"https://api.example.test/api/receptionist/tenant/{Tenant}/call-status")}", body);
        Assert.Contains("StatusCallbackMethod=POST", body);
    }

    [Fact]
    public async Task No_sip_trunk_or_credential_is_made()
    {
        var (provisioner, handler, _) = Make();

        await provisioner.ProvisionAsync(Tenant, "561");

        Assert.DoesNotContain(handler.Requests, r => r.Url.Contains("trunking.twilio.com", StringComparison.Ordinal) || r.Url.Contains("/SIP/", StringComparison.Ordinal));
        Assert.DoesNotContain("TrunkSid", handler.Requests[1].Body);
    }

    [Fact]
    public void The_routes_set_on_the_number_are_the_tenant_call_routes()
    {
        Assert.Equal($"/api/receptionist/tenant/{Tenant}/voice", TwilioProvisioner.TenantVoicePath(Tenant));
        Assert.Equal($"/api/receptionist/tenant/{Tenant}/call-status", TwilioProvisioner.TenantCallStatusPath(Tenant));
    }

    [Fact]
    public async Task Only_the_subaccount_itself_is_made_with_the_parent_credentials()
    {
        var (provisioner, handler, _) = Make();

        await provisioner.ProvisionAsync(Tenant, "561");

        Assert.Equal(Basic(MasterSid, MasterToken), handler.Requests[0].Authorization);
        Assert.All(handler.Requests.Skip(1), r => Assert.Equal(Basic(SubSid, SubToken), r.Authorization));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("56")]
    [InlineData("5612")]
    [InlineData("5a1")]
    [InlineData("+56")]
    public async Task An_area_code_that_is_not_three_digits_is_refused_before_anything_is_called(string? areaCode)
    {
        var (provisioner, handler, _) = Make();

        var result = await provisioner.ProvisionAsync(Tenant, areaCode);

        Assert.False(result.IsSuccess);
        Assert.Equal(TelephonyFailureCodes.AreaCodeInvalid, result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Unset_variables_are_refused_before_anything_is_called_and_the_log_names_them()
    {
        var (provisioner, handler, log) = Make(options: new TwilioOptions(null, " ", null));

        var result = await provisioner.ProvisionAsync(Tenant, "561");

        Assert.False(result.IsSuccess);
        Assert.Equal(TelephonyFailureCodes.TelephonyNotConfigured, result.ErrorCode);
        Assert.Empty(handler.Requests);
        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Contains("TWILIO_MASTER_ACCOUNT_SID, TWILIO_MASTER_AUTH_TOKEN, PUBLIC_BASE_URL", line.Line);
    }

    [Fact]
    public async Task The_public_address_is_needed_for_every_tenant()
    {
        var (provisioner, handler, _) = Make(options: new TwilioOptions(MasterSid, MasterToken, null));

        Assert.Equal(["PUBLIC_BASE_URL"], provisioner.Missing());
        var result = await provisioner.ProvisionAsync(Tenant, "561");
        Assert.Equal(TelephonyFailureCodes.TelephonyNotConfigured, result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("/Accounts.json", false)]
    [InlineData("/IncomingPhoneNumbers.json", true)]
    public async Task A_refusal_at_any_step_closes_the_subaccount_once_and_names_the_step(string failingUrl, bool subaccountExists)
    {
        var (provisioner, handler, log) = Make(seen =>
            seen.Ends(failingUrl)
                ? Json("""{"code":21452,"message":"no numbers in that area code"}""", HttpStatusCode.BadRequest)
                : Happy(seen));

        var result = await provisioner.ProvisionAsync(Tenant, "561");

        Assert.False(result.IsSuccess);
        Assert.Equal(TelephonyFailureCodes.ProvisioningFailed, result.ErrorCode);
        var closes = handler.Requests.Where(r => r.Ends($"/Accounts/{SubSid}.json")).ToList();
        if (subaccountExists)
        {
            var close = Assert.Single(closes);
            Assert.Equal("Status=closed", close.Body);
            Assert.Equal(Basic(MasterSid, MasterToken), close.Authorization);
            Assert.Same(close, handler.Requests[^1]);
        }
        else
        {
            Assert.Empty(closes);
            Assert.Single(handler.Requests);
        }
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Error && l.Line.Contains("code 21452"));
    }

    [Fact]
    public async Task A_step_that_fails_is_not_retried()
    {
        var (provisioner, handler, _) = Make(seen => seen.Ends("/IncomingPhoneNumbers.json") ? Json("{}", HttpStatusCode.InternalServerError) : Happy(seen));

        await provisioner.ProvisionAsync(Tenant, "561");

        Assert.Single(handler.Requests, r => r.Ends("/IncomingPhoneNumbers.json"));
    }

    [Fact]
    public async Task A_subaccount_that_cannot_be_closed_is_logged_by_sid_at_error_so_it_can_be_closed_by_hand()
    {
        var (provisioner, _, log) = Make(seen =>
            seen.Ends("/IncomingPhoneNumbers.json") || seen.Ends($"/Accounts/{SubSid}.json")
                ? Json("{}", HttpStatusCode.ServiceUnavailable)
                : Happy(seen));

        var result = await provisioner.ProvisionAsync(Tenant, "561");

        Assert.False(result.IsSuccess);
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Error && l.Line.Contains(SubSid) && l.Line.Contains("closed by hand"));
    }

    [Fact]
    public async Task An_answer_missing_a_documented_field_is_a_failure_that_closes_the_subaccount()
    {
        var (provisioner, handler, _) = Make(seen => seen.Ends("/IncomingPhoneNumbers.json") ? Json("""{"sid":"PNphone1"}""") : Happy(seen));

        var result = await provisioner.ProvisionAsync(Tenant, "561");

        Assert.False(result.IsSuccess);
        Assert.Equal(TelephonyFailureCodes.ProvisioningFailed, result.ErrorCode);
        Assert.EndsWith($"/Accounts/{SubSid}.json", handler.Requests[^1].Url);
    }

    [Fact]
    public async Task A_subaccount_answer_without_a_token_is_a_failure()
    {
        var (provisioner, handler, _) = Make(seen => seen.Ends("/Accounts.json") ? Json($$"""{"sid":"{{SubSid}}"}""") : Happy(seen));

        var result = await provisioner.ProvisionAsync(Tenant, "561");

        Assert.False(result.IsSuccess);
        Assert.Equal(TelephonyFailureCodes.ProvisioningFailed, result.ErrorCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task No_log_line_or_error_carries_a_token_or_a_body()
    {
        var (provisioner, _, log) = Make(seen =>
            seen.Ends("/IncomingPhoneNumbers.json")
                ? Json($$"""{"code":21452,"message":"secret-body-text {{SubToken}}"}""", HttpStatusCode.BadRequest)
                : Happy(seen));

        var result = await provisioner.ProvisionAsync(Tenant, "561");

        var everything = result.Error + string.Join('\n', log.Lines.Select(l => l.Line));
        Assert.DoesNotContain(MasterToken, everything);
        Assert.DoesNotContain(SubToken, everything);
        Assert.DoesNotContain("secret-body-text", everything);
    }

    [Fact]
    public async Task An_unreachable_twilio_is_a_failure_and_the_clean_up_is_one_attempt()
    {
        var calls = 0;
        var handler = new StubHandler(seen =>
        {
            calls++;
            if (seen.Ends("/Accounts.json")) return Happy(seen);
            throw new HttpRequestException("connection refused");
        });
        var provisioner = new TwilioProvisioner(new HttpClient(handler), new TwilioOptions(MasterSid, MasterToken, "https://api.example.test"), new CapturingLogger());

        var result = await provisioner.ProvisionAsync(Tenant, "561");

        Assert.False(result.IsSuccess);
        Assert.Equal(TelephonyFailureCodes.ProvisioningFailed, result.ErrorCode);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Abandoning_closes_a_subaccount_with_the_parent_credentials()
    {
        var (provisioner, handler, _) = Make();

        var closed = await provisioner.AbandonAsync(SubSid, Tenant);

        Assert.True(closed);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith($"/Accounts/{SubSid}.json", request.Url);
        Assert.Equal("Status=closed", request.Body);
        Assert.Equal(Basic(MasterSid, MasterToken), request.Authorization);
    }

    [Fact]
    public async Task Abandoning_without_parent_credentials_makes_no_call_and_says_so_in_the_log()
    {
        var (provisioner, handler, log) = Make(options: new TwilioOptions(null, null, null));

        var closed = await provisioner.AbandonAsync(SubSid, Tenant);

        Assert.False(closed);
        Assert.Empty(handler.Requests);
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Error && l.Line.Contains(SubSid));
    }
}
