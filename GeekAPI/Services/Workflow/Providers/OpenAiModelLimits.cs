namespace GeekAPI.Services.Workflow.Providers;

/// <summary>
/// What each OpenAI model accepts in one request, in tokens: what is sent and the room reserved for
/// the reply, together. Counted on each request alone, never across a page or a run.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a table in code, and why in a file of its own.</b> Until 2026-10-12 no code knew a model's
/// limit. On 2026-10-10 a pillar and a blog were each sent as 141,637 tokens to a model that accepts
/// 128,000, after their openings had been paid for, and neither was written. OpenAI states the figure
/// on a model's page and returns it from no endpoint, so it is written here, read from those pages on
/// 2026-10-12. It is not a
/// default: which model is used is still only what <c>LlmProviders__OpenAi__Model</c> says, and
/// <c>ProviderOptions.cs</c> still names none (<c>ProviderModelGuardTests</c>). This says what a
/// model accepts once somebody has chosen it.
/// </para>
/// <para>
/// <b>A model that is not here is refused, never guessed at.</b> A guess that is too high is the
/// failure above; one that is too low leaves research out for no reason. Naming a new model in
/// <c>LlmProviders__OpenAi__Model</c> therefore needs its line here first, and the refusal says so.
/// </para>
/// <para>
/// Matched from the start of the name, so a dated snapshot ("gpt-4o-2024-08-06") and a smaller model
/// of the same family ("gpt-4.1-mini", "gpt-4o-mini") are the family's: each accepts what its family
/// does. Every model here counts tokens the same way, which is the way
/// <c>GccRequestSize</c> counts; a model that counts otherwise needs its own counter as well as its
/// line.
/// </para>
/// </remarks>
public static class OpenAiModelLimits
{
    private static readonly (string Family, int RequestTokens)[] Families =
    [
        ("gpt-4.1", 1_047_576),
        ("gpt-4o", 128_000),
    ];

    /// <summary>What <paramref name="model"/> accepts in one request, or null when it is not a model listed here.</summary>
    public static int? RequestTokensFor(string? model)
    {
        var name = (model ?? string.Empty).Trim();
        foreach (var (family, requestTokens) in Families)
        {
            if (name.Equals(family, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(family + "-", StringComparison.OrdinalIgnoreCase))
            {
                return requestTokens;
            }
        }

        return null;
    }

    /// <summary>The refusal for a model that is not listed: what was named, and what is.</summary>
    public static string Refusal(string? model) =>
        $"Refused: what the model '{(string.IsNullOrWhiteSpace(model) ? "(none)" : model.Trim())}' accepts in one "
        + "request is not known to GeekAPI, so a request cannot be checked against it before it is paid "
        + $"for. Known: {string.Join(", ", Families.Select(family => family.Family))} (and the models whose "
        + "names begin with one of them). Add the model to OpenAiModelLimits, or name a known one in "
        + "LlmProviders__OpenAi__Model. Nothing was sent and no request was billed.";
}
