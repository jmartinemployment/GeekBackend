namespace GeekAPI.Services.Voice.Telephony;

/// <summary>
/// The parent Twilio account and GeekAPI's public address (Railway variables TWILIO_MASTER_ACCOUNT_SID, TWILIO_MASTER_AUTH_TOKEN,
/// PUBLIC_BASE_URL). They are read once and checked when a signup is provisioned, not at startup: an unset variable fails that
/// signup with a logged error and a 503, and does not stop the rest of GeekAPI from booting. PUBLIC_BASE_URL is needed only for a
/// tenant that turned SMS notifications on, because it is the address Twilio posts that tenant's inbound texts to.
/// </summary>
public sealed record TwilioOptions(string? MasterAccountSid, string? MasterAuthToken, string? PublicBaseUrl)
{
    public static TwilioOptions FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static TwilioOptions From(Func<string, string?> read) =>
        new(read("TWILIO_MASTER_ACCOUNT_SID")?.Trim(), read("TWILIO_MASTER_AUTH_TOKEN")?.Trim(), read("PUBLIC_BASE_URL")?.Trim().TrimEnd('/'));

    /// <summary>The names of the variables that are unset or blank, in the order the plan lists them.</summary>
    public IReadOnlyList<string> Missing(bool smsEnabled)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(MasterAccountSid)) missing.Add("TWILIO_MASTER_ACCOUNT_SID");
        if (string.IsNullOrWhiteSpace(MasterAuthToken)) missing.Add("TWILIO_MASTER_AUTH_TOKEN");
        if (smsEnabled && string.IsNullOrWhiteSpace(PublicBaseUrl)) missing.Add("PUBLIC_BASE_URL");
        return missing;
    }
}
