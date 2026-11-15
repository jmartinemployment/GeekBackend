using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>
/// Connects a tenant's calendar (docs/Geek-Receptionist.md sections 6 and 7). Begin makes the address the tenant's owner is sent to.
/// Google sends the owner's browser back to the portal, and the portal calls Complete with the owner's own token, so Complete knows who
/// is approving: it refuses unless that tenant is the one sealed in the state, which is what stops someone from getting a stranger to
/// approve a connection that was started for another tenant. It checks the state before it spends Google's one-time code,
/// encrypts both tokens with <see cref="VoiceCredentialProtector"/> and hands only ciphertext to GeekRepository. Only google_calendar
/// is built: any other provider is refused (400) and never quietly treated as Google.
/// </summary>
public sealed class McpConnectionService(
    IVoiceStore store,
    GoogleCalendarOptions options,
    GoogleCalendarOAuth google,
    McpOAuthState state,
    VoiceCredentialProtector protector,
    ILogger<McpConnectionService> logger)
{
    public async Task<Result<string>> BeginAsync(Guid tenantId, string provider, CancellationToken ct = default)
    {
        var supported = CheckProvider(provider);
        if (supported is not null) return Result<string>.Failure(supported, McpFailureCodes.ProviderNotSupported);

        var configured = CheckConfigured();
        if (configured is not null) return Result<string>.Failure(configured, McpFailureCodes.OAuthNotConfigured);

        var tenant = await store.GetTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (!tenant.IsSuccess) return Result<string>.Failure(tenant.Error ?? "The tenant could not be read.", tenant.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        if (tenant.Value!.Status != VoiceTenantStatuses.Active)
            return Result<string>.Failure("A calendar can be connected only after the subscription is active.", McpFailureCodes.TenantNotActive);

        var sealedState = state.Create(tenantId, provider);
        if (!sealedState.IsSuccess) return Result<string>.Failure(sealedState.Error!, sealedState.ErrorCode!);

        return Result<string>.Success(google.AuthorizationUrl(sealedState.Value!));
    }

    /// <summary>
    /// <paramref name="callerTenantId"/> is the tenant in the owner's verified token. <paramref name="error"/> is Google's own error
    /// parameter, present when the person declined.
    /// </summary>
    public async Task<Result<Guid>> CompleteAsync(Guid callerTenantId, string provider, string? code, string? sealedState, string? error, CancellationToken ct = default)
    {
        var supported = CheckProvider(provider);
        if (supported is not null) return Result<Guid>.Failure(supported, McpFailureCodes.ProviderNotSupported);

        var configured = CheckConfigured();
        if (configured is not null) return Result<Guid>.Failure(configured, McpFailureCodes.OAuthNotConfigured);

        var tenantId = state.Read(sealedState, provider);
        if (!tenantId.IsSuccess) return Result<Guid>.Failure(tenantId.Error!, tenantId.ErrorCode!);
        if (tenantId.Value != callerTenantId)
        {
            logger.LogWarning("A {Provider} approval was started for tenant {StateTenant} and finished by tenant {CallerTenant}.", provider, tenantId.Value, callerTenantId);
            return Result<Guid>.Failure("That approval was started for a different account.", McpFailureCodes.TenantForbidden);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            logger.LogWarning("A tenant declined the {Provider} connection: {GoogleError}.", provider, error.Length > 64 ? error[..64] : error);
            return Result<Guid>.Failure("The calendar connection was declined.", McpFailureCodes.AuthorizationDenied);
        }

        if (string.IsNullOrWhiteSpace(code))
            return Result<Guid>.Failure("Google sent no authorization code.", VoiceFailureCodes.InvalidRequest);

        var tokens = await google.ExchangeCodeAsync(code, ct).ConfigureAwait(false);
        if (!tokens.IsSuccess) return Result<Guid>.Failure(tokens.Error!, tokens.ErrorCode!);

        var access = protector.Protect(tokens.Value!.AccessToken, VoiceSecretPurposes.McpAccessToken);
        if (!access.IsSuccess) return Result<Guid>.Failure(access.Error!, access.ErrorCode!);
        var refresh = protector.Protect(tokens.Value.RefreshToken, VoiceSecretPurposes.McpRefreshToken);
        if (!refresh.IsSuccess) return Result<Guid>.Failure(refresh.Error!, refresh.ErrorCode!);

        var saved = await store.SaveMcpConnectionAsync(
            tenantId.Value,
            provider,
            new SaveVoiceMcpConnectionCommand(access.Value!, refresh.Value!, tokens.Value.ExpiresAt, GoogleCalendarOptions.PrimaryCalendarId),
            ct).ConfigureAwait(false);
        if (!saved.IsSuccess)
            return Result<Guid>.Failure(saved.Error ?? "The connection could not be saved.", saved.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);

        logger.LogInformation("Tenant {TenantId} connected {Provider}.", tenantId.Value, provider);
        return Result<Guid>.Success(tenantId.Value);
    }

    private static string? CheckProvider(string provider) =>
        string.Equals(provider, GoogleCalendarOptions.Provider, StringComparison.Ordinal)
            ? null
            : $"Only {GoogleCalendarOptions.Provider} can be connected so far.";

    private string? CheckConfigured()
    {
        var missing = options.MissingForOAuth();
        if (missing.Count == 0) return null;
        logger.LogError("Calendar connection cannot start: {Variables} not set.", string.Join(", ", missing));
        return "Calendar connection is not configured.";
    }
}
