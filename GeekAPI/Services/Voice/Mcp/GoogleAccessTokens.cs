using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>
/// A tenant's live Google access token for one connected provider, renewed when it is close to expiring and the renewed one saved.
///
/// One implementation for every Google connection a tenant makes — its calendar, and the Drive folder its call records go to. The
/// token endpoint and the OAuth client are the same for both, and two copies of "renew it if it is nearly out" is the drift this
/// codebase has been bitten by before. Tokens are unsealed here and never logged.
/// </summary>
public sealed class GoogleAccessTokens(
    IVoiceStore store,
    VoiceCredentialProtector protector,
    GoogleCalendarOptions options,
    GoogleCalendarOAuth google,
    TimeProvider clock,
    ILogger<GoogleAccessTokens> logger)
{
    /// <summary>A token this close to expiring is renewed before it is used, rather than failing a call mid-way.</summary>
    public static readonly TimeSpan RenewWithin = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The token to call <paramref name="provider"/> with for this tenant. <paramref name="notConnectedCode"/> is the failure code for
    /// a tenant that has not connected this provider, or whose connection is switched off, so each caller can say so in its own terms.
    /// </summary>
    public async Task<Result<string>> ForAsync(Guid tenantId, string provider, string notConnectedCode, CancellationToken ct = default)
    {
        var connection = await store.GetMcpConnectionAsync(tenantId, provider, ct).ConfigureAwait(false);
        if (!connection.IsSuccess)
        {
            if (connection.ErrorCode == VoiceFailureCodes.NotFound)
                return Result<string>.Failure($"This tenant has no {provider} connection.", notConnectedCode);
            return Result<string>.Failure(connection.Error ?? "The connection could not be read.", connection.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        }
        if (!connection.Value!.IsActive)
            return Result<string>.Failure($"This tenant's {provider} connection is not active.", notConnectedCode);

        var access = protector.Unprotect(connection.Value.OauthAccessTokenEncrypted, VoiceSecretPurposes.McpAccessToken);
        if (!access.IsSuccess) return Result<string>.Failure(access.Error!, access.ErrorCode!);

        var expiresAt = connection.Value.TokenExpiresAt;
        if (expiresAt is not null && expiresAt.Value - clock.GetUtcNow() > RenewWithin)
            return Result<string>.Success(access.Value!);

        return await RenewAsync(tenantId, provider, connection.Value, ct).ConfigureAwait(false);
    }

    private async Task<Result<string>> RenewAsync(Guid tenantId, string provider, VoiceMcpConnection connection, CancellationToken ct)
    {
        var missing = options.MissingForRefresh();
        if (missing.Count > 0)
        {
            logger.LogError("A {Provider} token cannot be renewed: {Variables} not set.", provider, string.Join(", ", missing));
            return Result<string>.Failure("The Google connection is not configured.", McpFailureCodes.OAuthNotConfigured);
        }

        var refresh = protector.Unprotect(connection.OauthRefreshTokenEncrypted, VoiceSecretPurposes.McpRefreshToken);
        if (!refresh.IsSuccess) return Result<string>.Failure(refresh.Error!, refresh.ErrorCode!);

        var renewed = await google.RefreshAsync(refresh.Value!, ct).ConfigureAwait(false);
        if (!renewed.IsSuccess) return Result<string>.Failure(renewed.Error!, renewed.ErrorCode!);

        var sealedAccess = protector.Protect(renewed.Value!.AccessToken, VoiceSecretPurposes.McpAccessToken);
        if (!sealedAccess.IsSuccess) return Result<string>.Failure(sealedAccess.Error!, sealedAccess.ErrorCode!);

        var saved = await store.SaveMcpConnectionAsync(
            tenantId,
            provider,
            new SaveVoiceMcpConnectionCommand(sealedAccess.Value!, connection.OauthRefreshTokenEncrypted, renewed.Value.ExpiresAt, connection.ExternalCalendarId),
            ct).ConfigureAwait(false);
        if (!saved.IsSuccess)
            return Result<string>.Failure(saved.Error ?? "The renewed token could not be saved.", saved.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);

        logger.LogInformation("Renewed the {Provider} access token of tenant {TenantId}.", provider, tenantId);
        return Result<string>.Success(renewed.Value.AccessToken);
    }
}
