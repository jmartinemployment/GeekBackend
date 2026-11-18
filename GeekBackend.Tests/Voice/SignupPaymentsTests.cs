using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The PayPal half of /signup: a subscription to the tier's plan tagged with the tenant, and saving the visitor's PayPal account, each
/// only for a tenant still waiting for payment, each sent to PayPal once, and the visitor sent back only to an https address.
/// </summary>
public sealed class SignupPaymentsTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Back = "https://receptionist.example/signup/paypal";
    private const string Cancel = "https://receptionist.example/signup";

    private sealed class PayPal : HttpMessageHandler
    {
        public HttpStatusCode PaymentTokenStatus { get; init; } = HttpStatusCode.OK;
        public string? SubscriptionBody { get; init; }
        public List<(string Path, string? RequestId, string? Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((path, request.Headers.TryGetValues("PayPal-Request-Id", out var ids) ? ids.Single() : null, body));
            return path switch
            {
                "/v1/oauth2/token" => Json("""{"access_token":"A21AAtoken"}""", HttpStatusCode.OK),
                "/v1/billing/subscriptions" => Json(SubscriptionBody ??
                    """{"id":"I-NEW","status":"APPROVAL_PENDING","links":[{"rel":"approve","href":"https://www.sandbox.paypal.com/webapps/billing/subscriptions?ba_token=BA-1"},{"rel":"self","href":"https://x"}]}""",
                    HttpStatusCode.Created),
                "/v3/vault/setup-tokens" => Json("""{"id":"SETUP1","status":"PAYER_ACTION_REQUIRED","links":[{"rel":"approve","href":"https://www.sandbox.paypal.com/agreements/approve?approval_session_id=S1"}]}""", HttpStatusCode.OK),
                "/v3/vault/payment-tokens" => Json(PaymentTokenStatus == HttpStatusCode.OK ? """{"id":"PAYTOKEN1"}""" : """{"name":"UNPROCESSABLE_ENTITY"}""", PaymentTokenStatus),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }
    }

    private sealed class Store(string status) : IVoiceStore
    {
        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(tenantId == Tenant
                ? Result<VoiceTenant>.Success(new VoiceTenant(Tenant, "Plumbing", "Plumbing, heating, & air-conditioning contractors", false, null, null, DateTimeOffset.UnixEpoch, "US", "FL", "o@example.test", "+15615550100", status, DateTimeOffset.UnixEpoch))
                : Result<VoiceTenant>.Failure("No such tenant.", VoiceFailureCodes.NotFound));

        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceKnowledgeDocumentCreated>> CreateKnowledgeDocumentAsync(Guid tenantId, CreateVoiceKnowledgeDocumentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VoiceKnowledgeHit>>> SearchKnowledgeAsync(Guid tenantId, SearchVoiceKnowledgeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (SignupPayments Payments, PayPal Http) Build(string status = VoiceTenantStatuses.PendingPayment, PayPal? http = null, PayPalPlans? plans = null)
    {
        http ??= new PayPal();
        plans ??= PayPalPlans.From(name => name == "PAYPAL_PLAN_STARTER" ? "P-STARTER" : name.StartsWith("PAYPAL_PLAN_", StringComparison.Ordinal) ? "P-OTHER" : null);
        var signup = new PayPalSignup(new HttpClient(http), new PayPalOptions("https://paypal.test", "id", "secret"), plans, NullLogger<PayPalSignup>.Instance);
        return (new SignupPayments(new Store(status), signup), http);
    }

    [Fact]
    public async Task A_subscription_is_started_on_the_tiers_plan_tagged_with_the_tenant()
    {
        var (payments, http) = Build();
        var result = await payments.StartSubscriptionAsync(new StartSignupPaymentRequest(Tenant, "STARTER", Back, Cancel));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("I-NEW", result.Value!.Id);
        Assert.StartsWith("https://www.sandbox.paypal.com/", result.Value.ApprovalUrl, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(Assert.Single(http.Requests, r => r.Path == "/v1/billing/subscriptions").Body!);
        Assert.Equal("P-STARTER", body.RootElement.GetProperty("plan_id").GetString());
        Assert.Equal(Tenant.ToString(), body.RootElement.GetProperty("custom_id").GetString());
        Assert.Equal(Back, body.RootElement.GetProperty("application_context").GetProperty("return_url").GetString());
    }

    [Fact]
    public async Task A_tier_with_no_plan_set_is_refused_without_calling_PayPal()
    {
        var (payments, http) = Build(plans: PayPalPlans.From(_ => null));
        var result = await payments.StartSubscriptionAsync(new StartSignupPaymentRequest(Tenant, "PRO", Back, Cancel));
        Assert.Equal(BillingFailureCodes.BillingNotConfigured, result.ErrorCode);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("starter")]
    [InlineData("UNLIMITED")]
    [InlineData(null)]
    public async Task An_unknown_tier_is_refused(string? tier) =>
        Assert.Equal(BillingFailureCodes.TierUnknown, (await Build().Payments.StartSubscriptionAsync(new StartSignupPaymentRequest(Tenant, tier, Back, Cancel))).ErrorCode);

    [Theory]
    [InlineData(VoiceTenantStatuses.Active)]
    [InlineData(VoiceTenantStatuses.Suspended)]
    [InlineData(VoiceTenantStatuses.Cancelled)]
    public async Task Nothing_is_started_for_a_tenant_that_is_not_waiting_for_payment(string status)
    {
        var (payments, http) = Build(status);
        Assert.Equal(ActivationFailureCodes.TenantNotPending, (await payments.StartSubscriptionAsync(new StartSignupPaymentRequest(Tenant, "STARTER", Back, Cancel))).ErrorCode);
        Assert.Equal(ActivationFailureCodes.TenantNotPending, (await payments.StartVaultAsync(new StartSignupPaymentRequest(Tenant, null, Back, Cancel))).ErrorCode);
        Assert.Equal(ActivationFailureCodes.TenantNotPending, (await payments.CompleteVaultAsync(new CompleteSignupVaultRequest(Tenant, "SETUP1"))).ErrorCode);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task An_unknown_tenant_is_not_found() =>
        Assert.Equal(VoiceFailureCodes.NotFound, (await Build().Payments.StartVaultAsync(new StartSignupPaymentRequest(Guid.NewGuid(), null, Back, Cancel))).ErrorCode);

    [Theory]
    [InlineData("http://receptionist.example/signup/paypal")]
    [InlineData("/signup/paypal")]
    [InlineData(null)]
    public async Task PayPal_sends_the_visitor_back_only_to_https(string? back)
    {
        var (payments, http) = Build();
        Assert.Equal(VoiceFailureCodes.InvalidRequest, (await payments.StartVaultAsync(new StartSignupPaymentRequest(Tenant, null, back, Cancel))).ErrorCode);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Saving_the_payment_method_asks_PayPal_for_a_merchant_setup_token_then_turns_it_into_a_payment_token_once()
    {
        var (payments, http) = Build();
        var started = await payments.StartVaultAsync(new StartSignupPaymentRequest(Tenant, null, Back, Cancel));
        Assert.True(started.IsSuccess, started.Error);
        Assert.Equal("SETUP1", started.Value!.Id);
        using (var body = JsonDocument.Parse(Assert.Single(http.Requests, r => r.Path == "/v3/vault/setup-tokens").Body!))
            Assert.Equal("MERCHANT", body.RootElement.GetProperty("payment_source").GetProperty("paypal").GetProperty("usage_type").GetString());

        var saved = await payments.CompleteVaultAsync(new CompleteSignupVaultRequest(Tenant, "SETUP1"));
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal("PAYTOKEN1", saved.Value!.PaypalVaultTokenId);
        Assert.Equal("vault-SETUP1", Assert.Single(http.Requests, r => r.Path == "/v3/vault/payment-tokens").RequestId);
    }

    [Fact]
    public async Task A_setup_token_PayPal_refuses_is_the_visitors_to_approve_again()
    {
        var (payments, _) = Build(http: new PayPal { PaymentTokenStatus = HttpStatusCode.UnprocessableEntity });
        Assert.Equal(BillingFailureCodes.VaultTokenInvalid, (await payments.CompleteVaultAsync(new CompleteSignupVaultRequest(Tenant, "SETUP1"))).ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SETUP-1")]
    [InlineData(null)]
    public async Task A_setup_token_that_is_not_PayPals_shape_is_refused_before_anything_is_asked(string? setupToken)
    {
        var (payments, http) = Build();
        Assert.Equal(VoiceFailureCodes.InvalidRequest, (await payments.CompleteVaultAsync(new CompleteSignupVaultRequest(Tenant, setupToken))).ErrorCode);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task An_answer_with_no_approval_link_is_billing_unavailable()
    {
        var (payments, _) = Build(http: new PayPal { SubscriptionBody = """{"id":"I-NEW","links":[]}""" });
        Assert.Equal(BillingFailureCodes.BillingUnavailable, (await payments.StartSubscriptionAsync(new StartSignupPaymentRequest(Tenant, "STARTER", Back, Cancel))).ErrorCode);
    }
}
