using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Billing;

/// <summary>Why a billing check failed. The controller turns the code into a status; the wording is for people and never decides one.</summary>
public static class BillingFailureCodes
{
    public const string TierUnknown = "tier_unknown";
    public const string SubscriptionNotFound = "subscription_not_found";
    public const string SubscriptionNotActive = "subscription_not_active";
    public const string SubscriptionUnpaid = "subscription_unpaid";
    public const string PlanMismatch = "plan_mismatch";
    public const string VaultTokenInvalid = "vault_token_invalid";
    public const string BillingNotConfigured = "billing_not_configured";
    public const string BillingUnavailable = "billing_unavailable";
}

/// <summary>What PayPal confirmed, in the form the tenant's subscription is stored: the tier's own price and minutes, never the request's.</summary>
public sealed record VerifiedBilling(
    VoiceTier Tier,
    string SubscriptionId,
    string VaultTokenId,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset CurrentPeriodEnd,
    string BillingStatus);

/// <summary>
/// Confirms, before anything is provisioned, that the tenant has paid (docs/Geek-Receptionist.md section 5, item 2: no free trials).
/// It asks PayPal four things, each once: an access token, the subscription, the subscription's plan and the vaulted payment token.
/// The subscription must be ACTIVE and already paid, its plan must charge the tier's exact monthly price in USD every month, and the
/// vault token must exist and be a PayPal wallet. A PayPal 404 for an id the visitor sent is the visitor's mistake (400); a key PayPal
/// refuses, a 5xx, a timeout and an answer that is not what PayPal documents are all billing unavailable (503), logged with the status
/// and the route and never with a body. Nothing is retried and nothing is guessed: a field PayPal did not send is a refusal.
/// </summary>
public sealed class PayPalBillingVerifier
{
    private readonly HttpClient _http;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalBillingVerifier> _logger;

    public PayPalBillingVerifier(HttpClient http, PayPalOptions options, ILogger<PayPalBillingVerifier> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<Result<VerifiedBilling>> VerifyAsync(string? tierCode, string? subscriptionId, string? vaultTokenId, CancellationToken ct = default)
    {
        var tier = VoiceTierSchedule.Find(tierCode);
        if (tier is null)
            return Result<VerifiedBilling>.Failure($"The tier must be one of: {string.Join(", ", VoiceTierSchedule.All.Select(t => t.Code))}.", BillingFailureCodes.TierUnknown);
        if (string.IsNullOrWhiteSpace(subscriptionId))
            return Result<VerifiedBilling>.Failure("A PayPal subscription is required.", BillingFailureCodes.SubscriptionNotFound);
        if (string.IsNullOrWhiteSpace(vaultTokenId))
            return Result<VerifiedBilling>.Failure("A PayPal vault token is required.", BillingFailureCodes.VaultTokenInvalid);

        var missing = _options.Missing();
        if (missing.Count > 0)
        {
            _logger.LogError("PayPal billing cannot be verified: {Variables} not set.", string.Join(", ", missing));
            return Result<VerifiedBilling>.Failure("Billing is not configured.", BillingFailureCodes.BillingNotConfigured);
        }

        var token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (!token.IsSuccess) return Result<VerifiedBilling>.Failure(token.Error!, token.ErrorCode!);

        var subscription = await GetJsonAsync($"v1/billing/subscriptions/{Uri.EscapeDataString(subscriptionId)}", token.Value!, BillingFailureCodes.SubscriptionNotFound, "That PayPal subscription was not found.", ct).ConfigureAwait(false);
        if (!subscription.IsSuccess) return Result<VerifiedBilling>.Failure(subscription.Error!, subscription.ErrorCode!);
        using var subscriptionDoc = subscription.Value!;
        var sub = subscriptionDoc.RootElement;

        if (Text(sub, "status") != "ACTIVE")
            return Result<VerifiedBilling>.Failure("The PayPal subscription is not active.", BillingFailureCodes.SubscriptionNotActive);

        var paidAt = Time(sub, "billing_info", "last_payment", "time");
        var nextBilling = Time(sub, "billing_info", "next_billing_time");
        if (paidAt is null || nextBilling is null || nextBilling <= paidAt)
            return Result<VerifiedBilling>.Failure("The PayPal subscription has no paid period yet.", BillingFailureCodes.SubscriptionUnpaid);

        var planId = Text(sub, "plan_id");
        if (string.IsNullOrWhiteSpace(planId))
            return Unexpected("the subscription named no plan");

        var plan = await GetJsonAsync($"v1/billing/plans/{Uri.EscapeDataString(planId)}", token.Value!, BillingFailureCodes.PlanMismatch, "The PayPal plan was not found.", ct).ConfigureAwait(false);
        if (!plan.IsSuccess) return Result<VerifiedBilling>.Failure(plan.Error!, plan.ErrorCode!);
        using var planDoc = plan.Value!;

        var planProblem = CheckPlan(planDoc.RootElement, tier);
        if (planProblem is not null)
            return Result<VerifiedBilling>.Failure(planProblem, BillingFailureCodes.PlanMismatch);

        var vault = await GetJsonAsync($"v3/vault/payment-tokens/{Uri.EscapeDataString(vaultTokenId)}", token.Value!, BillingFailureCodes.VaultTokenInvalid, "That PayPal vault token was not found.", ct).ConfigureAwait(false);
        if (!vault.IsSuccess) return Result<VerifiedBilling>.Failure(vault.Error!, vault.ErrorCode!);
        using var vaultDoc = vault.Value!;
        if (!vaultDoc.RootElement.TryGetProperty("payment_source", out var source)
            || source.ValueKind != JsonValueKind.Object
            || !source.TryGetProperty("paypal", out var wallet)
            || wallet.ValueKind != JsonValueKind.Object)
            return Result<VerifiedBilling>.Failure("The vault token is not a PayPal account.", BillingFailureCodes.VaultTokenInvalid);

        return Result<VerifiedBilling>.Success(new VerifiedBilling(tier, subscriptionId, vaultTokenId, paidAt.Value, nextBilling.Value, "ACTIVE"));
    }

    /// <summary>
    /// The plan must charge this tier's exact monthly price in USD: one regular cycle with a fixed price, billed every one month.
    /// Returns the reason it does not, or null.
    /// </summary>
    private static string? CheckPlan(JsonElement plan, VoiceTier tier)
    {
        if (!plan.TryGetProperty("billing_cycles", out var cycles) || cycles.ValueKind != JsonValueKind.Array)
            return "The PayPal plan has no billing cycles.";

        var regular = cycles.EnumerateArray().Where(c => Text(c, "tenure_type") == "REGULAR").ToList();
        if (regular.Count != 1)
            return "The PayPal plan must have exactly one regular billing cycle.";
        if (cycles.EnumerateArray().Any(c => Text(c, "tenure_type") == "TRIAL"))
            return "The PayPal plan has a free trial, and there are no free trials.";

        var cycle = regular[0];
        if (Text(cycle, "frequency", "interval_unit") != "MONTH" || Number(cycle, "frequency", "interval_count") != 1m)
            return "The PayPal plan must bill every one month.";

        if (Text(cycle, "pricing_scheme", "fixed_price", "currency_code") != "USD")
            return "The PayPal plan must charge in USD.";
        var price = Number(cycle, "pricing_scheme", "fixed_price", "value");
        if (price != tier.MonthlyPriceUsd)
            return $"The PayPal plan charges {price?.ToString("0.00", CultureInfo.InvariantCulture) ?? "no fixed price"}, and {tier.Code} is {tier.MonthlyPriceUsd.ToString("0.00", CultureInfo.InvariantCulture)}.";

        return null;
    }

    private async Task<Result<string>> GetAccessTokenAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("v1/oauth2/token"))
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")]),
        };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        var response = await SendAsync(request, "POST v1/oauth2/token", ct).ConfigureAwait(false);
        if (response is null) return Unavailable<string>("PayPal could not be reached.");
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal answered {Status} for POST v1/oauth2/token.", (int)response.StatusCode);
                return Unavailable<string>($"PayPal answered {(int)response.StatusCode}.");
            }

            try
            {
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
                var accessToken = Text(doc.RootElement, "access_token");
                if (!string.IsNullOrWhiteSpace(accessToken)) return Result<string>.Success(accessToken);
            }
            catch (JsonException)
            {
            }

            _logger.LogError("PayPal answered a token body that was not usable for POST v1/oauth2/token.");
            return Unavailable<string>("PayPal answered a body that was not usable.");
        }
    }

    /// <summary>GET a PayPal resource. A 404 is <paramref name="notFoundCode"/>; any other failure is billing unavailable.</summary>
    private async Task<Result<JsonDocument>> GetJsonAsync(string path, string accessToken, string notFoundCode, string notFoundMessage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var route = $"GET {RouteOf(path)}";
        var response = await SendAsync(request, route, ct).ConfigureAwait(false);
        if (response is null) return Unavailable<JsonDocument>("PayPal could not be reached.");
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result<JsonDocument>.Failure(notFoundMessage, notFoundCode);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal answered {Status} for {Route}.", (int)response.StatusCode, route);
                return Unavailable<JsonDocument>($"PayPal answered {(int)response.StatusCode}.");
            }

            try
            {
                var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.ValueKind == JsonValueKind.Object) return Result<JsonDocument>.Success(doc);
                doc.Dispose();
            }
            catch (JsonException)
            {
            }

            _logger.LogError("PayPal answered a body that did not parse for {Route}.", route);
            return Unavailable<JsonDocument>("PayPal answered a body that did not parse.");
        }
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request, string route, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "PayPal unreachable for {Route}.", route);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("PayPal timed out for {Route}.", route);
            return null;
        }
    }

    private Uri Url(string path) => new($"{_options.BaseUrl}/{path}");

    /// <summary>The route in a log line: the resource type, never the id the visitor sent.</summary>
    private static string RouteOf(string path)
    {
        var parts = path.Split('/');
        return parts.Length >= 3 ? $"{parts[0]}/{parts[1]}/{parts[2]}/{{id}}" : path;
    }

    private static Result<VerifiedBilling> Unexpected(string what)
    {
        return Result<VerifiedBilling>.Failure($"PayPal answered something unexpected: {what}.", BillingFailureCodes.BillingUnavailable);
    }

    private static Result<T> Unavailable<T>(string message) => Result<T>.Failure(message, BillingFailureCodes.BillingUnavailable);

    private static string? Text(JsonElement element, params string[] path)
    {
        var value = Walk(element, path);
        return value is { ValueKind: JsonValueKind.String } ? value.Value.GetString() : null;
    }

    private static decimal? Number(JsonElement element, params string[] path)
    {
        var value = Walk(element, path);
        if (value is null) return null;
        return value.Value.ValueKind switch
        {
            JsonValueKind.String when decimal.TryParse(value.Value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var s) => s,
            JsonValueKind.Number when value.Value.TryGetDecimal(out var n) => n,
            _ => null,
        };
    }

    private static DateTimeOffset? Time(JsonElement element, params string[] path)
    {
        var text = Text(element, path);
        return text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.ToUniversalTime() : null;
    }

    private static JsonElement? Walk(JsonElement element, string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return null;
        }

        return current;
    }
}
