using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.Services.Voice.Billing;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// End-of-cycle billing: a period is closed once, only the sweep that recorded a charge sends it, it is sent once with the charge's id
/// as PayPal's request id and invoice, every outcome is recorded (a refusal, a 5xx and a charge left without an answer all as failed,
/// never sent again), and the period moves on only once its charge is settled and PayPal has renewed the subscription. A PayPal status
/// other than ACTIVE is recorded and stops billing.
/// </summary>
public sealed class OverageBillingTests
{
    private static readonly Guid Tenant = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Subscription = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ChargeId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset PeriodStart = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextBilling = new(2026, 12, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = PeriodEnd.AddHours(3);

    private static VoiceOverageCharge Charge(string status = VoiceOverageStatuses.Charging, int overage = 10, DateTimeOffset? createdAt = null) => new(
        ChargeId, Tenant, Subscription, "I-SUB", "VAULT-1", PeriodStart, PeriodEnd, 90, 90 + overage, overage, 0.50m, overage * 0.50m,
        status, null, null, null, createdAt ?? Now, status == VoiceOverageStatuses.Charging ? null : Now);

    private sealed class FakeBilling : IVoiceBillingStore
    {
        public List<string> Calls { get; } = new();
        public Result<IReadOnlyList<VoiceSubscriptionDue>> Due { get; set; } =
            Result<IReadOnlyList<VoiceSubscriptionDue>>.Success([new VoiceSubscriptionDue(Tenant, Subscription, PeriodEnd)]);
        public Result<VoiceBillingPeriodClosed> Closed { get; set; } = Result<VoiceBillingPeriodClosed>.Success(new VoiceBillingPeriodClosed(Charge(), Created: true));
        public bool SettleFails { get; set; }
        public ListVoiceSubscriptionsDueCommand? Listed { get; private set; }
        public SettleVoiceOverageChargeCommand? Settled { get; private set; }
        public AdvanceVoiceBillingPeriodCommand? Advanced { get; private set; }
        public SetVoiceBillingStatusCommand? StatusSet { get; private set; }

        public Task<Result<IReadOnlyList<VoiceSubscriptionDue>>> ListDueAsync(ListVoiceSubscriptionsDueCommand command, CancellationToken ct = default)
        {
            Calls.Add("list");
            Listed = command;
            return Task.FromResult(Due);
        }

        public Task<Result<VoiceBillingPeriodClosed>> CloseBillingPeriodAsync(Guid tenantId, CloseVoiceBillingPeriodCommand command, CancellationToken ct = default)
        {
            Calls.Add("close");
            return Task.FromResult(Closed);
        }

        public Task<Result<VoiceOverageCharge>> SettleOverageChargeAsync(Guid tenantId, Guid chargeId, SettleVoiceOverageChargeCommand command, CancellationToken ct = default)
        {
            Calls.Add("settle");
            Settled = command;
            return Task.FromResult(SettleFails
                ? Result<VoiceOverageCharge>.Failure("down", VoiceFailureCodes.StoreUnavailable)
                : Result<VoiceOverageCharge>.Success(Closed.Value!.Charge with
                {
                    Status = command.Status, PaypalOrderId = command.PaypalOrderId, PaypalCaptureId = command.PaypalCaptureId, Failure = command.Failure, SettledAt = Now,
                }));
        }

        public Task<Result<VoiceBillingPeriod>> AdvanceBillingPeriodAsync(Guid tenantId, AdvanceVoiceBillingPeriodCommand command, CancellationToken ct = default)
        {
            Calls.Add("advance");
            Advanced = command;
            return Task.FromResult(Result<VoiceBillingPeriod>.Success(new VoiceBillingPeriod(Subscription, command.FromPeriodEnd, command.NewPeriodEnd)));
        }

        public Task<Result<bool>> SetBillingStatusAsync(Guid tenantId, SetVoiceBillingStatusCommand command, CancellationToken ct = default)
        {
            Calls.Add("status");
            StatusSet = command;
            return Task.FromResult(Result<bool>.Success(true));
        }
    }

    private sealed class PayPalStub : HttpMessageHandler
    {
        public HttpStatusCode OrderStatus { get; init; } = HttpStatusCode.Created;
        public string OrderBody { get; init; } =
            """{"id":"ORDER-1","status":"COMPLETED","purchase_units":[{"payments":{"captures":[{"id":"CAPTURE-1","status":"COMPLETED"}]}}]}""";
        public bool OrderUnreachable { get; init; }
        public string SubscriptionBody { get; init; } = SubscriptionJson("ACTIVE", NextBilling.AddDays(-1).AddHours(-12), NextBilling);
        public List<(string Path, string? RequestId, string? Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((path, request.Headers.TryGetValues("PayPal-Request-Id", out var ids) ? ids.Single() : null, body));
            if (path == "/v1/oauth2/token") return Json("""{"access_token":"A21AAtoken"}""", HttpStatusCode.OK);
            if (path == "/v2/checkout/orders")
            {
                if (OrderUnreachable) throw new HttpRequestException("reset");
                return Json(OrderBody, OrderStatus);
            }

            if (path.StartsWith("/v1/billing/subscriptions/", StringComparison.Ordinal)) return Json(SubscriptionBody, HttpStatusCode.OK);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception)));
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // Renewed: PayPal took the next payment just before this period ended and now bills a month later.
    private static string SubscriptionJson(string status, DateTimeOffset? lastPayment, DateTimeOffset next) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["id"] = "I-SUB",
        ["status"] = status,
        ["billing_info"] = new Dictionary<string, object?>
        {
            ["last_payment"] = lastPayment is null ? null : new { time = lastPayment.Value.ToString("O") },
            ["next_billing_time"] = next.ToString("O"),
        },
    });

    private static (OverageBilling Billing, FakeBilling Store, PayPalStub PayPal, CapturingLogger<OverageBilling> Log) Build(
        FakeBilling? store = null, PayPalStub? paypal = null, PayPalOptions? options = null)
    {
        store ??= new FakeBilling();
        paypal ??= new PayPalStub();
        var log = new CapturingLogger<OverageBilling>();
        var cycle = new PayPalCycleBilling(new HttpClient(paypal), options ?? new PayPalOptions("https://paypal.test", "id", "secret"), NullLogger<PayPalCycleBilling>.Instance);
        return (new OverageBilling(store, cycle, new FakeTimeProvider(Now), log), store, paypal, log);
    }

    [Fact]
    public async Task Without_PayPal_nothing_is_closed_and_the_sweep_says_why()
    {
        var (billing, store, _, log) = Build(options: new PayPalOptions(null, "id", "secret"));
        Assert.Empty(await billing.RunAsync());
        Assert.Empty(store.Calls);
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Error && l.Text.Contains("PAYPAL_BASE_URL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Periods_are_closed_two_hours_after_they_end_so_the_last_calls_are_in()
    {
        var (billing, store, _, _) = Build();
        await billing.RunAsync();
        Assert.Equal(Now - TimeSpan.FromHours(2), store.Listed!.EndedBy);
    }

    [Fact]
    public async Task An_overage_is_charged_once_to_the_saved_payment_method_and_the_period_moves_on()
    {
        var (billing, store, paypal, _) = Build();
        var result = Assert.Single(await billing.RunAsync());

        Assert.Equal(OverageBillingStep.Advanced, result.Step);
        var order = Assert.Single(paypal.Requests, r => r.Path == "/v2/checkout/orders");
        Assert.Equal(ChargeId.ToString(), order.RequestId);
        using var body = JsonDocument.Parse(order.Body!);
        var unit = body.RootElement.GetProperty("purchase_units")[0];
        Assert.Equal("5.00", unit.GetProperty("amount").GetProperty("value").GetString());
        Assert.Equal("USD", unit.GetProperty("amount").GetProperty("currency_code").GetString());
        Assert.Equal(PayPalCycleBilling.InvoiceId(ChargeId), unit.GetProperty("invoice_id").GetString());
        Assert.Equal("VAULT-1", body.RootElement.GetProperty("payment_source").GetProperty("paypal").GetProperty("vault_id").GetString());
        Assert.Equal("MERCHANT", body.RootElement.GetProperty("payment_source").GetProperty("paypal").GetProperty("stored_credential").GetProperty("payment_initiator").GetString());

        Assert.Equal(new SettleVoiceOverageChargeCommand(VoiceOverageStatuses.Charged, "ORDER-1", "CAPTURE-1", null), store.Settled);
        Assert.Equal(new AdvanceVoiceBillingPeriodCommand(Subscription, PeriodEnd, NextBilling), store.Advanced);
        Assert.Equal(["list", "close", "settle", "advance"], store.Calls);
    }

    [Fact]
    public async Task A_period_with_no_overage_charges_nothing_and_moves_on()
    {
        var store = new FakeBilling { Closed = Result<VoiceBillingPeriodClosed>.Success(new VoiceBillingPeriodClosed(Charge(VoiceOverageStatuses.NoOverage, overage: 0), Created: true)) };
        var (billing, _, paypal, _) = Build(store);
        Assert.Equal(OverageBillingStep.Advanced, Assert.Single(await billing.RunAsync()).Step);
        Assert.DoesNotContain(paypal.Requests, r => r.Path == "/v2/checkout/orders");
        Assert.DoesNotContain("settle", store.Calls);
    }

    [Fact]
    public async Task A_declined_charge_is_recorded_as_failed_with_PayPals_reason_logged_and_not_sent_again()
    {
        var paypal = new PayPalStub
        {
            OrderStatus = HttpStatusCode.UnprocessableEntity,
            OrderBody = """{"name":"UNPROCESSABLE_ENTITY","details":[{"issue":"INSTRUMENT_DECLINED"}]}""",
        };
        var (billing, store, _, log) = Build(paypal: paypal);
        Assert.Equal(OverageBillingStep.Advanced, Assert.Single(await billing.RunAsync()).Step);

        Assert.Equal(VoiceOverageStatuses.Failed, store.Settled!.Status);
        Assert.Contains("INSTRUMENT_DECLINED", store.Settled.Failure, StringComparison.Ordinal);
        Assert.Contains("nothing was charged", store.Settled.Failure, StringComparison.Ordinal);
        Assert.Single(paypal.Requests, r => r.Path == "/v2/checkout/orders");
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Error && l.Text.Contains("failed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_charge_PayPal_did_not_answer_is_failed_with_the_invoice_to_check_never_resent(bool unreachable)
    {
        var paypal = new PayPalStub { OrderUnreachable = unreachable, OrderStatus = HttpStatusCode.InternalServerError, OrderBody = "{}" };
        var (billing, store, _, _) = Build(paypal: paypal);
        await billing.RunAsync();

        Assert.Equal(VoiceOverageStatuses.Failed, store.Settled!.Status);
        Assert.Contains("unknown", store.Settled.Failure, StringComparison.Ordinal);
        Assert.Contains(PayPalCycleBilling.InvoiceId(ChargeId), store.Settled.Failure, StringComparison.Ordinal);
        Assert.Single(paypal.Requests, r => r.Path == "/v2/checkout/orders");
    }

    [Fact]
    public async Task An_order_PayPal_did_not_complete_is_not_recorded_as_paid()
    {
        var paypal = new PayPalStub { OrderBody = """{"id":"ORDER-2","status":"PAYER_ACTION_REQUIRED","purchase_units":[]}""" };
        var (billing, store, _, _) = Build(paypal: paypal);
        await billing.RunAsync();
        Assert.Equal(VoiceOverageStatuses.Failed, store.Settled!.Status);
        Assert.Contains("ORDER-2", store.Settled.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_charge_another_sweep_recorded_moments_ago_is_left_to_it()
    {
        var store = new FakeBilling { Closed = Result<VoiceBillingPeriodClosed>.Success(new VoiceBillingPeriodClosed(Charge(createdAt: Now.AddMinutes(-2)), Created: false)) };
        var (billing, _, paypal, _) = Build(store);
        Assert.Equal(OverageBillingStep.ChargeInProgress, Assert.Single(await billing.RunAsync()).Step);
        Assert.Empty(paypal.Requests);
        Assert.Equal(["list", "close"], store.Calls);
    }

    [Fact]
    public async Task A_charge_left_without_an_outcome_is_failed_with_the_invoice_to_check_and_not_sent()
    {
        var store = new FakeBilling { Closed = Result<VoiceBillingPeriodClosed>.Success(new VoiceBillingPeriodClosed(Charge(createdAt: Now.AddMinutes(-20)), Created: false)) };
        var (billing, _, paypal, _) = Build(store);
        await billing.RunAsync();

        Assert.DoesNotContain(paypal.Requests, r => r.Path == "/v2/checkout/orders");
        Assert.Equal(VoiceOverageStatuses.Failed, store.Settled!.Status);
        Assert.Contains(PayPalCycleBilling.InvoiceId(ChargeId), store.Settled.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_charge_whose_outcome_cannot_be_recorded_is_logged_critical_with_the_order()
    {
        var store = new FakeBilling { SettleFails = true };
        var (billing, _, _, log) = Build(store);
        Assert.Equal(OverageBillingStep.StoreFailed, Assert.Single(await billing.RunAsync()).Step);
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Critical && l.Text.Contains("ORDER-1", StringComparison.Ordinal));
        Assert.DoesNotContain("advance", store.Calls);
    }

    [Fact]
    public async Task A_subscription_PayPal_has_not_renewed_waits_where_it_is()
    {
        // The last payment is the one that opened this period, so the next month has not been paid.
        var paypal = new PayPalStub { SubscriptionBody = SubscriptionJson("ACTIVE", PeriodStart, PeriodEnd) };
        var (billing, store, _, _) = Build(paypal: paypal);
        Assert.Equal(OverageBillingStep.AwaitingRenewal, Assert.Single(await billing.RunAsync()).Step);
        Assert.Null(store.Advanced);
    }

    [Theory]
    [InlineData("SUSPENDED")]
    [InlineData("CANCELLED")]
    public async Task A_subscription_PayPal_no_longer_holds_active_is_recorded_and_not_moved_on(string status)
    {
        var paypal = new PayPalStub { SubscriptionBody = SubscriptionJson(status, PeriodStart, NextBilling) };
        var (billing, store, _, _) = Build(paypal: paypal);
        Assert.Equal(OverageBillingStep.Stopped, Assert.Single(await billing.RunAsync()).Step);
        Assert.Equal(new SetVoiceBillingStatusCommand(Subscription, status), store.StatusSet);
        Assert.Null(store.Advanced);
    }

    [Fact]
    public async Task A_due_list_that_cannot_be_read_closes_nothing_and_is_logged()
    {
        var store = new FakeBilling { Due = Result<IReadOnlyList<VoiceSubscriptionDue>>.Failure("down", VoiceFailureCodes.StoreUnavailable) };
        var (billing, _, _, log) = Build(store);
        Assert.Empty(await billing.RunAsync());
        Assert.Equal(["list"], store.Calls);
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Error);
    }
}
