namespace GeekAPI.Services.Voice.Telephony;

/// <summary>
/// Retell's API key, the live-call model and GeekAPI's public address (Railway variables RETELL_API_KEY, RETELL_LLM_MODEL,
/// PUBLIC_BASE_URL). They are read once and checked when a signup is activated, not at startup: an unset variable fails that
/// signup with a logged error and a 503, and does not stop the rest of GeekAPI from booting. RETELL_LLM_MODEL has no default in
/// code; it is the model Retell runs every tenant's agent on. PUBLIC_BASE_URL is the address Retell posts each tenant's call events to.
/// </summary>
public sealed record RetellOptions(string? ApiKey, string? LlmModel, string? PublicBaseUrl)
{
    public static RetellOptions FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static RetellOptions From(Func<string, string?> read) =>
        new(read("RETELL_API_KEY")?.Trim(), read("RETELL_LLM_MODEL")?.Trim(), read("PUBLIC_BASE_URL")?.Trim().TrimEnd('/'));

    /// <summary>The names of the variables that are unset or blank, in the order the plan lists them.</summary>
    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add("RETELL_API_KEY");
        if (string.IsNullOrWhiteSpace(LlmModel)) missing.Add("RETELL_LLM_MODEL");
        if (string.IsNullOrWhiteSpace(PublicBaseUrl)) missing.Add("PUBLIC_BASE_URL");
        return missing;
    }
}
