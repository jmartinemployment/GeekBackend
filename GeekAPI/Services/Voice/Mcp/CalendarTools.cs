using System.Text.Json;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>
/// Runs one calendar tool for one tenant (docs/Geek-Receptionist.md sections 7 and 8). It reads the tenant's stored connection, decrypts
/// the access token, renews it first when it has lapsed or lapses within <see cref="RenewWithin"/> (saving the new access token and its
/// expiry, and leaving the refresh token as it was), and hands the token to the gateway for that one call. A token the gateway still
/// refuses is reauthorization_required: it is not renewed a second time and the call is not repeated. A tenant with no active
/// connection is calendar_not_connected (404). Nothing is retried, and no log line carries a token, an argument or a tool's content.
/// </summary>
public sealed class CalendarTools(
    IVoiceStore store,
    VoiceCredentialProtector protector,
    GoogleCalendarOptions options,
    GoogleCalendarOAuth google,
    McpGatewayClient gateway,
    TimeProvider clock,
    ILogger<CalendarTools> logger)
{
    public static readonly TimeSpan RenewWithin = TimeSpan.FromMinutes(2);

    public async Task<Result<McpToolResult>> CallAsync(Guid tenantId, string provider, string tool, JsonElement? arguments, CancellationToken ct = default)
    {
        if (!string.Equals(provider, GoogleCalendarOptions.Provider, StringComparison.Ordinal))
            return Result<McpToolResult>.Failure($"Only {GoogleCalendarOptions.Provider} can be used so far.", McpFailureCodes.ProviderNotSupported);

        var connection = await store.GetMcpConnectionAsync(tenantId, provider, ct).ConfigureAwait(false);
        if (!connection.IsSuccess)
        {
            if (connection.ErrorCode == VoiceFailureCodes.NotFound)
                return Result<McpToolResult>.Failure("This tenant has no calendar connected.", McpFailureCodes.CalendarNotConnected);
            return Result<McpToolResult>.Failure(connection.Error ?? "The calendar connection could not be read.", connection.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        }
        if (!connection.Value!.IsActive)
            return Result<McpToolResult>.Failure("This tenant's calendar connection is not active.", McpFailureCodes.CalendarNotConnected);

        var access = protector.Unprotect(connection.Value.OauthAccessTokenEncrypted);
        if (!access.IsSuccess) return Result<McpToolResult>.Failure(access.Error!, access.ErrorCode!);
        var accessToken = access.Value!;

        var expiresAt = connection.Value.TokenExpiresAt;
        if (expiresAt is null || expiresAt.Value - clock.GetUtcNow() <= RenewWithin)
        {
            var renewed = await RenewAsync(tenantId, provider, connection.Value, ct).ConfigureAwait(false);
            if (!renewed.IsSuccess) return Result<McpToolResult>.Failure(renewed.Error!, renewed.ErrorCode!);
            accessToken = renewed.Value!;
        }

        return await gateway.CallToolAsync(provider, accessToken, tool, arguments, ct).ConfigureAwait(false);
    }

    private async Task<Result<string>> RenewAsync(Guid tenantId, string provider, VoiceMcpConnection connection, CancellationToken ct)
    {
        var missing = options.MissingForRefresh();
        if (missing.Count > 0)
        {
            logger.LogError("A calendar token cannot be renewed: {Variables} not set.", string.Join(", ", missing));
            return Result<string>.Failure("Calendar connection is not configured.", McpFailureCodes.OAuthNotConfigured);
        }

        var refresh = protector.Unprotect(connection.OauthRefreshTokenEncrypted);
        if (!refresh.IsSuccess) return Result<string>.Failure(refresh.Error!, refresh.ErrorCode!);

        var renewed = await google.RefreshAsync(refresh.Value!, ct).ConfigureAwait(false);
        if (!renewed.IsSuccess) return Result<string>.Failure(renewed.Error!, renewed.ErrorCode!);

        var sealedAccess = protector.Protect(renewed.Value!.AccessToken);
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
