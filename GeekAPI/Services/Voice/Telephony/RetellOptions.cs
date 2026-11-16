namespace GeekAPI.Services.Voice.Telephony;

/// <summary>
/// Retell's API key, the live-call model, GeekAPI's public address and the key Retell signs call events with (Railway variables
/// RETELL_API_KEY, RETELL_LLM_MODEL, PUBLIC_BASE_URL, RETELL_WEBHOOK_SECRET). They are read once and checked when a signup is activated, not at startup: an unset variable fails that
/// signup with a logged error and a 503, and does not stop the rest of GeekAPI from booting. RETELL_LLM_MODEL has no default in
/// code; it is the model Retell runs every tenant's agent on. PUBLIC_BASE_URL is the address Retell posts each tenant's call events to.
/// RETELL_WEBHOOK_SECRET is the Retell API key that carries the Webhook badge in Retell's dashboard, which is the only key Retell signs
/// deliveries with; it is a different key from RETELL_API_KEY when the account has more than one (checked 2026-11-16: it does).
/// </summary>
public sealed record RetellOptions(string? ApiKey, string? LlmModel, string? PublicBaseUrl, string? WebhookKey)
{
    public static RetellOptions FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static RetellOptions From(Func<string, string?> read) =>
        new(read("RETELL_API_KEY")?.Trim(), read("RETELL_LLM_MODEL")?.Trim(), read("PUBLIC_BASE_URL")?.Trim().TrimEnd('/'), read("RETELL_WEBHOOK_SECRET")?.Trim());

    /// <summary>The names of the variables that are unset or blank, in the order the plan lists them.</summary>
    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add("RETELL_API_KEY");
        if (string.IsNullOrWhiteSpace(LlmModel)) missing.Add("RETELL_LLM_MODEL");
        if (string.IsNullOrWhiteSpace(PublicBaseUrl)) missing.Add("PUBLIC_BASE_URL");
        if (string.IsNullOrWhiteSpace(WebhookKey)) missing.Add("RETELL_WEBHOOK_SECRET");
        return missing;
    }
}
