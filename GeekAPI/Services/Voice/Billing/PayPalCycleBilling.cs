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

/// <summary>An order for the owner to approve in PayPal: its id and the PayPal page the owner is sent to.</summary>
public sealed record OwnerOverageOrder(string OrderId, string ApprovalUrl);

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
                    amount = new { currency_code = "USD", value = Money(charge.AmountUsd) },
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

        using var request = JsonRequest(HttpMethod.Post, OrdersPath, body, token.Value!);
        request.Headers.Add("PayPal-Request-Id", charge.Id.ToString());

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

    /// <summary>The invoice id of the owner's own payment of a charge: a different one from the automatic charge's, and as unique.</summary>
    public static string OwnerInvoiceId(Guid chargeId) => $"GR-OVERAGE-{chargeId:N}-OWNER";

    /// <summary>
    /// An order for the owner to pay a failed overage charge themselves: the charge's exact amount, approved by the owner in PayPal and
    /// taken only when <see cref="CaptureOwnerOrderAsync"/> captures it. Creating an order takes no money.
    /// </summary>
    public async Task<Result<OwnerOverageOrder>> CreateOwnerOrderAsync(VoiceOverageCharge charge, Uri returnUrl, Uri cancelUrl, CancellationToken ct = default)
    {
        var token = await _rest.GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (!token.IsSuccess) return Result<OwnerOverageOrder>.Failure(token.Error!, token.ErrorCode!);

        var body = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = "overage",
                    invoice_id = OwnerInvoiceId(charge.Id),
                    custom_id = charge.Id.ToString(),
                    description = $"Geek-Receptionist overage: {charge.OverageMinutes} minutes, {charge.PeriodStart:yyyy-MM-dd} to {charge.PeriodEnd:yyyy-MM-dd}",
                    amount = new { currency_code = "USD", value = Money(charge.AmountUsd) },
                },
            },
            payment_source = new
            {
                paypal = new
                {
                    experience_context = new
                    {
                        brand_name = "Geek-Receptionist",
                        shipping_preference = "NO_SHIPPING",
                        user_action = "PAY_NOW",
                        return_url = returnUrl.AbsoluteUri,
                        cancel_url = cancelUrl.AbsoluteUri,
                    },
                },
            },
        };

        var route = $"POST {OrdersPath}";
        using var request = JsonRequest(HttpMethod.Post, OrdersPath, body, token.Value!);
        var response = await _rest.SendAsync(request, route, ct).ConfigureAwait(false);
        if (response is null) return PayPalRest.Unavailable<OwnerOverageOrder>("PayPal could not be reached.");

        using (response)
        {
            using var doc = await ReadObjectAsync(response, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || doc is null)
            {
                _logger.LogError("PayPal answered {Status} for {Route} (the owner's overage order).", (int)response.StatusCode, route);
                return PayPalRest.Unavailable<OwnerOverageOrder>($"PayPal answered {(int)response.StatusCode}.");
            }

            var orderId = Text(doc.RootElement, "id");
            var approval = Links(doc.RootElement).FirstOrDefault(l => l.Rel is "payer-action" or "approve").Href;
            if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(approval))
            {
                _logger.LogError("PayPal answered the owner's overage order with no id or approval link for {Route}.", route);
                return PayPalRest.Unavailable<OwnerOverageOrder>("PayPal answered the order without an approval link.");
            }

            return Result<OwnerOverageOrder>.Success(new OwnerOverageOrder(orderId, approval));
        }
    }

    /// <summary>
    /// Takes the money for an order the owner approved, once (the charge and order as PayPal-Request-Id), and accepts it only when PayPal
    /// says the order and its capture COMPLETED, for this charge and its exact amount in USD. An order PayPal has already captured is read
    /// back and judged the same way, so the owner's page being opened twice records one payment.
    /// </summary>
    public async Task<Result<OverageChargeOutcome>> CaptureOwnerOrderAsync(VoiceOverageCharge charge, string orderId, CancellationToken ct = default)
    {
        var token = await _rest.GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (!token.IsSuccess) return Result<OverageChargeOutcome>.Failure(token.Error!, token.ErrorCode!);

        var path = $"{OrdersPath}/{Uri.EscapeDataString(orderId)}/capture";
        var route = $"POST {OrdersPath}/{{id}}/capture";
        using var request = JsonRequest(HttpMethod.Post, path, new { }, token.Value!);
        request.Headers.Add("PayPal-Request-Id", $"collect-{charge.Id:N}-{orderId}");
        var response = await _rest.SendAsync(request, route, ct).ConfigureAwait(false);
        if (response is null) return PayPalRest.Unavailable<OverageChargeOutcome>("PayPal could not be reached; nothing is recorded as paid.");

        using (response)
        {
            using var doc = await ReadObjectAsync(response, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode && doc is not null) return Judge(doc.RootElement, charge, orderId);

            var issues = doc is null ? [] : Issues(doc.RootElement).ToList();
            if ((int)response.StatusCode == 422 && issues.Contains("ORDER_ALREADY_CAPTURED"))
            {
                var order = await _rest.GetJsonAsync($"{OrdersPath}/{Uri.EscapeDataString(orderId)}", token.Value!,
                    BillingFailureCodes.PaymentNotCompleted, "That PayPal order was not found.", ct).ConfigureAwait(false);
                if (!order.IsSuccess) return Result<OverageChargeOutcome>.Failure(order.Error!, order.ErrorCode!);
                using var captured = order.Value!;
                return Judge(captured.RootElement, charge, orderId);
            }

            if ((int)response.StatusCode is >= 400 and < 500)
            {
                _logger.LogWarning("PayPal refused to capture the owner's overage order with {Status} for {Route}.", (int)response.StatusCode, route);
                var name = doc is null ? null : Text(doc.RootElement, "name");
                return Result<OverageChargeOutcome>.Failure(
                    $"PayPal did not take the payment ({name ?? "no reason given"}{(issues.Count > 0 ? ": " + string.Join(", ", issues) : "")}).",
                    BillingFailureCodes.PaymentNotCompleted);
            }

            _logger.LogError("PayPal answered {Status} for {Route} (the owner's overage order).", (int)response.StatusCode, route);
            return PayPalRest.Unavailable<OverageChargeOutcome>($"PayPal answered {(int)response.StatusCode}; nothing is recorded as paid.");
        }
    }

    /// <summary>Paid means the order and its capture COMPLETED, the purchase is this charge's, and the capture is the charge's exact amount in USD.</summary>
    private static Result<OverageChargeOutcome> Judge(JsonElement order, VoiceOverageCharge charge, string orderId)
    {
        var capture = FirstCapture(order);
        var captureId = capture is null ? null : Text(capture.Value, "id");
        var unit = order.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array && units.GetArrayLength() > 0 ? units[0] : (JsonElement?)null;

        if (Text(order, "status") != "COMPLETED" || capture is null || Text(capture.Value, "status") != "COMPLETED" || string.IsNullOrWhiteSpace(captureId))
            return Result<OverageChargeOutcome>.Failure("PayPal has not completed this payment.", BillingFailureCodes.PaymentNotCompleted);
        if (unit is null || Text(unit.Value, "custom_id") != charge.Id.ToString())
            return Result<OverageChargeOutcome>.Failure("That PayPal order is not for this charge.", BillingFailureCodes.PaymentNotCompleted);
        if (Text(capture.Value, "amount", "currency_code") != "USD" || Number(capture.Value, "amount", "value") != charge.AmountUsd)
            return Result<OverageChargeOutcome>.Failure("That PayPal payment is not this charge's amount.", BillingFailureCodes.PaymentNotCompleted);

        return Result<OverageChargeOutcome>.Success(OverageChargeOutcome.Charged(orderId, captureId));
    }

    private HttpRequestMessage JsonRequest(HttpMethod method, string path, object body, string accessToken)
    {
        var request = new HttpRequestMessage(method, _rest.Url(path))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Prefer", "return=representation");
        return request;
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static IEnumerable<(string? Rel, string? Href)> Links(JsonElement element)
    {
        if (!element.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array) yield break;
        foreach (var link in links.EnumerateArray())
            yield return (Text(link, "rel"), Text(link, "href"));
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
