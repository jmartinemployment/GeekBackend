using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeekApplication.Results;

using static GeekAPI.Services.Voice.Billing.PayPalJson;

namespace GeekAPI.Services.Voice.Billing;

/// <summary>
/// The PayPal plan of each tier (Railway variables PAYPAL_PLAN_STARTER, PAYPAL_PLAN_STANDARD, PAYPAL_PLAN_PRO, PAYPAL_PLAN_PREMIUM).
/// The sandbox and the live account have different plans, so they are variables and not code. Read once and checked when a signup
/// starts a subscription: an unset one refuses that signup with a logged error and a 503, and does not stop GeekAPI from booting.
/// Activation checks the plan's price itself, so a variable naming the wrong plan is refused there and never charged at the wrong price.
/// </summary>
public sealed record PayPalPlans(IReadOnlyDictionary<string, string?> ByTier)
{
    public static PayPalPlans FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static PayPalPlans From(Func<string, string?> read) =>
        new(VoiceTierSchedule.All.ToDictionary(t => t.Code, t => read(VariableFor(t.Code))?.Trim(), StringComparer.Ordinal));

    public static string VariableFor(string tierCode) => $"PAYPAL_PLAN_{tierCode}";

    /// <summary>The plan of this tier, or null when its variable is unset or blank.</summary>
    public string? For(string tierCode) =>
        ByTier.TryGetValue(tierCode, out var plan) && !string.IsNullOrWhiteSpace(plan) ? plan : null;
}

/// <summary>A PayPal page the visitor is sent to: what PayPal made (a subscription or a setup token) and where to approve it.</summary>
public sealed record PayPalApproval(string Id, string ApprovalUrl);

/// <summary>
/// The two PayPal steps of /signup (docs/Geek-Receptionist.md section 5, item 2), each approved by the visitor in PayPal: the monthly
/// subscription to the tier's plan, tagged with the tenant so activation can tell it is this tenant's, and saving their PayPal account
/// for the overage charges (a setup token the visitor approves, then turned into a payment token). Nothing here charges anything or
/// activates anything; <see cref="PayPalBillingVerifier"/> checks the result before any number is bought. Nothing is retried.
/// </summary>
public sealed class PayPalSignup
{
    private const string BrandName = "Geek-Receptionist";

    private readonly PayPalRest _rest;
    private readonly PayPalOptions _options;
    private readonly PayPalPlans _plans;
    private readonly ILogger<PayPalSignup> _logger;

    public PayPalSignup(HttpClient http, PayPalOptions options, PayPalPlans plans, ILogger<PayPalSignup> logger)
    {
        _rest = new PayPalRest(http, options, logger);
        _options = options;
        _plans = plans;
        _logger = logger;
    }

    /// <summary>A subscription to the tier's plan, waiting for the visitor to approve it in PayPal. Its custom_id is the tenant.</summary>
    public async Task<Result<PayPalApproval>> StartSubscriptionAsync(Guid tenantId, VoiceTier tier, Uri returnUrl, Uri cancelUrl, CancellationToken ct = default)
    {
        var plan = _plans.For(tier.Code);
        if (plan is null)
        {
            _logger.LogError("A signup could not start a PayPal subscription: {Variable} is not set.", PayPalPlans.VariableFor(tier.Code));
            return Result<PayPalApproval>.Failure("Billing is not configured.", BillingFailureCodes.BillingNotConfigured);
        }

        return await CreateAsync("v1/billing/subscriptions", new
        {
            plan_id = plan,
            custom_id = tenantId.ToString(),
            application_context = new
            {
                brand_name = BrandName,
                shipping_preference = "NO_SHIPPING",
                user_action = "SUBSCRIBE_NOW",
                return_url = returnUrl.AbsoluteUri,
                cancel_url = cancelUrl.AbsoluteUri,
            },
        }, "approve", "the subscription", ct).ConfigureAwait(false);
    }

    /// <summary>A setup token that saves the visitor's PayPal account for later charges once they approve it in PayPal.</summary>
    public Task<Result<PayPalApproval>> StartVaultAsync(Uri returnUrl, Uri cancelUrl, CancellationToken ct = default) =>
        CreateAsync("v3/vault/setup-tokens", new
        {
            payment_source = new
            {
                paypal = new
                {
                    usage_type = "MERCHANT",
                    experience_context = new
                    {
                        brand_name = BrandName,
                        shipping_preference = "NO_SHIPPING",
                        return_url = returnUrl.AbsoluteUri,
                        cancel_url = cancelUrl.AbsoluteUri,
                    },
                },
            },
        }, "approve", "saving the payment method", ct);

    /// <summary>
    /// Turns a setup token the visitor approved into the payment token overage charges use. Sent once, with the setup token as
    /// PayPal-Request-Id, so a second press answers the same token rather than making another. A setup token PayPal refuses (not
    /// approved, expired, unknown) is the visitor's to approve again (400).
    /// </summary>
    public async Task<Result<string>> CompleteVaultAsync(string setupTokenId, CancellationToken ct = default)
    {
        var missing = _options.Missing();
        if (missing.Count > 0)
        {
            _logger.LogError("A signup could not save its payment method: {Variables} not set.", string.Join(", ", missing));
            return Result<string>.Failure("Billing is not configured.", BillingFailureCodes.BillingNotConfigured);
        }

        var token = await _rest.GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (!token.IsSuccess) return Result<string>.Failure(token.Error!, token.ErrorCode!);

        const string path = "v3/vault/payment-tokens";
        var route = $"POST {path}";
        using var request = JsonRequest(path, new { payment_source = new { token = new { id = setupTokenId, type = "SETUP_TOKEN" } } }, token.Value!);
        request.Headers.Add("PayPal-Request-Id", $"vault-{setupTokenId}");
        var response = await _rest.SendAsync(request, route, ct).ConfigureAwait(false);
        if (response is null) return PayPalRest.Unavailable<string>("PayPal could not be reached.");

        using (response)
        {
            using var doc = await ReadObjectAsync(response, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 400 and < 500)
            {
                _logger.LogWarning("PayPal refused to save a signup's payment method with {Status} for {Route}.", (int)response.StatusCode, route);
                return Result<string>.Failure("PayPal did not save the payment method. Approve it in PayPal again.", BillingFailureCodes.VaultTokenInvalid);
            }

            var id = doc is null ? null : Text(doc.RootElement, "id");
            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(id))
            {
                _logger.LogError("PayPal answered {Status} for {Route} with no payment token.", (int)response.StatusCode, route);
                return PayPalRest.Unavailable<string>($"PayPal answered {(int)response.StatusCode}.");
            }

            return Result<string>.Success(id);
        }
    }

    private async Task<Result<PayPalApproval>> CreateAsync(string path, object body, string approveRel, string what, CancellationToken ct)
    {
        var missing = _options.Missing();
        if (missing.Count > 0)
        {
            _logger.LogError("A signup could not start {What}: {Variables} not set.", what, string.Join(", ", missing));
            return Result<PayPalApproval>.Failure("Billing is not configured.", BillingFailureCodes.BillingNotConfigured);
        }

        var token = await _rest.GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (!token.IsSuccess) return Result<PayPalApproval>.Failure(token.Error!, token.ErrorCode!);

        var route = $"POST {path}";
        using var request = JsonRequest(path, body, token.Value!);
        var response = await _rest.SendAsync(request, route, ct).ConfigureAwait(false);
        if (response is null) return PayPalRest.Unavailable<PayPalApproval>("PayPal could not be reached.");

        using (response)
        {
            using var doc = await ReadObjectAsync(response, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || doc is null)
            {
                _logger.LogError("PayPal answered {Status} for {Route} ({What}).", (int)response.StatusCode, route, what);
                return PayPalRest.Unavailable<PayPalApproval>($"PayPal answered {(int)response.StatusCode}.");
            }

            var id = Text(doc.RootElement, "id");
            var approval = Links(doc.RootElement).FirstOrDefault(l => l.Rel == approveRel).Href;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(approval) || !approval.StartsWith("https://", StringComparison.Ordinal))
            {
                _logger.LogError("PayPal answered {Route} ({What}) with no id or approval link.", route, what);
                return PayPalRest.Unavailable<PayPalApproval>("PayPal answered without an approval link.");
            }

            return Result<PayPalApproval>.Success(new PayPalApproval(id, approval));
        }
    }

    private HttpRequestMessage JsonRequest(string path, object body, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _rest.Url(path))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Prefer", "return=representation");
        return request;
    }

    private static IEnumerable<(string? Rel, string? Href)> Links(JsonElement element)
    {
        if (!element.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array) yield break;
        foreach (var link in links.EnumerateArray())
            yield return (Text(link, "rel"), Text(link, "href"));
    }

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
