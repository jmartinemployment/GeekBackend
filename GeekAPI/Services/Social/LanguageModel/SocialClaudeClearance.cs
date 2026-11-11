using GeekApplication.Models.Social;

namespace GeekAPI.Services.Social.LanguageModel;

/// <summary>
/// Which platforms the operator has cleared to send another person's comment text to Claude. A comment
/// is platform data, and whether it may leave for Claude is set by each platform's terms (open items M4
/// and I6 for Meta, L5 for LinkedIn, in docs/PLATFORM-CAPABILITIES.md), not by code. Jeff records a
/// clearance by naming the platform in <see cref="Variable"/> on GeekAPI.
/// </summary>
/// <remarks>
/// Unset or empty clears nothing, so every comment is held as blocked_no_clearance and no comment text
/// leaves the service. A name that is not a <see cref="SocialPlatforms"/> value throws when the service
/// starts: a typo must not quietly clear nothing, or the wrong platform.
/// </remarks>
public sealed class SocialClaudeClearance
{
    public const string Variable = "GEEK_SOCIAL_CLAUDE_CLEARED_PLATFORMS";

    private readonly HashSet<string> _cleared;

    private SocialClaudeClearance(HashSet<string> cleared) => _cleared = cleared;

    /// <summary>The cleared platforms, in the order <see cref="SocialPlatforms.All"/> lists them.</summary>
    public IReadOnlyList<string> Cleared => SocialPlatforms.All.Where(_cleared.Contains).ToArray();

    public bool IsCleared(string? platform) => platform is not null && _cleared.Contains(platform);

    public static SocialClaudeClearance FromEnvironment() => From(Environment.GetEnvironmentVariable);

    /// <summary>Reads <see cref="Variable"/> through <paramref name="read"/>, so a test supplies a value without touching the environment.</summary>
    public static SocialClaudeClearance From(Func<string, string?> read)
    {
        var raw = read(Variable);
        var cleared = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw))
            return new SocialClaudeClearance(cleared);

        foreach (var entry in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!SocialPlatforms.IsKnown(entry))
            {
                throw new InvalidOperationException(
                    $"{Variable} names '{entry}', which is not a platform. Use a comma list of: {string.Join(", ", SocialPlatforms.All)}.");
            }

            cleared.Add(entry);
        }

        return new SocialClaudeClearance(cleared);
    }
}
