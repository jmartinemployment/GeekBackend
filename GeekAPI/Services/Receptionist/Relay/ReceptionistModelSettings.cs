using GeekAPI.Services.Social.LanguageModel;
using GeekApplication.Models.Social;

namespace GeekAPI.Services.Receptionist.Relay;

/// <summary>
/// The model a voice call is answered with: GeekAPI's configured default LLM provider. <c>LlmProviders__DefaultProvider</c> names it
/// and that provider's own <c>LlmProviders__{Section}__ApiKey</c> and <c>__Model</c> supply its key and model, read the way
/// GeekSocial reads them (<see cref="SocialModelSettings"/>), so one setting chooses the provider for every feature. No provider and
/// no model is a default: anything unset, a value that names no provider, or a provider without a receptionist adapter here means
/// not configured, and <see cref="Reason"/> says which.
/// </summary>
public sealed class ReceptionistModelSettings
{
    /// <summary>The providers <see cref="LlmReceptionistModel"/> can call.</summary>
    public static readonly IReadOnlyList<string> Supported = [SocialModelProviders.Anthropic, SocialModelProviders.OpenAi, SocialModelProviders.Groq];

    private ReceptionistModelSettings(SocialModelSettings? model, string reason)
    {
        Model = model;
        Reason = reason;
    }

    /// <summary>The provider's settings when configured; null otherwise.</summary>
    public SocialModelSettings? Model { get; }

    public bool IsConfigured => Model is not null;

    /// <summary>"configured", or what is missing. Names settings only, never their values.</summary>
    public string Reason { get; }

    public static ReceptionistModelSettings FromEnvironment() => From(Environment.GetEnvironmentVariable);

    /// <summary>Reads the settings through <paramref name="read"/>, so a test supplies values without touching the environment.</summary>
    public static ReceptionistModelSettings From(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        SocialModelSettings settings;
        try
        {
            settings = SocialModelSettings.From(read);
        }
        catch (InvalidOperationException ex)
        {
            return new ReceptionistModelSettings(null, ex.Message);
        }

        if (settings.Provider is null)
            return new ReceptionistModelSettings(null, $"{SocialModelSettings.ProviderVariable} is not set.");
        if (!Supported.Contains(settings.Provider))
            return new ReceptionistModelSettings(null,
                $"{SocialModelSettings.ProviderVariable} names {settings.Provider}, which the receptionist cannot call. It can call: {string.Join(", ", Supported)}.");
        if (settings.ApiKey.Length == 0)
            return new ReceptionistModelSettings(null, $"{settings.KeyVariable} is not set.");
        if (settings.Model.Length == 0)
            return new ReceptionistModelSettings(null, $"{settings.ModelVariable} is not set.");

        return new ReceptionistModelSettings(settings, "configured");
    }
}
