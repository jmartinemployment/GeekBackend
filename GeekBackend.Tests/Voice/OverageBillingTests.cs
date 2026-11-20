using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GeekAPI.Controllers.Voice;
using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// End-of-cycle billing and a tenant's standing. A period is closed once, only the sweep that recorded a charge sends it, once, and
/// every outcome is recorded; a failed charge or a PayPal-suspended subscription suspends the tenant; its number keeps answering seven
/// days and then the record says it stopped, which the tenant's call route reads; a tenant with nothing owed and an ACTIVE subscription
/// is restored; a subscription PayPal cancels cancels the tenant and closes its Twilio subaccount. No call leaves for anything but
/// PayPal and Twilio. The owner pays an unpaid overage through a
/// PayPal order that is accepted only for that charge's exact amount.
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
    private const string Number = "+15615550100";

    private static VoiceOverageCharge Charge(string status = VoiceOverageStatuses.Charging, int overage = 10, DateTimeOffset? createdAt = null) => new(
        ChargeId, Tenant, Subscription, "I-SUB", "VAULT-1", PeriodStart, PeriodEnd, 90, 90 + overage, overage, 0.50m, overage * 0.50m,
        status, null, null, status == VoiceOverageStatuses.Failed ? "declined" : null, createdAt ?? Now, status == VoiceOverageStatuses.Charging ? null : Now);

    private static VoiceBillingStanding Standing(
        string status = VoiceTenantStatuses.Active, DateTimeOffset? suspendedAt = null, DateTimeOffset? stoppedAt = null,
        string billing = VoiceBillingStatuses.Active, params VoiceOverageCharge[] unpaid) =>
        new(Tenant, status, suspendedAt, stoppedAt, Subscription, VoiceSubscriptionTiers.Starter, billing, "I-SUB", PeriodStart, PeriodEnd, unpaid);

    // ----- Fakes -----

    private sealed class FakeBilling : IVoiceBillingStore
    {
        public List<string> Calls { get; } = new();
        public Result<IReadOnlyList<VoiceSubscriptionDue>> Due { get; set; } =
            Result<IReadOnlyList<VoiceSubscriptionDue>>.Success([new VoiceSubscriptionDue(Tenant, Subscription, PeriodEnd)]);
        public IReadOnlyList<VoiceSuspendedTenant> Suspended { get; set; } = [];
        public Result<VoiceBillingPeriodClosed> Closed { get; set; } = Result<VoiceBillingPeriodClosed>.Success(new VoiceBillingPeriodClosed(Charge(), Created: true));
        public VoiceBillingStanding Current { get; set; } = Standing();
        public bool SettleFails { get; set; }
        public bool CollectFails { get; set; }
        public ListVoiceSubscriptionsDueCommand? Listed { get; private set; }
        public SettleVoiceOverageChargeCommand? Settled { get; private set; }
        public AdvanceVoiceBillingPeriodCommand? Advanced { get; private set; }
        public List<SetVoiceBillingStatusCommand> StatusesSet { get; } = new();
        public CollectVoiceOverageChargeCommand? Collected { get; private set; }

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
            StatusesSet.Add(command);
            Current = Current with { BillingStatus = command.BillingStatus };
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<IReadOnlyList<VoiceSuspendedTenant>>> ListSuspendedAsync(CancellationToken ct = default)
        {
            Calls.Add("list-suspended");
            return Task.FromResult(Result<IReadOnlyList<VoiceSuspendedTenant>>.Success(Suspended));
        }

        public Task<Result<VoiceBillingStanding>> GetStandingAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Result<VoiceBillingStanding>.Success(Current));

        public Task<Result<VoiceBillingStanding>> SuspendTenantAsync(Guid tenantId, CancellationToken ct = default)
        {
            Calls.Add("suspend");
            if (Current.TenantStatus == VoiceTenantStatuses.Active) Current = Current with { TenantStatus = VoiceTenantStatuses.Suspended, SuspendedAt = Now };
            return Task.FromResult(Result<VoiceBillingStanding>.Success(Current));
        }

        public Task<Result<VoiceBillingStanding>> RestoreTenantAsync(Guid tenantId, CancellationToken ct = default)
        {
            Calls.Add("restore");
            Current = Current with { TenantStatus = VoiceTenantStatuses.Active, SuspendedAt = null, AnsweringStoppedAt = null };
            return Task.FromResult(Result<VoiceBillingStanding>.Success(Current));
        }

        public Task<Result<VoiceBillingStanding>> MarkAnsweringStoppedAsync(Guid tenantId, CancellationToken ct = default)
        {
            Calls.Add("answering-stopped");
            Current = Current with { AnsweringStoppedAt = Now };
            return Task.FromResult(Result<VoiceBillingStanding>.Success(Current));
        }

        public Task<Result<VoiceBillingStanding>> CancelTenantAsync(Guid tenantId, CancellationToken ct = default)
        {
            Calls.Add("cancel");
            Current = Current with { TenantStatus = VoiceTenantStatuses.Cancelled, SuspendedAt = null };
            return Task.FromResult(Result<VoiceBillingStanding>.Success(Current));
        }

        public Task<Result<bool>> MarkTelephonyReleasedAsync(Guid tenantId, CancellationToken ct = default)
        {
            Calls.Add("released");
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<VoiceOverageCharge>> CollectOverageChargeAsync(Guid tenantId, Guid chargeId, CollectVoiceOverageChargeCommand command, CancellationToken ct = default)
        {
            Calls.Add("collect");
            Collected = command;
            if (CollectFails) return Task.FromResult(Result<VoiceOverageCharge>.Failure("down", VoiceFailureCodes.StoreUnavailable));
            Current = Current with { UnpaidCharges = Current.UnpaidCharges.Where(c => c.Id != chargeId).ToList() };
            return Task.FromResult(Result<VoiceOverageCharge>.Success(Charge(VoiceOverageStatuses.Failed) with { CollectedOrderId = command.PaypalOrderId }));
        }
    }

    private sealed class FakeVoice : IVoiceStore
    {
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Result<VoiceTelephony>.Success(new VoiceTelephony(tenantId, "AC-SUB", "enc", Number, "PN1", null, null, "pending", false)));

        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        // Bring-your-own-storage is not what this test is about; a fake that is asked says there is none.
        public Task<Result<VoiceStorageConnection>> GetStorageConnectionAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Result<VoiceStorageConnection>.Failure("No storage connection.", VoiceFailureCodes.NotFound));

        public Task<Result<VoiceStorageConnection>> SaveStorageConnectionAsync(Guid tenantId, SaveVoiceStorageConnectionCommand command, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceConnectionRemoved>> DisconnectMcpAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceConnectionRemoved>> DisconnectStorageAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>PayPal and Twilio, told apart by host.</summary>
    private sealed class Services : HttpMessageHandler
    {
        public HttpStatusCode OrderStatus { get; init; } = HttpStatusCode.Created;
        public string OrderBody { get; init; } =
            """{"id":"ORDER-1","status":"COMPLETED","purchase_units":[{"payments":{"captures":[{"id":"CAPTURE-1","status":"COMPLETED"}]}}]}""";
        public bool OrderUnreachable { get; init; }
        public string SubscriptionBody { get; set; } = SubscriptionJson("ACTIVE", NextBilling.AddDays(-1).AddHours(-12), NextBilling);
        public HttpStatusCode CaptureStatus { get; init; } = HttpStatusCode.Created;
        public string CaptureBody { get; init; } = Captured("5.00");
        public string OrderReadBody { get; init; } = Captured("5.00");
        public List<(HttpMethod Method, string Host, string Path, string? RequestId, string? Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, uri.Host, uri.AbsolutePath, request.Headers.TryGetValues("PayPal-Request-Id", out var ids) ? ids.Single() : null, body));

            if (uri.Host == "api.twilio.com") return Json("""{"sid":"AC-SUB","status":"closed"}""", HttpStatusCode.OK);

            var path = uri.AbsolutePath;
            if (path == "/v1/oauth2/token") return Json("""{"access_token":"A21AAtoken"}""", HttpStatusCode.OK);
            if (path == "/v2/checkout/orders")
            {
                if (OrderUnreachable) throw new HttpRequestException("reset");
                return Json(OrderBody, OrderStatus);
            }

            if (path.EndsWith("/capture", StringComparison.Ordinal)) return Json(CaptureBody, CaptureStatus);
            if (path.StartsWith("/v2/checkout/orders/", StringComparison.Ordinal)) return Json(OrderReadBody, HttpStatusCode.OK);
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

    private static string Captured(string amount, string? customId = null) => JsonSerializer.Serialize(new
    {
        id = "ORDER-9",
        status = "COMPLETED",
        purchase_units = new[]
        {
            new
            {
                custom_id = customId ?? ChargeId.ToString(),
                payments = new { captures = new[] { new { id = "CAPTURE-9", status = "COMPLETED", amount = new { currency_code = "USD", value = amount } } } },
            },
        },
    });

    private sealed record Rig(OverageBilling Billing, OverageCollection Collection, FakeBilling Store, Services Http, CapturingLogger<OverageBilling> Log, CapturingLogger<OverageCollection> CollectionLog);

    private static Rig Build(FakeBilling? store = null, Services? http = null, PayPalOptions? options = null, DateTimeOffset? now = null)
    {
        store ??= new FakeBilling();
        http ??= new Services();
        var client = new HttpClient(http, disposeHandler: false);
        var paypal = new PayPalCycleBilling(client, options ?? new PayPalOptions("https://paypal.test", "id", "secret"), NullLogger<PayPalCycleBilling>.Instance);
        var standing = new TenantStanding(
            store,
            new FakeVoice(),
            new TwilioProvisioner(client, new TwilioOptions("AC-MASTER", "master-token", null), NullLogger<TwilioProvisioner>.Instance),
            NullLogger<TenantStanding>.Instance);
        var log = new CapturingLogger<OverageBilling>();
        var collectionLog = new CapturingLogger<OverageCollection>();
        return new Rig(
            new OverageBilling(store, paypal, standing, new FakeTimeProvider(now ?? Now), log),
            new OverageCollection(store, paypal, standing, collectionLog),
            store, http, log, collectionLog);
    }

    // ----- Closing a period and charging -----

    [Fact]
    public async Task Without_PayPal_nothing_is_closed_and_the_sweep_says_why()
    {
        var rig = Build(options: new PayPalOptions(null, "id", "secret"));
        var sweep = await rig.Billing.RunAsync();
        Assert.Empty(sweep.Periods);
        Assert.Empty(sweep.Suspended);
        Assert.Empty(rig.Store.Calls);
        Assert.Contains(rig.Log.Lines, l => l.Level == LogLevel.Error && l.Text.Contains("PAYPAL_BASE_URL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Periods_are_closed_two_hours_after_they_end_so_the_last_calls_are_in()
    {
        var rig = Build();
        await rig.Billing.RunAsync();
        Assert.Equal(Now - TimeSpan.FromHours(2), rig.Store.Listed!.EndedBy);
    }

    [Fact]
    public async Task An_overage_is_charged_once_to_the_saved_payment_method_and_the_period_moves_on()
    {
        var rig = Build();
        var result = Assert.Single((await rig.Billing.RunAsync()).Periods);

        Assert.Equal(OverageBillingStep.Advanced, result.Step);
        var order = Assert.Single(rig.Http.Requests, r => r.Path == "/v2/checkout/orders");
        Assert.Equal(ChargeId.ToString(), order.RequestId);
        using var body = JsonDocument.Parse(order.Body!);
        var unit = body.RootElement.GetProperty("purchase_units")[0];
        Assert.Equal("5.00", unit.GetProperty("amount").GetProperty("value").GetString());
        Assert.Equal("USD", unit.GetProperty("amount").GetProperty("currency_code").GetString());
        Assert.Equal(PayPalCycleBilling.InvoiceId(ChargeId), unit.GetProperty("invoice_id").GetString());
        Assert.Equal("VAULT-1", body.RootElement.GetProperty("payment_source").GetProperty("paypal").GetProperty("vault_id").GetString());
        Assert.Equal("MERCHANT", body.RootElement.GetProperty("payment_source").GetProperty("paypal").GetProperty("stored_credential").GetProperty("payment_initiator").GetString());

        Assert.Equal(new SettleVoiceOverageChargeCommand(VoiceOverageStatuses.Charged, "ORDER-1", "CAPTURE-1", null), rig.Store.Settled);
        Assert.Equal(new AdvanceVoiceBillingPeriodCommand(Subscription, PeriodEnd, NextBilling), rig.Store.Advanced);
        Assert.Equal(["list", "close", "settle", "advance", "list-suspended"], rig.Store.Calls);
    }

    [Fact]
    public async Task A_period_with_no_overage_charges_nothing_and_moves_on()
    {
        var store = new FakeBilling { Closed = Result<VoiceBillingPeriodClosed>.Success(new VoiceBillingPeriodClosed(Charge(VoiceOverageStatuses.NoOverage, overage: 0), Created: true)) };
        var rig = Build(store);
        Assert.Equal(OverageBillingStep.Advanced, Assert.Single((await rig.Billing.RunAsync()).Periods).Step);
        Assert.DoesNotContain(rig.Http.Requests, r => r.Path == "/v2/checkout/orders");
        Assert.DoesNotContain("settle", store.Calls);
    }

    [Fact]
    public async Task A_declined_charge_is_recorded_failed_suspends_the_tenant_and_is_not_sent_again()
    {
        var http = new Services
        {
            OrderStatus = HttpStatusCode.UnprocessableEntity,
            OrderBody = """{"name":"UNPROCESSABLE_ENTITY","details":[{"issue":"INSTRUMENT_DECLINED"}]}""",
        };
        var rig = Build(http: http);
        Assert.Equal(OverageBillingStep.Advanced, Assert.Single((await rig.Billing.RunAsync()).Periods).Step);

        Assert.Equal(VoiceOverageStatuses.Failed, rig.Store.Settled!.Status);
        Assert.Contains("INSTRUMENT_DECLINED", rig.Store.Settled.Failure, StringComparison.Ordinal);
        Assert.Contains("nothing was charged", rig.Store.Settled.Failure, StringComparison.Ordinal);
        Assert.Single(http.Requests, r => r.Path == "/v2/checkout/orders");
        Assert.Contains("suspend", rig.Store.Calls);
        Assert.Equal(VoiceTenantStatuses.Suspended, rig.Store.Current.TenantStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_charge_PayPal_did_not_answer_is_failed_with_the_invoice_to_check_never_resent(bool unreachable)
    {
        var http = new Services { OrderUnreachable = unreachable, OrderStatus = HttpStatusCode.InternalServerError, OrderBody = "{}" };
        var rig = Build(http: http);
        await rig.Billing.RunAsync();

        Assert.Equal(VoiceOverageStatuses.Failed, rig.Store.Settled!.Status);
        Assert.Contains("unknown", rig.Store.Settled.Failure, StringComparison.Ordinal);
        Assert.Contains(PayPalCycleBilling.InvoiceId(ChargeId), rig.Store.Settled.Failure, StringComparison.Ordinal);
        Assert.Single(http.Requests, r => r.Path == "/v2/checkout/orders");
    }

    [Fact]
    public async Task An_order_PayPal_did_not_complete_is_not_recorded_as_paid()
    {
        var rig = Build(http: new Services { OrderBody = """{"id":"ORDER-2","status":"PAYER_ACTION_REQUIRED","purchase_units":[]}""" });
        await rig.Billing.RunAsync();
        Assert.Equal(VoiceOverageStatuses.Failed, rig.Store.Settled!.Status);
        Assert.Contains("ORDER-2", rig.Store.Settled.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_charge_another_sweep_recorded_moments_ago_is_left_to_it()
    {
        var store = new FakeBilling { Closed = Result<VoiceBillingPeriodClosed>.Success(new VoiceBillingPeriodClosed(Charge(createdAt: Now.AddMinutes(-2)), Created: false)) };
        var rig = Build(store);
        Assert.Equal(OverageBillingStep.ChargeInProgress, Assert.Single((await rig.Billing.RunAsync()).Periods).Step);
        Assert.Empty(rig.Http.Requests);
        Assert.Equal(["list", "close", "list-suspended"], store.Calls);
    }

    [Fact]
    public async Task A_charge_left_without_an_outcome_is_failed_with_the_invoice_to_check_and_not_sent()
    {
        var store = new FakeBilling { Closed = Result<VoiceBillingPeriodClosed>.Success(new VoiceBillingPeriodClosed(Charge(createdAt: Now.AddMinutes(-20)), Created: false)) };
        var rig = Build(store);
        await rig.Billing.RunAsync();

        Assert.DoesNotContain(rig.Http.Requests, r => r.Path == "/v2/checkout/orders");
        Assert.Equal(VoiceOverageStatuses.Failed, store.Settled!.Status);
        Assert.Contains(PayPalCycleBilling.InvoiceId(ChargeId), store.Settled.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_charge_whose_outcome_cannot_be_recorded_is_logged_critical_with_the_order()
    {
        var store = new FakeBilling { SettleFails = true };
        var rig = Build(store);
        Assert.Equal(OverageBillingStep.StoreFailed, Assert.Single((await rig.Billing.RunAsync()).Periods).Step);
        Assert.Contains(rig.Log.Lines, l => l.Level == LogLevel.Critical && l.Text.Contains("ORDER-1", StringComparison.Ordinal));
        Assert.DoesNotContain("advance", store.Calls);
    }

    [Fact]
    public async Task A_subscription_PayPal_has_not_renewed_waits_where_it_is()
    {
        // The last payment is the one that opened this period, so the next month has not been paid.
        var rig = Build(http: new Services { SubscriptionBody = SubscriptionJson("ACTIVE", PeriodStart, PeriodEnd) });
        Assert.Equal(OverageBillingStep.AwaitingRenewal, Assert.Single((await rig.Billing.RunAsync()).Periods).Step);
        Assert.Null(rig.Store.Advanced);
    }

    [Fact]
    public async Task A_subscription_PayPal_suspends_is_recorded_and_suspends_the_tenant()
    {
        var rig = Build(http: new Services { SubscriptionBody = SubscriptionJson("SUSPENDED", PeriodStart, NextBilling) });
        Assert.Equal(OverageBillingStep.Suspended, Assert.Single((await rig.Billing.RunAsync()).Periods).Step);
        Assert.Contains(new SetVoiceBillingStatusCommand(Subscription, "SUSPENDED"), rig.Store.StatusesSet);
        Assert.Equal(VoiceTenantStatuses.Suspended, rig.Store.Current.TenantStatus);
        Assert.Null(rig.Store.Advanced);
    }

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("EXPIRED")]
    public async Task A_subscription_PayPal_ends_bills_the_period_then_cancels_the_tenant_and_releases_everything(string status)
    {
        var rig = Build(http: new Services { SubscriptionBody = SubscriptionJson(status, PeriodStart, NextBilling) });
        Assert.Equal(OverageBillingStep.Cancelled, Assert.Single((await rig.Billing.RunAsync()).Periods).Step);

        Assert.Contains("settle", rig.Store.Calls);
        Assert.Equal(VoiceTenantStatuses.Cancelled, rig.Store.Current.TenantStatus);
        var close = Assert.Single(rig.Http.Requests, r => r.Host == "api.twilio.com");
        Assert.Contains("Status=closed", close.Body, StringComparison.Ordinal);
        Assert.Contains("released", rig.Store.Calls);
        Assert.Null(rig.Store.Advanced);
    }

    [Fact]
    public async Task A_due_list_that_cannot_be_read_closes_nothing_and_is_logged()
    {
        var store = new FakeBilling { Due = Result<IReadOnlyList<VoiceSubscriptionDue>>.Failure("down", VoiceFailureCodes.StoreUnavailable) };
        var rig = Build(store);
        Assert.Empty((await rig.Billing.RunAsync()).Periods);
        Assert.Equal(["list", "list-suspended"], store.Calls);
        Assert.Contains(rig.Log.Lines, l => l.Level == LogLevel.Error);
    }

    // ----- Suspended tenants -----

    private static FakeBilling SuspendedStore(VoiceBillingStanding standing) => new()
    {
        Due = Result<IReadOnlyList<VoiceSubscriptionDue>>.Success([]),
        Suspended = [new VoiceSuspendedTenant(Tenant)],
        Current = standing,
    };

    /// <summary>A suspension, a stop and a restore change only the record: nothing is sent to Twilio or anywhere but PayPal.</summary>
    private static void NothingButPayPal(Services http) => Assert.All(http.Requests, r => Assert.Equal("paypal.test", r.Host));

    [Fact]
    public async Task A_suspended_tenant_keeps_answering_for_seven_days()
    {
        var store = SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now.AddDays(-6), unpaid: Charge(VoiceOverageStatuses.Failed)));
        var rig = Build(store);
        Assert.Equal(TenantStandingStep.StillSuspended, Assert.Single((await rig.Billing.RunAsync()).Suspended).Step);
        NothingButPayPal(rig.Http);
    }

    [Fact]
    public async Task After_seven_days_still_owing_its_number_stops_answering_and_that_is_recorded()
    {
        var store = SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now.AddDays(-7), unpaid: Charge(VoiceOverageStatuses.Failed)));
        var rig = Build(store);
        Assert.Equal(TenantStandingStep.AnsweringStopped, Assert.Single((await rig.Billing.RunAsync()).Suspended).Step);

        Assert.Contains("answering-stopped", store.Calls);
        NothingButPayPal(rig.Http);
    }

    [Fact]
    public async Task A_number_already_stopped_is_left_alone_while_still_owing()
    {
        var store = SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now.AddDays(-9), stoppedAt: Now.AddDays(-2), unpaid: Charge(VoiceOverageStatuses.Failed)));
        var rig = Build(store);
        Assert.Equal(TenantStandingStep.StillSuspended, Assert.Single((await rig.Billing.RunAsync()).Suspended).Step);
        NothingButPayPal(rig.Http);
    }

    [Fact]
    public async Task A_clear_tenant_whose_number_stopped_is_restored_by_its_record_alone()
    {
        var store = SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now.AddDays(-9), stoppedAt: Now.AddDays(-2)));
        var rig = Build(store);
        Assert.Equal(TenantStandingStep.Restored, Assert.Single((await rig.Billing.RunAsync()).Suspended).Step);

        Assert.Equal(VoiceTenantStatuses.Active, store.Current.TenantStatus);
        NothingButPayPal(rig.Http);
    }

    [Fact]
    public async Task A_subscription_PayPal_holds_active_again_restores_a_tenant_that_owes_nothing()
    {
        var store = SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now.AddDays(-1), billing: "SUSPENDED"));
        var rig = Build(store);
        Assert.Equal(TenantStandingStep.Restored, Assert.Single((await rig.Billing.RunAsync()).Suspended).Step);
        Assert.Contains(new SetVoiceBillingStatusCommand(Subscription, VoiceBillingStatuses.Active), store.StatusesSet);
        NothingButPayPal(rig.Http);
    }

    [Fact]
    public async Task A_suspended_subscription_PayPal_then_cancels_cancels_the_tenant()
    {
        var store = SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now.AddDays(-3), billing: "SUSPENDED"));
        var rig = Build(store, new Services { SubscriptionBody = SubscriptionJson("CANCELLED", PeriodStart, NextBilling) });
        Assert.Equal(TenantStandingStep.Cancelled, Assert.Single((await rig.Billing.RunAsync()).Suspended).Step);
        Assert.Equal(VoiceTenantStatuses.Cancelled, store.Current.TenantStatus);
        Assert.Contains("released", store.Calls);
    }

    // ----- The owner paying -----

    [Fact]
    public async Task The_owner_is_given_a_PayPal_order_for_the_charges_exact_amount()
    {
        var http = new Services
        {
            OrderBody = """{"id":"ORDER-9","status":"PAYER_ACTION_REQUIRED","links":[{"rel":"self","href":"https://x"},{"rel":"payer-action","href":"https://paypal.test/checkoutnow?token=ORDER-9"}]}""",
            OrderStatus = HttpStatusCode.OK,
        };
        var rig = Build(SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now, unpaid: Charge(VoiceOverageStatuses.Failed))), http);

        var order = await rig.Collection.StartAsync(Tenant, ChargeId, new Uri("https://portal.example/billing/paid"), new Uri("https://portal.example/dashboard"));
        Assert.True(order.IsSuccess, order.Error);
        Assert.Equal("https://paypal.test/checkoutnow?token=ORDER-9", order.Value!.ApprovalUrl);

        using var body = JsonDocument.Parse(Assert.Single(http.Requests, r => r.Path == "/v2/checkout/orders").Body!);
        var unit = body.RootElement.GetProperty("purchase_units")[0];
        Assert.Equal("5.00", unit.GetProperty("amount").GetProperty("value").GetString());
        Assert.Equal(PayPalCycleBilling.OwnerInvoiceId(ChargeId), unit.GetProperty("invoice_id").GetString());
        Assert.False(body.RootElement.GetProperty("payment_source").GetProperty("paypal").TryGetProperty("vault_id", out _));
    }

    [Fact]
    public async Task A_charge_that_is_not_owed_cannot_be_paid()
    {
        var rig = Build(SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now)));
        var order = await rig.Collection.StartAsync(Tenant, ChargeId, new Uri("https://p.example/a"), new Uri("https://p.example/b"));
        Assert.Equal(BillingFailureCodes.ChargeNotOwed, order.ErrorCode);
        Assert.Empty(rig.Http.Requests);
    }

    [Fact]
    public async Task A_completed_payment_is_recorded_and_restores_a_tenant_that_is_then_clear()
    {
        var rig = Build(SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now.AddDays(-8), stoppedAt: Now.AddDays(-1), unpaid: Charge(VoiceOverageStatuses.Failed))));
        var standing = await rig.Collection.CompleteAsync(Tenant, ChargeId, "ORDER-9");

        Assert.True(standing.IsSuccess, standing.Error);
        Assert.Equal(new CollectVoiceOverageChargeCommand("ORDER-9", "CAPTURE-9"), rig.Store.Collected);
        Assert.Equal(VoiceTenantStatuses.Active, standing.Value!.TenantStatus);
        Assert.Equal($"collect-{ChargeId:N}-ORDER-9", Assert.Single(rig.Http.Requests, r => r.Path.EndsWith("/capture", StringComparison.Ordinal)).RequestId);
        NothingButPayPal(rig.Http);
    }

    [Theory]
    [InlineData("4.99", null)]
    [InlineData("5.00", "66666666-6666-6666-6666-666666666666")]
    public async Task A_payment_for_another_amount_or_another_charge_is_not_recorded(string amount, string? customId)
    {
        var http = new Services { CaptureBody = Captured(amount, customId) };
        var rig = Build(SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now, unpaid: Charge(VoiceOverageStatuses.Failed))), http);
        var standing = await rig.Collection.CompleteAsync(Tenant, ChargeId, "ORDER-9");
        Assert.Equal(BillingFailureCodes.PaymentNotCompleted, standing.ErrorCode);
        Assert.Null(rig.Store.Collected);
    }

    [Fact]
    public async Task An_order_PayPal_already_captured_is_read_back_and_recorded_once()
    {
        var http = new Services { CaptureStatus = HttpStatusCode.UnprocessableEntity, CaptureBody = """{"name":"UNPROCESSABLE_ENTITY","details":[{"issue":"ORDER_ALREADY_CAPTURED"}]}""" };
        var rig = Build(SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now, unpaid: Charge(VoiceOverageStatuses.Failed))), http);
        var standing = await rig.Collection.CompleteAsync(Tenant, ChargeId, "ORDER-9");
        Assert.True(standing.IsSuccess, standing.Error);
        Assert.Contains(http.Requests, r => r.Method == HttpMethod.Get && r.Path == "/v2/checkout/orders/ORDER-9");
        Assert.NotNull(rig.Store.Collected);
    }

    [Fact]
    public async Task A_payment_the_store_could_not_record_is_logged_critical_with_the_order()
    {
        var store = SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now, unpaid: Charge(VoiceOverageStatuses.Failed)));
        store.CollectFails = true;
        var rig = Build(store);
        var standing = await rig.Collection.CompleteAsync(Tenant, ChargeId, "ORDER-9");
        Assert.False(standing.IsSuccess);
        Assert.Contains(rig.CollectionLog.Lines, l => l.Level == LogLevel.Critical && l.Text.Contains("ORDER-9", StringComparison.Ordinal));
    }

    // ----- The owner's route -----

    [Theory]
    [InlineData("https://portal.example/billing/paid", true)]
    [InlineData("http://localhost:3010/billing/paid", true)]
    [InlineData("http://portal.example/billing/paid", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/billing/paid", false)]
    [InlineData(null, false)]
    public void PayPal_sends_the_owner_back_only_to_https_or_localhost(string? address, bool accepted) =>
        Assert.Equal(accepted, ReturnAddress.Parse(address) is not null);

    [Fact]
    public async Task The_owner_sees_their_standing_and_never_PayPals_ids()
    {
        var rig = Build(SuspendedStore(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now, unpaid: Charge(VoiceOverageStatuses.Failed))));
        var controller = new BillingController(rig.Collection)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ReceptionistAuth.TenantClaim, Tenant.ToString())], "test")) },
            },
        };

        var ok = Assert.IsType<OkObjectResult>(await controller.Standing(Tenant, CancellationToken.None));
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"answeringUntil\"", json, StringComparison.Ordinal);
        Assert.Contains("\"amountUsd\":5.0", json, StringComparison.Ordinal);
        Assert.DoesNotContain("I-SUB", json, StringComparison.Ordinal);
        Assert.DoesNotContain("VAULT-1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("declined", json, StringComparison.Ordinal);

        Assert.Equal(403, ((ObjectResult)await controller.Standing(Guid.NewGuid(), CancellationToken.None)).StatusCode);
    }
}
