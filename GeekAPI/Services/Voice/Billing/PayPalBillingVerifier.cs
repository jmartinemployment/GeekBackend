using System.Globalization;
using System.Text.Json;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

using static GeekAPI.Services.Voice.Billing.PayPalJson;

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
    /// <summary>PayPal did not take an owner's payment, or took one that is not this charge's.</summary>
    public const string PaymentNotCompleted = "payment_not_completed";
    /// <summary>The owner asked to pay an overage that is not owed: already paid, or not theirs.</summary>
    public const string ChargeNotOwed = "charge_not_owed";
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
    private readonly PayPalRest _rest;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalBillingVerifier> _logger;

    public PayPalBillingVerifier(HttpClient http, PayPalOptions options, ILogger<PayPalBillingVerifier> logger)
    {
        _rest = new PayPalRest(http, options, logger);
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

        var token = await _rest.GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (!token.IsSuccess) return Result<VerifiedBilling>.Failure(token.Error!, token.ErrorCode!);

        var subscription = await _rest.GetJsonAsync($"v1/billing/subscriptions/{Uri.EscapeDataString(subscriptionId)}", token.Value!, BillingFailureCodes.SubscriptionNotFound, "That PayPal subscription was not found.", ct).ConfigureAwait(false);
        if (!subscription.IsSuccess) return Result<VerifiedBilling>.Failure(subscription.Error!, subscription.ErrorCode!);
        using var subscriptionDoc = subscription.Value!;
        var sub = subscriptionDoc.RootElement;

        if (Text(sub, "status") != VoiceBillingStatuses.Active)
            return Result<VerifiedBilling>.Failure("The PayPal subscription is not active.", BillingFailureCodes.SubscriptionNotActive);

        var paidAt = Time(sub, "billing_info", "last_payment", "time");
        var nextBilling = Time(sub, "billing_info", "next_billing_time");
        if (paidAt is null || nextBilling is null || nextBilling <= paidAt)
            return Result<VerifiedBilling>.Failure("The PayPal subscription has no paid period yet.", BillingFailureCodes.SubscriptionUnpaid);

        var planId = Text(sub, "plan_id");
        if (string.IsNullOrWhiteSpace(planId))
            return Unexpected("the subscription named no plan");

        var plan = await _rest.GetJsonAsync($"v1/billing/plans/{Uri.EscapeDataString(planId)}", token.Value!, BillingFailureCodes.PlanMismatch, "The PayPal plan was not found.", ct).ConfigureAwait(false);
        if (!plan.IsSuccess) return Result<VerifiedBilling>.Failure(plan.Error!, plan.ErrorCode!);
        using var planDoc = plan.Value!;

        var planProblem = CheckPlan(planDoc.RootElement, tier);
        if (planProblem is not null)
            return Result<VerifiedBilling>.Failure(planProblem, BillingFailureCodes.PlanMismatch);

        var vault = await _rest.GetJsonAsync($"v3/vault/payment-tokens/{Uri.EscapeDataString(vaultTokenId)}", token.Value!, BillingFailureCodes.VaultTokenInvalid, "That PayPal vault token was not found.", ct).ConfigureAwait(false);
        if (!vault.IsSuccess) return Result<VerifiedBilling>.Failure(vault.Error!, vault.ErrorCode!);
        using var vaultDoc = vault.Value!;
        if (!vaultDoc.RootElement.TryGetProperty("payment_source", out var source)
            || source.ValueKind != JsonValueKind.Object
            || !source.TryGetProperty("paypal", out var wallet)
            || wallet.ValueKind != JsonValueKind.Object)
            return Result<VerifiedBilling>.Failure("The vault token is not a PayPal account.", BillingFailureCodes.VaultTokenInvalid);

        return Result<VerifiedBilling>.Success(new VerifiedBilling(tier, subscriptionId, vaultTokenId, paidAt.Value, nextBilling.Value, VoiceBillingStatuses.Active));
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

    private static Result<VerifiedBilling> Unexpected(string what)
    {
        return Result<VerifiedBilling>.Failure($"PayPal answered something unexpected: {what}.", BillingFailureCodes.BillingUnavailable);
    }
}
