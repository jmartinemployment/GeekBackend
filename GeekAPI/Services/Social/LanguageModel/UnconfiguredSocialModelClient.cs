using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.LanguageModel;

/// <summary>
/// Registered when <see cref="SocialModelSettings.ProviderVariable"/> is not set. GeekSocial has no
/// default provider, so every call fails not_configured, names the setting, and sends nothing; the
/// rest of GeekAPI, which does not use this client, keeps running.
/// </summary>
public sealed class UnconfiguredSocialModelClient : ISocialModelClient
{
    private readonly ILogger<UnconfiguredSocialModelClient> _logger;

    public UnconfiguredSocialModelClient(ILogger<UnconfiguredSocialModelClient> logger)
    {
        _logger = logger;
    }

    public string? Provider => null;

    public string? Model => null;

    public Task<Result<SocialModelAnswer>> AnswerAsync(SocialModelRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The reason reaches the post form, so it names no setting; the log line names it for the operator.
        const string reason = "No AI provider is set for GeekSocial on GeekAPI. Nothing was sent.";
        _logger.LogError("Social model call {Purpose} failed ({Code}): {Reason} Set {Setting} to one of: {Providers}.",
            string.IsNullOrWhiteSpace(request.Purpose) ? "unnamed" : request.Purpose.Trim(), SocialFailureCodes.NotConfigured, reason,
            SocialModelSettings.ProviderVariable, string.Join(", ", SocialModelProviders.All));
        return Task.FromResult(Result<SocialModelAnswer>.Failure(reason, SocialFailureCodes.NotConfigured));
    }
}
