using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

using static GeekAPI.Services.Voice.Billing.PayPalJson;

namespace GeekAPI.Services.Voice.Billing;

/// <summary>What became of one overage charge: charged, with PayPal's order and capture ids, or failed, with the reason.</summary>
public sealed record OverageChargeOutcome(string Status, string? PaypalOrderId, string? PaypalCaptureId, string? Failure)
{
    public static OverageChargeOutcome Charged(string orderId, string captureId) => new(VoiceOverageStatuses.Charged, orderId, captureId, null);
    public static OverageChargeOutcome Failed(string reason) => new(VoiceOverageStatuses.Failed, null, null, reason);
}

/// <summary>A PayPal subscription as end-of-cycle billing reads it: its status, when it was last paid and when PayPal bills it next.</summary>
public sealed record PayPalSubscriptionState(string Status, DateTimeOffset? LastPaymentAt, DateTimeOffset? NextBillingAt);

/// <summary>
/// What end-of-cycle billing asks PayPal (docs/Geek-Receptionist.md section 5, item 2): to charge a period's overage to the tenant's
/// saved payment method through <c>/v2/checkout/orders</c>, with no buyer present, and to read the subscription to see whether it has
/// renewed. Each request is sent once. A charge carries the charge's id as its PayPal-Request-Id and a unique invoice id, so PayPal
/// itself refuses a second charge for the same period. Nothing is retried: a charge whose answer never arrived is failed, and its
/// reason says to look the invoice up in PayPal before charging again. Logs carry the status and the route, never a body, a token id
/// or a secret.
/// </summary>
public sealed class PayPalCycleBilling
{
    private const string OrdersPath = "v2/checkout/orders";

    private readonly PayPalRest _rest;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalCycleBilling> _logger;

    public PayPalCycleBilling(HttpClient http, PayPalOptions options, ILogger<PayPalCycleBilling> logger)
    {
        _rest = new PayPalRest(http, options, logger);
        _options = options;
        _logger = logger;
    }

    /// <summary>The PayPal variables that are unset, so a sweep can stop before it closes anything.</summary>
    public IReadOnlyList<string> Missing() => _options.Missing();

    /// <summary>The invoice id PayPal holds for a charge. PayPal refuses a second order with the same invoice id.</summary>
    public static string InvoiceId(Guid chargeId) => $"GR-OVERAGE-{chargeId:N}";

    public async Task<OverageChargeOutcome> ChargeAsync(VoiceOverageCharge charge, CancellationToken ct = default)
    {
        var invoice = InvoiceId(charge.Id);
        var token = await _rest.GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (!token.IsSuccess) return OverageChargeOutcome.Failed($"No PayPal access token ({token.Error}); nothing was charged.");

        var body = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = "overage",
                    invoice_id = invoice,
                    custom_id = charge.Id.ToString(),
                    description = $"Geek-Receptionist overage: {charge.OverageMinutes} minutes, {charge.PeriodStart:yyyy-MM-dd} to {charge.PeriodEnd:yyyy-MM-dd}",
                    amount = new { currency_code = "USD", value = charge.AmountUsd.ToString("0.00", CultureInfo.InvariantCulture) },
                },
            },
            payment_source = new
            {
                paypal = new
                {
                    vault_id = charge.PaypalVaultTokenId,
                    stored_credential = new { payment_initiator = "MERCHANT", usage = "SUBSEQUENT", charge_pattern = "UNSCHEDULED_POSTPAID" },
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _rest.Url(OrdersPath))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value!);
        request.Headers.Add("PayPal-Request-Id", charge.Id.ToString());
        request.Headers.Add("Prefer", "return=representation");

        var route = $"POST {OrdersPath}";
        var response = await _rest.SendAsync(request, route, ct).ConfigureAwait(false);
        if (response is null) return Unknown(invoice, "PayPal could not be reached or did not answer in time");

        using (response)
        {
            var status = (int)response.StatusCode;
            using var doc = await ReadObjectAsync(response, ct).ConfigureAwait(false);

            if (status >= 500)
            {
                _logger.LogError("PayPal answered {Status} for {Route}.", status, route);
                return Unknown(invoice, $"PayPal answered {status}");
            }

            if (status >= 400)
            {
                _logger.LogError("PayPal refused an overage charge with {Status} for {Route}.", status, route);
                var name = doc is null ? null : Text(doc.RootElement, "name");
                var issues = doc is null ? "" : string.Join(", ", Issues(doc.RootElement));
                return OverageChargeOutcome.Failed($"PayPal refused the charge ({status} {name ?? "no reason given"}{(issues.Length > 0 ? ": " + issues : "")}); nothing was charged.");
            }

            if (doc is null)
            {
                _logger.LogError("PayPal answered a body that did not parse for {Route}.", route);
                return Unknown(invoice, $"PayPal answered {status} with a body that did not parse");
            }

            var orderId = Text(doc.RootElement, "id");
            var orderStatus = Text(doc.RootElement, "status");
            var capture = FirstCapture(doc.RootElement);
            var captureId = capture is null ? null : Text(capture.Value, "id");
            var captureStatus = capture is null ? null : Text(capture.Value, "status");

            if (orderStatus == "COMPLETED" && captureStatus == "COMPLETED" && !string.IsNullOrWhiteSpace(orderId) && !string.IsNullOrWhiteSpace(captureId))
                return OverageChargeOutcome.Charged(orderId, captureId);

            _logger.LogError("PayPal answered an overage order that is not paid for {Route}: order {OrderStatus}, capture {CaptureStatus}.", route, orderStatus, captureStatus);
            return OverageChargeOutcome.Failed(
                $"PayPal answered order {orderId ?? "with no id"} as {orderStatus ?? "no status"} with capture {captureStatus ?? "none"}, so it is not recorded as paid; check invoice {invoice} in PayPal.");
        }
    }

    public async Task<Result<PayPalSubscriptionState>> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        var token = await _rest.GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (!token.IsSuccess) return Result<PayPalSubscriptionState>.Failure(token.Error!, token.ErrorCode!);

        var subscription = await _rest.GetJsonAsync($"v1/billing/subscriptions/{Uri.EscapeDataString(subscriptionId)}", token.Value!,
            BillingFailureCodes.SubscriptionNotFound, "That PayPal subscription was not found.", ct).ConfigureAwait(false);
        if (!subscription.IsSuccess) return Result<PayPalSubscriptionState>.Failure(subscription.Error!, subscription.ErrorCode!);

        using var doc = subscription.Value!;
        var status = Text(doc.RootElement, "status");
        if (string.IsNullOrWhiteSpace(status))
            return Result<PayPalSubscriptionState>.Failure("PayPal answered a subscription with no status.", BillingFailureCodes.BillingUnavailable);

        return Result<PayPalSubscriptionState>.Success(new PayPalSubscriptionState(
            status,
            Time(doc.RootElement, "billing_info", "last_payment", "time"),
            Time(doc.RootElement, "billing_info", "next_billing_time")));
    }

    private static OverageChargeOutcome Unknown(string invoice, string what) => OverageChargeOutcome.Failed(
        $"{what}, so whether the charge went through is unknown; search PayPal for invoice {invoice} before charging again.");

    private static JsonElement? FirstCapture(JsonElement order)
    {
        if (!order.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array) return null;
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.ValueKind == JsonValueKind.Object
                && unit.TryGetProperty("payments", out var payments) && payments.ValueKind == JsonValueKind.Object
                && payments.TryGetProperty("captures", out var captures) && captures.ValueKind == JsonValueKind.Array)
            {
                foreach (var capture in captures.EnumerateArray())
                    return capture;
            }
        }

        return null;
    }

    private static IEnumerable<string> Issues(JsonElement error)
    {
        if (!error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array) yield break;
        foreach (var detail in details.EnumerateArray())
        {
            var issue = Text(detail, "issue");
            if (!string.IsNullOrWhiteSpace(issue)) yield return issue;
        }
    }

    /// <summary>The body as a JSON object, or null when it is empty or is not one.</summary>
    private static async Task<JsonDocument?> ReadObjectAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
            doc.Dispose();
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
