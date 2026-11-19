namespace GeekAPI.Services.Voice.Email;

/// <summary>
/// The transactional email provider (Railway variables RESEND_API_KEY and RECEPTIONIST_EMAIL_FROM, plus the optional
/// RECEPTIONIST_EMAIL_FROM_NAME). They are read once and checked when an email is sent, not at startup: an unset variable
/// fails that send with a logged error and an <c>email_not_configured</c> result, and does not stop the rest of GeekAPI
/// from booting. <see cref="ApiKey"/> is a secret and is never logged.
/// </summary>
public sealed record EmailOptions(string? ApiKey, string? FromAddress, string? FromName)
{
    public static EmailOptions FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static EmailOptions From(Func<string, string?> read) =>
        new(read("RESEND_API_KEY")?.Trim(), read("RECEPTIONIST_EMAIL_FROM")?.Trim(), read("RECEPTIONIST_EMAIL_FROM_NAME")?.Trim());

    /// <summary>The names of the required variables that are unset or blank; the from-name is optional and never listed.</summary>
    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add("RESEND_API_KEY");
        if (string.IsNullOrWhiteSpace(FromAddress)) missing.Add("RECEPTIONIST_EMAIL_FROM");
        return missing;
    }

    /// <summary>The From header the provider is given: "Name &lt;address&gt;" when a name is set, the bare address otherwise.</summary>
    public string FromHeader() =>
        string.IsNullOrWhiteSpace(FromName) ? FromAddress! : $"{FromName} <{FromAddress}>";
}
