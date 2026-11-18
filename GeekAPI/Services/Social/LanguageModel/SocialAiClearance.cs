using GeekApplication.Models.Social;

namespace GeekAPI.Services.Social.LanguageModel;

/// <summary>
/// Which platforms the operator has cleared to send another person's comment text to which AI provider.
/// A comment is platform data, and whether it may leave for a given company is set by that platform's
/// terms and that provider's agreement (open items M4 and I6 for Meta, L5 for LinkedIn, in
/// docs/PLATFORM-CAPABILITIES.md), not by code. The operator records a clearance by naming the pair in
/// <see cref="Variable"/> on GeekAPI, for example <c>anthropic:facebook,openai:facebook</c>.
/// </summary>
/// <remarks>
/// A clearance belongs to one provider, so switching provider clears nothing: every comment is held as
/// blocked_no_clearance until the new provider's pair is named. Unset or empty clears nothing. An entry
/// that is not <c>provider:platform</c>, or names a provider or platform that does not exist, throws
/// when the service starts: a typo must not quietly clear nothing, or the wrong pair.
/// <see cref="RetiredVariable"/>, which cleared platforms for Claude alone before the provider was
/// replaceable, also throws when it is still set, naming its replacement, so a clearance given to one
/// company is never read as a clearance for another.
/// </remarks>
public sealed class SocialAiClearance
{
    public const string Variable = "GEEK_SOCIAL_AI_CLEARED_PLATFORMS";
    public const string RetiredVariable = "GEEK_SOCIAL_CLAUDE_CLEARED_PLATFORMS";

    private readonly HashSet<(string Provider, string Platform)> _cleared;

    private SocialAiClearance(HashSet<(string Provider, string Platform)> cleared)
    {
        _cleared = cleared;
    }

    public bool IsCleared(string? provider, string? platform) =>
        provider is not null && platform is not null && _cleared.Contains((provider, platform));

    /// <summary>The platforms cleared for <paramref name="provider"/>, in the order <see cref="SocialPlatforms.All"/> lists them.</summary>
    public IReadOnlyList<string> ClearedFor(string? provider) =>
        provider is null ? Array.Empty<string>() : SocialPlatforms.All.Where(p => _cleared.Contains((provider, p))).ToArray();

    /// <summary>Why a comment from <paramref name="platform"/> was held. It holds no comment text and names no setting.</summary>
    public static string HoldReason(string? provider, string platform) => provider is null
        ? "No AI provider is set for GeekSocial, so no comment text is sent."
        : $"{platform} is not cleared to send comment text to {SocialModelProviders.DisplayName(provider)}. Nothing was sent.";

    public static SocialAiClearance FromEnvironment() => From(Environment.GetEnvironmentVariable);

    /// <summary>Reads the clearance through <paramref name="read"/>, so a test supplies a value without touching the environment.</summary>
    public static SocialAiClearance From(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        if (!string.IsNullOrWhiteSpace(read(RetiredVariable)))
        {
            throw new InvalidOperationException(
                $"{RetiredVariable} is set, and it is no longer read: it cleared platforms for Claude alone. Remove it, and name each cleared pair in {Variable} as provider:platform (for example anthropic:facebook).");
        }

        var raw = read(Variable);
        var cleared = new HashSet<(string Provider, string Platform)>();
        if (string.IsNullOrWhiteSpace(raw))
            return new SocialAiClearance(cleared);

        foreach (var entry in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !SocialModelProviders.All.Contains(parts[0]) || !SocialPlatforms.IsKnown(parts[1]))
            {
                throw new InvalidOperationException(
                    $"{Variable} names '{entry}', which is not provider:platform. Providers: {string.Join(", ", SocialModelProviders.All)}. Platforms: {string.Join(", ", SocialPlatforms.All)}.");
            }

            cleared.Add((parts[0], parts[1]));
        }

        return new SocialAiClearance(cleared);
    }
}
