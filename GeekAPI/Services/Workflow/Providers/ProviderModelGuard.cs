namespace GeekAPI.Services.Workflow.Providers;

/// <summary>
/// The model a provider was told to use, or a refusal naming the variable to set.
/// </summary>
/// <remarks>
/// <para>
/// Lives here rather than three times over: the three providers each resolve a model and each had to
/// decide what an unset one means, and three copies of that decision is how one of them ends up
/// defaulting while the others refuse.
/// </para>
/// <para>
/// Refusing is the point. The model defaults were removed from <see cref="LlmProvidersOptions"/> because
/// a model id in source is a model nobody chose -- delete the variable and generation keeps working,
/// billed against whatever was typed when the file was written. Groq's default had already gone stale
/// against a model its API retired. <c>ContentProviderFactory</c> makes the same argument for providers:
/// "silently substituting one is how every create ends up billed against a model nobody chose."
/// </para>
/// </remarks>
internal static class ProviderModelGuard
{
    /// <summary>
    /// <paramref name="model"/> when it is set, otherwise throws naming <paramref name="settingName"/>.
    /// Whitespace counts as unset, the same rule the API keys use -- a variable set to "" is the shape
    /// that has caused two auth outages here.
    /// </summary>
    internal static string Require(string? model, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(model)) return model;

        throw new ContentGenerationException(
            $"No model is configured for this provider. Set {settingName}. Nothing was sent and no "
            + "request was billed.");
    }
}
