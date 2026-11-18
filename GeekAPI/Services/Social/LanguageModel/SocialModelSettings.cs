using GeekAPI.Services.Workflow.Domain.Enums;
using GeekApplication.Models.Social;

namespace GeekAPI.Services.Social.LanguageModel;

/// <summary>
/// Which AI provider GeekSocial calls, and that provider's key, model and address, read once from
/// GeekAPI's environment when the service starts.
/// </summary>
/// <remarks>
/// <para>
/// The provider is <see cref="ProviderVariable"/>, the setting Content Creator also reads, so one
/// setting chooses the provider for both. GeekSocial reads the raw variable and takes no default: the
/// "OpenAi" that <c>LlmProvidersOptions.DefaultProvider</c> falls back to when the setting is absent is
/// not used here. An unset provider registers <see cref="UnconfiguredSocialModelClient"/>, so every
/// GeekSocial model call fails not_configured and nothing is sent, while the rest of GeekAPI runs. A
/// value that names no provider throws when the service starts: a typo must not quietly call nothing,
/// or the wrong company.
/// </para>
/// <para>
/// The key and model come from the provider's own <c>LlmProviders__{Section}__ApiKey</c> and
/// <c>__Model</c>; a missing one fails each call not_configured, naming the setting. Anthropic's key
/// falls back to <see cref="AnthropicKeyVariable"/>, as it did before the provider was replaceable.
/// </para>
/// </remarks>
public sealed class SocialModelSettings
{
    public const string ProviderVariable = "LlmProviders__DefaultProvider";
    public const string AnthropicKeyVariable = "ANTHROPIC_API_KEY";
    private const int DefaultTimeoutSeconds = 120;
    private const string DefaultAnthropicVersion = "2023-06-01";

    private SocialModelSettings()
    {
    }

    /// <summary>One of <see cref="SocialModelProviders.All"/>, or null when <see cref="ProviderVariable"/> is not set.</summary>
    public string? Provider { get; private init; }

    public string ApiKey { get; private init; } = string.Empty;
    public string Model { get; private init; } = string.Empty;
    public string BaseUrl { get; private init; } = string.Empty;
    public int TimeoutSeconds { get; private init; } = DefaultTimeoutSeconds;
    public string AnthropicVersion { get; private init; } = DefaultAnthropicVersion;

    /// <summary>The setting that holds the key, named in a not_configured message.</summary>
    public string KeyVariable => Provider is null ? ProviderVariable : $"LlmProviders__{SocialModelProviders.SettingsSection(Provider)}__ApiKey";

    /// <summary>The setting that holds the model, named in a not_configured message.</summary>
    public string ModelVariable => Provider is null ? ProviderVariable : $"LlmProviders__{SocialModelProviders.SettingsSection(Provider)}__Model";

    public static SocialModelSettings FromEnvironment() => From(Environment.GetEnvironmentVariable);

    /// <summary>Reads the settings through <paramref name="read"/>, so a test supplies values without touching the environment.</summary>
    public static SocialModelSettings From(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        var raw = read(ProviderVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return new SocialModelSettings();

        var provider = ParseProvider(raw)
            ?? throw new InvalidOperationException(
                $"{ProviderVariable} is '{raw.Trim()}', which is not an AI provider GeekSocial can call. Use one of: {string.Join(", ", SocialModelProviders.All)} (any case).");

        var section = SocialModelProviders.SettingsSection(provider);
        string Setting(string name) => read($"LlmProviders__{section}__{name}")?.Trim() ?? string.Empty;

        var key = Setting("ApiKey");
        if (key.Length == 0 && provider == SocialModelProviders.Anthropic)
            key = read(AnthropicKeyVariable)?.Trim() ?? string.Empty;

        var timeoutText = Setting("TimeoutSeconds");
        var timeout = DefaultTimeoutSeconds;
        if (timeoutText.Length > 0 && (!int.TryParse(timeoutText, out timeout) || timeout <= 0))
        {
            throw new InvalidOperationException(
                $"LlmProviders__{section}__TimeoutSeconds is '{timeoutText}', which is not a positive whole number of seconds.");
        }

        var baseUrl = Setting("BaseUrl");
        var version = Setting("AnthropicVersion");
        return new SocialModelSettings
        {
            Provider = provider,
            ApiKey = key,
            Model = Setting("Model"),
            BaseUrl = baseUrl.Length > 0 ? baseUrl : DefaultBaseUrl(provider),
            TimeoutSeconds = timeout,
            AnthropicVersion = version.Length > 0 ? version : DefaultAnthropicVersion,
        };
    }

    /// <summary>
    /// The provider a setting names. Every value Content Creator's <see cref="LlmProviderType"/> accepts
    /// is accepted the same way (a name in any case, or its number), so the one setting never starts
    /// one product and stops the other; Gemini and Perplexity are GeekSocial's own additions.
    /// </summary>
    private static string? ParseProvider(string raw)
    {
        if (Enum.TryParse<LlmProviderType>(raw.Trim(), ignoreCase: true, out var shared) && Enum.IsDefined(shared))
        {
            return shared switch
            {
                LlmProviderType.OpenAi => SocialModelProviders.OpenAi,
                LlmProviderType.Anthropic => SocialModelProviders.Anthropic,
                LlmProviderType.Groq => SocialModelProviders.Groq,
                _ => throw new InvalidOperationException($"Content Creator's provider '{shared}' has no GeekSocial client."),
            };
        }

        return SocialModelProviders.Parse(raw);
    }

    /// <summary>
    /// Each provider's own address. Gemini's is the models collection: the client adds
    /// <c>/{model}:generateContent</c>. Perplexity's is the Agent API, which replaced its Sonar chat
    /// completions on 2026-09-27.
    /// </summary>
    private static string DefaultBaseUrl(string provider) => provider switch
    {
        SocialModelProviders.Anthropic => "https://api.anthropic.com/v1/messages",
        SocialModelProviders.OpenAi => "https://api.openai.com/v1/chat/completions",
        SocialModelProviders.Groq => "https://api.groq.com/openai/v1/chat/completions",
        SocialModelProviders.Gemini => "https://generativelanguage.googleapis.com/v1beta/models",
        SocialModelProviders.Perplexity => "https://api.perplexity.ai/v1/agent",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Not an AI provider GeekSocial knows."),
    };
}
