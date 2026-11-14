using GeekApplication.Models.Voice;

namespace GeekAPI.Services.Voice.Billing;

/// <summary>One tier of the subscription schedule: its monthly price, the minutes it includes and the rate for each minute above them.</summary>
public sealed record VoiceTier(string Code, decimal MonthlyPriceUsd, int IncludedMinutes, decimal OverageRateUsd);

/// <summary>
/// The exact four-tier schedule of docs/Geek-Receptionist.md section 5, item 2. A price here is what PayPal's plan must charge, and
/// what is stored on the tenant's subscription: a tenant's price never comes from the request.
/// </summary>
public static class VoiceTierSchedule
{
    public const decimal OverageRateUsd = 0.50m;

    public static readonly IReadOnlyList<VoiceTier> All =
    [
        new(VoiceSubscriptionTiers.Starter, 30.00m, 90, OverageRateUsd),
        new(VoiceSubscriptionTiers.Standard, 48.00m, 250, OverageRateUsd),
        new(VoiceSubscriptionTiers.Pro, 148.00m, 1000, OverageRateUsd),
        new(VoiceSubscriptionTiers.Premium, 298.00m, 2000, OverageRateUsd),
    ];

    /// <summary>The tier with this exact code, or null when there is none. A code is matched exactly, never by case.</summary>
    public static VoiceTier? Find(string? code) =>
        All.FirstOrDefault(tier => string.Equals(tier.Code, code, StringComparison.Ordinal));
}
