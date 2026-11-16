using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.Services.Voice.Billing;
using GeekApplication.Models.Voice;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The payment check before anything is provisioned: the tier schedule is exact, an ACTIVE and already paid subscription on a plan
/// that charges the tier's price is the only thing that passes, a PayPal 404 is the visitor's mistake, and everything else PayPal can
/// do wrong is billing unavailable. Each call is made once and no log line carries an id, a secret or a body.
/// </summary>
public sealed class PayPalBillingVerifierTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Subscription = "I-SUBSCRIPTION123";
    private const string Vault = "VAULT-TOKEN-456";
    private const string Secret = "client-secret-value";
    private static readonly DateTimeOffset Paid = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextBilling = new(2026, 12, 14, 15, 0, 0, TimeSpan.Zero);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Authorization)> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString()));
            return Task.FromResult(respond(request));
        }
    }

    private sealed class CapturingLogger : ILogger<PayPalBillingVerifier>
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string SubscriptionJson(string status = "ACTIVE", bool paid = true, string? customId = "22222222-2222-2222-2222-222222222222") => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["id"] = Subscription,
        ["custom_id"] = customId,
        ["status"] = status,
        ["plan_id"] = "P-PLAN",
        ["billing_info"] = paid
            ? new Dictionary<string, object?>
            {
                ["last_payment"] = new Dictionary<string, object?> { ["amount"] = new { currency_code = "USD", value = "30.00" }, ["time"] = Paid.ToString("O") },
                ["next_billing_time"] = NextBilling.ToString("O"),
            }
            : new Dictionary<string, object?> { ["next_billing_time"] = NextBilling.ToString("O") },
    });

    private static string PlanJson(string price = "30.00", string currency = "USD", string unit = "MONTH", int count = 1, bool trial = false)
    {
        var cycles = new List<object>();
        if (trial) cycles.Add(new { tenure_type = "TRIAL", sequence = 1, frequency = new { interval_unit = "MONTH", interval_count = 1 }, pricing_scheme = new { fixed_price = new { currency_code = "USD", value = "0" } } });
        cycles.Add(new { tenure_type = "REGULAR", sequence = trial ? 2 : 1, frequency = new { interval_unit = unit, interval_count = count }, pricing_scheme = new { fixed_price = new { currency_code = currency, value = price } } });
        return JsonSerializer.Serialize(new { id = "P-PLAN", status = "ACTIVE", billing_cycles = cycles });
    }

    private static string VaultJson() => """{"id":"VAULT-TOKEN-456","customer":{"id":"cust"},"payment_source":{"paypal":{"payer_id":"PAYER","email_address":"owner@bocacool.example"}}}""";

    private sealed class Scenario
    {
        public string Subscription { get; init; } = SubscriptionJson();
        public string Plan { get; init; } = PlanJson();
        public string Vault { get; init; } = VaultJson();
        public HttpStatusCode TokenStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode SubscriptionStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode PlanStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode VaultStatus { get; init; } = HttpStatusCode.OK;
    }

    private static (PayPalBillingVerifier Verifier, StubHandler Handler, CapturingLogger Log) Build(Scenario? scenario = null, PayPalOptions? options = null)
    {
        scenario ??= new Scenario();
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/oauth2/token") return Json("""{"access_token":"A21AAtoken","token_type":"Bearer"}""", scenario.TokenStatus);
            if (path.StartsWith("/v1/billing/subscriptions/", StringComparison.Ordinal)) return Json(scenario.Subscription, scenario.SubscriptionStatus);
            if (path.StartsWith("/v1/billing/plans/", StringComparison.Ordinal)) return Json(scenario.Plan, scenario.PlanStatus);
            if (path.StartsWith("/v3/vault/payment-tokens/", StringComparison.Ordinal)) return Json(scenario.Vault, scenario.VaultStatus);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var log = new CapturingLogger();
        var verifier = new PayPalBillingVerifier(
            new HttpClient(handler),
            options ?? new PayPalOptions("https://paypal.test", "client-id", Secret),
            log);
        return (verifier, handler, log);
    }

    [Fact]
    public void The_schedule_is_the_plans_four_tiers_exactly()
    {
        Assert.Equal(
            [("STARTER", 30.00m, 90, 0.50m), ("STANDARD", 48.00m, 250, 0.50m), ("PRO", 148.00m, 1000, 0.50m), ("PREMIUM", 298.00m, 2000, 0.50m)],
            VoiceTierSchedule.All.Select(t => (t.Code, t.MonthlyPriceUsd, t.IncludedMinutes, t.OverageRateUsd)));
        Assert.All(VoiceTierSchedule.All, t => Assert.Contains(t.Code, VoiceSubscriptionTiers.All));
    }

    [Theory]
    [InlineData("starter")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("GOLD")]
    public async Task A_tier_that_is_not_in_the_schedule_is_refused_before_PayPal_is_asked(string? tier)
    {
        var (verifier, handler, _) = Build();

        var result = await verifier.VerifyAsync(Tenant, tier, Subscription, Vault);

        Assert.Equal(BillingFailureCodes.TierUnknown, result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_active_paid_subscription_on_the_tiers_plan_with_a_paypal_vault_token_passes()
    {
        var (verifier, handler, _) = Build();

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.True(result.IsSuccess, result.Error);
        var billing = result.Value!;
        Assert.Equal("STARTER", billing.Tier.Code);
        Assert.Equal(30.00m, billing.Tier.MonthlyPriceUsd);
        Assert.Equal(90, billing.Tier.IncludedMinutes);
        Assert.Equal(Subscription, billing.SubscriptionId);
        Assert.Equal(Vault, billing.VaultTokenId);
        Assert.Equal(Paid, billing.CurrentPeriodStart);
        Assert.Equal(NextBilling, billing.CurrentPeriodEnd);
        Assert.Equal("ACTIVE", billing.BillingStatus);

        Assert.Equal(
            [
                (HttpMethod.Post, "/v1/oauth2/token"),
                (HttpMethod.Get, $"/v1/billing/subscriptions/{Subscription}"),
                (HttpMethod.Get, "/v1/billing/plans/P-PLAN"),
                (HttpMethod.Get, $"/v3/vault/payment-tokens/{Vault}"),
            ],
            handler.Requests.Select(r => (r.Method, r.Path)));
        Assert.StartsWith("Basic ", handler.Requests[0].Authorization, StringComparison.Ordinal);
        Assert.All(handler.Requests.Skip(1), r => Assert.Equal("Bearer A21AAtoken", r.Authorization));
    }

    [Theory]
    [InlineData("STARTER", "30.00")]
    [InlineData("STANDARD", "48.00")]
    [InlineData("PRO", "148.00")]
    [InlineData("PREMIUM", "298.00")]
    public async Task Each_tier_passes_on_a_plan_that_charges_its_own_price(string tier, string price)
    {
        var (verifier, _, _) = Build(new Scenario { Plan = PlanJson(price) });

        var result = await verifier.VerifyAsync(Tenant, tier, Subscription, Vault);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(VoiceTierSchedule.Find(tier)!.IncludedMinutes, result.Value!.Tier.IncludedMinutes);
    }

    [Theory]
    [InlineData("STANDARD", "30.00")]
    [InlineData("STARTER", "48.00")]
    [InlineData("PREMIUM", "29.80")]
    public async Task A_plan_that_charges_another_price_is_a_mismatch(string tier, string price)
    {
        var (verifier, _, _) = Build(new Scenario { Plan = PlanJson(price) });

        var result = await verifier.VerifyAsync(Tenant, tier, Subscription, Vault);

        Assert.Equal(BillingFailureCodes.PlanMismatch, result.ErrorCode);
    }

    [Theory]
    [InlineData("EUR", "MONTH", 1, false)]
    [InlineData("USD", "YEAR", 1, false)]
    [InlineData("USD", "MONTH", 3, false)]
    [InlineData("USD", "MONTH", 1, true)]
    public async Task A_plan_in_another_currency_period_or_with_a_free_trial_is_a_mismatch(string currency, string unit, int count, bool trial)
    {
        var (verifier, _, _) = Build(new Scenario { Plan = PlanJson(currency: currency, unit: unit, count: count, trial: trial) });

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.Equal(BillingFailureCodes.PlanMismatch, result.ErrorCode);
    }

    [Theory]
    [InlineData("33333333-3333-3333-3333-333333333333")]
    [InlineData(null)]
    public async Task A_subscription_started_for_another_tenant_or_by_hand_is_refused_before_the_plan_is_asked_for(string? customId)
    {
        var (verifier, handler, _) = Build(new Scenario { Subscription = SubscriptionJson(customId: customId) });

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.Equal(BillingFailureCodes.SubscriptionNotFound, result.ErrorCode);
        Assert.DoesNotContain(handler.Requests, r => r.Path.StartsWith("/v1/billing/plans/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("APPROVAL_PENDING")]
    [InlineData("APPROVED")]
    [InlineData("SUSPENDED")]
    [InlineData("CANCELLED")]
    [InlineData("EXPIRED")]
    [InlineData("active")]
    public async Task A_subscription_that_is_not_ACTIVE_is_refused_and_the_plan_is_never_asked_for(string status)
    {
        var (verifier, handler, _) = Build(new Scenario { Subscription = SubscriptionJson(status) });

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.Equal(BillingFailureCodes.SubscriptionNotActive, result.ErrorCode);
        Assert.DoesNotContain(handler.Requests, r => r.Path.StartsWith("/v1/billing/plans/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_active_subscription_with_no_payment_yet_is_unpaid()
    {
        var (verifier, _, _) = Build(new Scenario { Subscription = SubscriptionJson(paid: false) });

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.Equal(BillingFailureCodes.SubscriptionUnpaid, result.ErrorCode);
    }

    [Fact]
    public async Task A_subscription_PayPal_does_not_know_is_the_visitors_mistake()
    {
        var (verifier, _, _) = Build(new Scenario { SubscriptionStatus = HttpStatusCode.NotFound });

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.Equal(BillingFailureCodes.SubscriptionNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task A_vault_token_PayPal_does_not_know_is_invalid()
    {
        var (verifier, _, _) = Build(new Scenario { VaultStatus = HttpStatusCode.NotFound });

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.Equal(BillingFailureCodes.VaultTokenInvalid, result.ErrorCode);
    }

    [Theory]
    [InlineData("""{"id":"VAULT-TOKEN-456","payment_source":{"card":{"last_digits":"1111"}}}""")]
    [InlineData("""{"id":"VAULT-TOKEN-456"}""")]
    public async Task A_vault_token_that_is_not_a_paypal_account_is_invalid(string vault)
    {
        var (verifier, _, _) = Build(new Scenario { Vault = vault });

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.Equal(BillingFailureCodes.VaultTokenInvalid, result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_token_request_PayPal_refuses_is_billing_unavailable_and_nothing_else_is_asked(HttpStatusCode status)
    {
        var (verifier, handler, log) = Build(new Scenario { TokenStatus = status });

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.Equal(BillingFailureCodes.BillingUnavailable, result.ErrorCode);
        Assert.Single(handler.Requests);
        Assert.NotEmpty(log.Lines);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_subscription_read_that_fails_for_any_reason_but_404_is_billing_unavailable(HttpStatusCode status)
    {
        var (verifier, _, _) = Build(new Scenario { SubscriptionStatus = status });

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.Equal(BillingFailureCodes.BillingUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task A_PayPal_that_cannot_be_reached_is_billing_unavailable_and_is_logged()
    {
        var log = new CapturingLogger();
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var verifier = new PayPalBillingVerifier(new HttpClient(handler), new PayPalOptions("https://paypal.test", "id", Secret), log);

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.Equal(BillingFailureCodes.BillingUnavailable, result.ErrorCode);
        Assert.NotEmpty(log.Lines);
    }

    [Fact]
    public async Task A_subscription_body_that_is_not_json_is_billing_unavailable()
    {
        var (verifier, _, _) = Build(new Scenario { Subscription = "<html>" });

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.Equal(BillingFailureCodes.BillingUnavailable, result.ErrorCode);
    }

    [Theory]
    [InlineData(null, "id", "secret", "PAYPAL_BASE_URL")]
    [InlineData("https://paypal.test", "", "secret", "PAYPAL_CLIENT_ID")]
    [InlineData("https://paypal.test", "id", null, "PAYPAL_CLIENT_SECRET")]
    public async Task A_missing_variable_is_billing_not_configured_and_names_itself_in_the_log(string? baseUrl, string? id, string? secret, string name)
    {
        var (verifier, handler, log) = Build(options: new PayPalOptions(baseUrl, id, secret));

        var result = await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.Equal(BillingFailureCodes.BillingNotConfigured, result.ErrorCode);
        Assert.Empty(handler.Requests);
        Assert.Contains(log.Lines, line => line.Contains(name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_log_line_carries_an_id_a_secret_or_a_body()
    {
        var (verifier, _, log) = Build(new Scenario { SubscriptionStatus = HttpStatusCode.ServiceUnavailable });

        await verifier.VerifyAsync(Tenant, "STARTER", Subscription, Vault);

        Assert.NotEmpty(log.Lines);
        Assert.All(log.Lines, line =>
        {
            Assert.DoesNotContain(Subscription, line, StringComparison.Ordinal);
            Assert.DoesNotContain(Vault, line, StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, line, StringComparison.Ordinal);
            Assert.DoesNotContain("A21AAtoken", line, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_options_read_the_three_variables_and_report_which_are_missing()
    {
        var variables = new Dictionary<string, string?> { ["PAYPAL_BASE_URL"] = "https://api-m.sandbox.paypal.com/", ["PAYPAL_CLIENT_ID"] = " id " };

        var options = PayPalOptions.From(name => variables.GetValueOrDefault(name));

        Assert.Equal("https://api-m.sandbox.paypal.com", options.BaseUrl);
        Assert.Equal("id", options.ClientId);
        Assert.Equal(["PAYPAL_CLIENT_SECRET"], options.Missing());
    }
}
