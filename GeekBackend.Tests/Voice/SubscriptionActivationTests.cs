using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.Controllers.Voice;
using GeekAPI.Services.Voice;
using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.Onboarding;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// Activation of a tenant that is waiting for payment: everything that can be refused without creating anything is refused first, PayPal
/// is asked before anything is created, Twilio and Retell are set up in that order, the starting settings are saved before the tenant is
/// activated, and a failure after something exists undoes it once (Retell, then Twilio), leaves the tenant waiting and names the step.
/// </summary>
public sealed class SubscriptionActivationTests
{
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Paid = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextBilling = new(2026, 12, 14, 15, 0, 0, TimeSpan.Zero);
    private const string KeyBase64 = "BwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwc=";

    private static ActivateSubscriptionRequest Request(bool sms = false) =>
        new(TenantId, "STARTER", "I-SUBSCRIPTION123", "VAULT-TOKEN-456", "561", sms);

    private static VoiceTenant Tenant(string status = VoiceTenantStatuses.PendingPayment, string state = "FL") => new(
        TenantId, "Boca Cool HVAC", "HVAC", false, null, null, Paid, "US", state, "Palm Beach", "owner@bocacool.example", "+15615550101", status, Paid);

    private sealed class Store(List<string> calls) : IVoiceStore
    {
        public Result<VoiceTenant> Tenant { get; set; } = Result<VoiceTenant>.Success(SubscriptionActivationTests.Tenant());
        public Func<Result<VoiceReceptionistConfig>>? Save { get; set; }
        public Func<Result<VoiceTenant>>? Activate { get; set; }
        public List<VoiceReceptionistConfig> SavedConfigs { get; } = new();
        public List<ActivateVoiceTenantCommand> Activations { get; } = new();

        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default)
        {
            calls.Add("store GetTenant");
            return Task.FromResult(Tenant);
        }

        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default)
        {
            calls.Add("store SaveConfig");
            SavedConfigs.Add(config);
            return Task.FromResult(Save is not null ? Save() : Result<VoiceReceptionistConfig>.Success(config));
        }

        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default)
        {
            calls.Add("store Activate");
            Activations.Add(command);
            return Task.FromResult(Activate is not null ? Activate() : Result<VoiceTenant>.Success(SubscriptionActivationTests.Tenant(VoiceTenantStatuses.Active)));
        }

        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceKnowledgeDocumentCreated>> CreateKnowledgeDocumentAsync(Guid tenantId, CreateVoiceKnowledgeDocumentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VoiceKnowledgeHit>>> SearchKnowledgeAsync(Guid tenantId, SearchVoiceKnowledgeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Router(string service, List<string> calls, Func<string, HttpMethod, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            calls.Add($"{service} {request.Method} {path}");
            return Task.FromResult(respond(path, request.Method));
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string PayPalSubscription() => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["id"] = "I-SUBSCRIPTION123",
        ["status"] = "ACTIVE",
        ["plan_id"] = "P-PLAN",
        ["billing_info"] = new Dictionary<string, object?>
        {
            ["last_payment"] = new Dictionary<string, object?> { ["amount"] = new { currency_code = "USD", value = "30.00" }, ["time"] = Paid.ToString("O") },
            ["next_billing_time"] = NextBilling.ToString("O"),
        },
    });

    private static string PayPalPlan() => JsonSerializer.Serialize(new
    {
        id = "P-PLAN",
        status = "ACTIVE",
        billing_cycles = new[] { new { tenure_type = "REGULAR", sequence = 1, frequency = new { interval_unit = "MONTH", interval_count = 1 }, pricing_scheme = new { fixed_price = new { currency_code = "USD", value = "30.00" } } } },
    });

    private static HttpResponseMessage PayPalHappy(string path, HttpMethod _)
    {
        if (path == "/v1/oauth2/token") return Json("""{"access_token":"A21AAtoken","token_type":"Bearer"}""");
        if (path.StartsWith("/v1/billing/subscriptions/", StringComparison.Ordinal)) return Json(PayPalSubscription());
        if (path.StartsWith("/v1/billing/plans/", StringComparison.Ordinal)) return Json(PayPalPlan());
        if (path.StartsWith("/v3/vault/payment-tokens/", StringComparison.Ordinal)) return Json("""{"id":"VAULT-TOKEN-456","payment_source":{"paypal":{"payer_id":"PAYER"}}}""");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage TwilioHappy(string path, HttpMethod _)
    {
        if (path.EndsWith("/Accounts.json", StringComparison.Ordinal)) return Json("""{"sid":"ACsub1","auth_token":"sub-token-secret"}""", HttpStatusCode.Created);
        if (path.EndsWith("/Accounts/ACsub1.json", StringComparison.Ordinal)) return Json("""{"status":"closed"}""");
        if (path.EndsWith("/CredentialLists.json", StringComparison.Ordinal)) return Json("""{"sid":"CLlist1"}""", HttpStatusCode.Created);
        if (path.EndsWith("/Credentials.json", StringComparison.Ordinal)) return Json("""{"sid":"CRcred1"}""", HttpStatusCode.Created);
        if (path.EndsWith("/v1/Trunks", StringComparison.Ordinal)) return Json($$"""{"sid":"TKtrunk1","domain_name":"gr-{{TenantId:N}}.pstn.twilio.com"}""", HttpStatusCode.Created);
        if (path.EndsWith("/OriginationUrls", StringComparison.Ordinal)) return Json("""{"sid":"OUorig1"}""", HttpStatusCode.Created);
        if (path.EndsWith("/Trunks/TKtrunk1/CredentialLists", StringComparison.Ordinal)) return Json("""{"sid":"CLassoc1"}""", HttpStatusCode.Created);
        if (path.EndsWith("/IncomingPhoneNumbers.json", StringComparison.Ordinal)) return Json("""{"sid":"PNphone1","phone_number":"+15615550123"}""", HttpStatusCode.Created);
        throw new InvalidOperationException("unexpected twilio request " + path);
    }

    private static HttpResponseMessage RetellHappy(string path, HttpMethod method)
    {
        if (method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NoContent);
        if (path.EndsWith("/create-retell-llm", StringComparison.Ordinal)) return Json("""{"llm_id":"llm_1"}""", HttpStatusCode.Created);
        if (path.EndsWith("/create-agent", StringComparison.Ordinal)) return Json("""{"agent_id":"agent_1"}""", HttpStatusCode.Created);
        if (path.EndsWith("/import-phone-number", StringComparison.Ordinal)) return Json("""{"phone_number":"+15615550123"}""", HttpStatusCode.Created);
        throw new InvalidOperationException("unexpected retell request " + path);
    }

    private sealed class World
    {
        public List<string> Calls { get; } = new();
        public Store Store { get; }
        public SubscriptionActivation Activation { get; }
        public VoiceCredentialProtector Protector { get; } = new(() => KeyBase64);

        public World(
            Func<string, HttpMethod, HttpResponseMessage>? paypal = null,
            Func<string, HttpMethod, HttpResponseMessage>? twilio = null,
            Func<string, HttpMethod, HttpResponseMessage>? retell = null,
            RetellOptions? retellOptions = null,
            TwilioOptions? twilioOptions = null,
            Func<string?>? key = null)
        {
            Store = new Store(Calls);
            var protector = key is null ? Protector : new VoiceCredentialProtector(key);
            Activation = new SubscriptionActivation(
                Store,
                new PayPalBillingVerifier(new HttpClient(new Router("paypal", Calls, paypal ?? PayPalHappy)), new PayPalOptions("https://paypal.test", "client-id", "client-secret"), NullLogger<PayPalBillingVerifier>.Instance),
                new TwilioProvisioner(new HttpClient(new Router("twilio", Calls, twilio ?? TwilioHappy)), twilioOptions ?? new TwilioOptions("ACmaster", "master-token", null), NullLogger<TwilioProvisioner>.Instance),
                new RetellProvisioner(new HttpClient(new Router("retell", Calls, retell ?? RetellHappy)), retellOptions ?? new RetellOptions("key_retell", "gpt-4.1-mini", "https://api.example.test"), NullLogger<RetellProvisioner>.Instance),
                protector,
                NullLogger<SubscriptionActivation>.Instance);
        }

        public IEnumerable<string> Service(string name) => Calls.Where(c => c.StartsWith(name + " ", StringComparison.Ordinal));
    }

    private static bool Closed(string call) => call == "twilio POST /2010-04-01/Accounts/ACsub1.json";

    [Fact]
    public async Task A_paid_signup_is_provisioned_in_order_and_then_activated_with_the_tiers_own_price()
    {
        var world = new World();

        var result = await world.Activation.ActivateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(new SubscriptionActivated(TenantId, VoiceTenantStatuses.Active, "STARTER", 90, "+15615550123"), result.Value);

        var order = world.Calls.Select(c => c.Split(' ')[0]).Distinct().ToList();
        Assert.Equal(["store", "paypal", "twilio", "retell"], order.Take(4));
        Assert.Equal(["store GetTenant", "store SaveConfig", "store Activate"], world.Calls.Where(c => c.StartsWith("store", StringComparison.Ordinal)));
        Assert.True(world.Calls.IndexOf("store SaveConfig") < world.Calls.IndexOf("store Activate"));
        Assert.True(world.Calls.FindLastIndex(c => c.StartsWith("retell", StringComparison.Ordinal)) < world.Calls.IndexOf("store SaveConfig"));
        Assert.Equal(3, world.Service("retell").Count());
        Assert.Equal(7, world.Service("twilio").Count());
        Assert.DoesNotContain(world.Calls, Closed);

        var command = Assert.Single(world.Store.Activations);
        Assert.Equal(("STARTER", 30.00m, 90, 0.50m), (command.TierCode, command.MonthlyPriceUsd, command.IncludedMinutes, command.OverageRateUsd));
        Assert.Equal(("I-SUBSCRIPTION123", "VAULT-TOKEN-456"), (command.PaypalSubscriptionId, command.PaypalVaultTokenId));
        Assert.Equal(("ACsub1", "+15615550123", "PNphone1", "agent_1"), (command.TwilioSubaccountSid, command.AssignedPhoneNumber, command.TwilioPhoneSid, command.RetellAgentId));
        Assert.Equal((null, null, "not_requested", false), (command.A2pBrandSid, command.A2pCampaignSid, command.A2pStatus, command.SmsNotificationsEnabled));
        Assert.NotEqual("sub-token-secret", command.TwilioAuthTokenEncrypted);
        Assert.Equal("sub-token-secret", world.Protector.Unprotect(command.TwilioAuthTokenEncrypted, VoiceSecretPurposes.TwilioAuthToken).Value);
    }

    [Fact]
    public async Task The_starting_settings_are_saved_with_the_script_and_the_disclosure_on()
    {
        var world = new World();

        await world.Activation.ActivateAsync(Request());

        var config = Assert.Single(world.Store.SavedConfigs);
        Assert.True(config.RecordingDisclosureEnabled);
        Assert.Equal("retell-Lily", config.VoiceId);
        Assert.Contains("Boca Cool HVAC", config.GreetingMessage);
        Assert.Contains("a business in Florida that works in HVAC", config.SystemPromptTemplate);
    }

    [Fact]
    public async Task A_signup_that_wants_sms_is_refused_before_anything_is_read_or_created()
    {
        var world = new World();

        var result = await world.Activation.ActivateAsync(Request(sms: true));

        Assert.Equal(ActivationFailureCodes.SmsNotificationsNotAvailable, result.ErrorCode);
        Assert.Empty(world.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_request_missing_a_field_is_refused_before_anything_is_read(int which)
    {
        var world = new World();
        ActivateSubscriptionRequest? request = which switch
        {
            0 => null,
            1 => Request() with { TenantId = Guid.Empty },
            _ => Request() with { EnableSmsNotifications = null },
        };

        var result = await world.Activation.ActivateAsync(request);

        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Empty(world.Calls);
    }

    [Theory]
    [InlineData(VoiceTenantStatuses.Active)]
    [InlineData(VoiceTenantStatuses.Suspended)]
    [InlineData(VoiceTenantStatuses.Cancelled)]
    public async Task A_tenant_that_is_not_waiting_for_payment_is_not_activated_again(string status)
    {
        var world = new World();
        world.Store.Tenant = Result<VoiceTenant>.Success(Tenant(status));

        var result = await world.Activation.ActivateAsync(Request());

        Assert.Equal(ActivationFailureCodes.TenantNotPending, result.ErrorCode);
        Assert.Equal(["store GetTenant"], world.Calls);
    }

    [Fact]
    public async Task An_unknown_tenant_is_not_found_and_nothing_else_is_called()
    {
        var world = new World();
        world.Store.Tenant = Result<VoiceTenant>.Failure("The tenant was not found.", VoiceFailureCodes.NotFound);

        var result = await world.Activation.ActivateAsync(Request());

        Assert.Equal(VoiceFailureCodes.NotFound, result.ErrorCode);
        Assert.Equal(["store GetTenant"], world.Calls);
    }

    [Fact]
    public async Task A_tenant_whose_state_cannot_fill_the_script_is_refused_before_PayPal_is_asked()
    {
        var world = new World();
        world.Store.Tenant = Result<VoiceTenant>.Success(Tenant(state: "ZZ"));

        var result = await world.Activation.ActivateAsync(Request());

        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Equal(["store GetTenant"], world.Calls);
    }

    [Theory]
    [InlineData("retell")]
    [InlineData("twilio")]
    public async Task An_unset_variable_refuses_before_PayPal_Twilio_or_Retell_is_called(string which)
    {
        var world = which == "retell"
            ? new World(retellOptions: new RetellOptions("key", null, "https://api.example.test"))
            : new World(twilioOptions: new TwilioOptions(null, "master-token", null));

        var result = await world.Activation.ActivateAsync(Request());

        Assert.Equal(which == "retell" ? RetellFailureCodes.NotConfigured : TelephonyFailureCodes.TelephonyNotConfigured, result.ErrorCode);
        Assert.Equal(["store GetTenant"], world.Calls);
    }

    [Fact]
    public async Task An_unusable_encryption_key_refuses_before_PayPal_is_asked()
    {
        var world = new World(key: () => null);

        var result = await world.Activation.ActivateAsync(Request());

        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, result.ErrorCode);
        Assert.Equal(["store GetTenant"], world.Calls);
    }

    [Fact]
    public async Task A_subscription_PayPal_does_not_confirm_creates_nothing()
    {
        var world = new World(paypal: (path, method) => path.StartsWith("/v1/billing/subscriptions/", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.NotFound) : PayPalHappy(path, method));

        var result = await world.Activation.ActivateAsync(Request());

        Assert.Equal(BillingFailureCodes.SubscriptionNotFound, result.ErrorCode);
        Assert.Empty(world.Service("twilio"));
        Assert.Empty(world.Service("retell"));
        Assert.Empty(world.Store.SavedConfigs);
    }

    [Fact]
    public async Task A_refused_Twilio_step_stops_before_Retell_and_leaves_the_tenant_waiting()
    {
        var world = new World(twilio: (path, method) => path.EndsWith("/IncomingPhoneNumbers.json", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.BadRequest) : TwilioHappy(path, method));

        var result = await world.Activation.ActivateAsync(Request());

        Assert.Equal(TelephonyFailureCodes.ProvisioningFailed, result.ErrorCode);
        Assert.Contains("buy the phone number", result.Error);
        Assert.Empty(world.Service("retell"));
        Assert.Contains(world.Calls, Closed);
        Assert.Empty(world.Store.SavedConfigs);
        Assert.Empty(world.Store.Activations);
    }

    [Fact]
    public async Task A_refused_Retell_step_closes_the_subaccount_and_saves_and_activates_nothing()
    {
        var world = new World(retell: (path, method) => path.EndsWith("/import-phone-number", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.BadRequest) : RetellHappy(path, method));

        var result = await world.Activation.ActivateAsync(Request());

        Assert.Equal(RetellFailureCodes.ProvisioningFailed, result.ErrorCode);
        Assert.Contains("import the phone number", result.Error);
        Assert.Equal(1, world.Calls.Count(Closed));
        Assert.Empty(world.Store.SavedConfigs);
        Assert.Empty(world.Store.Activations);
    }

    [Fact]
    public async Task A_settings_save_that_fails_undoes_Retell_then_Twilio_and_does_not_activate()
    {
        var world = new World();
        world.Store.Save = () => Result<VoiceReceptionistConfig>.Failure("down", VoiceFailureCodes.StoreUnavailable);

        var result = await world.Activation.ActivateAsync(Request());

        Assert.Equal(VoiceFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.Empty(world.Store.Activations);
        AssertUndoneInOrder(world);
    }

    [Fact]
    public async Task An_activation_the_store_refuses_undoes_Retell_then_Twilio_and_answers_with_the_stores_code()
    {
        var world = new World();
        world.Store.Activate = () => Result<VoiceTenant>.Failure("changed", VoiceFailureCodes.Conflict);

        var result = await world.Activation.ActivateAsync(Request());

        Assert.Equal(VoiceFailureCodes.Conflict, result.ErrorCode);
        Assert.Contains("undone", result.Error);
        Assert.Single(world.Store.Activations);
        AssertUndoneInOrder(world);
    }

    [Fact]
    public async Task A_token_that_cannot_be_encrypted_undoes_everything_before_the_store_is_touched()
    {
        var calls = 0;
        var world = new World(key: () => calls++ == 0 ? KeyBase64 : null);

        var result = await world.Activation.ActivateAsync(Request());

        Assert.Equal(VoiceCredentialProtector.EncryptionUnavailable, result.ErrorCode);
        Assert.Empty(world.Store.SavedConfigs);
        Assert.Empty(world.Store.Activations);
        AssertUndoneInOrder(world);
    }

    private static void AssertUndoneInOrder(World world)
    {
        var deletes = world.Service("retell").Where(c => c.Contains(" DELETE ", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            ["retell DELETE /delete-phone-number/%2B15615550123", "retell DELETE /delete-agent/agent_1", "retell DELETE /delete-retell-llm/llm_1"],
            deletes);
        var lastDelete = world.Calls.FindLastIndex(c => c.Contains(" DELETE ", StringComparison.Ordinal));
        Assert.True(world.Calls.FindIndex(Closed) > lastDelete);
        Assert.Equal(1, world.Calls.Count(Closed));
    }

    [Fact]
    public void Every_failure_code_activation_can_answer_has_a_status_from_its_code()
    {
        Assert.Equal(400, OnboardingController.StatusFor(ActivationFailureCodes.SmsNotificationsNotAvailable));
        Assert.Equal(400, OnboardingController.StatusFor(TelephonyFailureCodes.AreaCodeInvalid));
        Assert.All(
            new[] { BillingFailureCodes.TierUnknown, BillingFailureCodes.SubscriptionNotFound, BillingFailureCodes.SubscriptionNotActive, BillingFailureCodes.SubscriptionUnpaid, BillingFailureCodes.PlanMismatch, BillingFailureCodes.VaultTokenInvalid },
            code => Assert.Equal(400, OnboardingController.StatusFor(code)));
        Assert.Equal(404, OnboardingController.StatusFor(VoiceFailureCodes.NotFound));
        Assert.Equal(409, OnboardingController.StatusFor(ActivationFailureCodes.TenantNotPending));
        Assert.Equal(409, OnboardingController.StatusFor(VoiceFailureCodes.Conflict));
        Assert.Equal(502, OnboardingController.StatusFor(TelephonyFailureCodes.ProvisioningFailed));
        Assert.Equal(502, OnboardingController.StatusFor(RetellFailureCodes.ProvisioningFailed));
        Assert.All(
            new[] { BillingFailureCodes.BillingNotConfigured, BillingFailureCodes.BillingUnavailable, TelephonyFailureCodes.TelephonyNotConfigured, RetellFailureCodes.NotConfigured, VoiceCredentialProtector.EncryptionUnavailable, VoiceFailureCodes.StoreUnavailable },
            code => Assert.Equal(503, OnboardingController.StatusFor(code)));
    }
}
