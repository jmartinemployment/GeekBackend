namespace GeekAPI.Services.Voice.Billing;

/// <summary>
/// PayPal's REST host and app credentials (Railway variables PAYPAL_BASE_URL, PAYPAL_CLIENT_ID, PAYPAL_CLIENT_SECRET). They are read
/// once and checked when a verification needs them, not at startup: an unset variable stops the first signup from being activated with
/// a logged error and a 503, and does not stop the rest of GeekAPI from booting.
/// </summary>
public sealed record PayPalOptions(string? BaseUrl, string? ClientId, string? ClientSecret)
{
    public static PayPalOptions FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static PayPalOptions From(Func<string, string?> read) =>
        new(read("PAYPAL_BASE_URL")?.Trim().TrimEnd('/'), read("PAYPAL_CLIENT_ID")?.Trim(), read("PAYPAL_CLIENT_SECRET")?.Trim());

    /// <summary>The names of the variables that are unset or blank, in the order the plan lists them.</summary>
    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(BaseUrl)) missing.Add("PAYPAL_BASE_URL");
        if (string.IsNullOrWhiteSpace(ClientId)) missing.Add("PAYPAL_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(ClientSecret)) missing.Add("PAYPAL_CLIENT_SECRET");
        return missing;
    }
}
