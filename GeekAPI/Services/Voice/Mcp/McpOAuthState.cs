using System.Globalization;
using System.Text.Json;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>
/// The state value that travels to Google and back, and the only thing that ties the callback to a tenant: the tenant, the provider and
/// an expiry, sealed with <see cref="VoiceCredentialProtector"/> (AES-256-GCM). A value that was not sealed by this service, was
/// altered, belongs to a different provider or is older than <see cref="Lifetime"/> is refused, and the refusal never says which.
/// </summary>
public sealed class McpOAuthState(VoiceCredentialProtector protector, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private sealed record Payload(Guid Tenant, string Provider, long Expires);

    public Result<string> Create(Guid tenantId, string provider)
    {
        var expires = clock.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        var json = JsonSerializer.Serialize(new Payload(tenantId, provider, expires));
        return protector.Protect(json);
    }

    /// <summary>
    /// The tenant the state was made for. A key that is unusable comes back as the protector's own code, so it is a 503; a value this
    /// key did not seal is a bad state (400), which the protector alone cannot say, so the key is asked.
    /// </summary>
    public Result<Guid> Read(string? state, string provider)
    {
        if (string.IsNullOrWhiteSpace(state))
            return Result<Guid>.Failure("The connection has no state.", McpFailureCodes.StateInvalid);

        var opened = protector.Unprotect(state);
        if (!opened.IsSuccess)
        {
            return opened.ErrorCode == VoiceCredentialProtector.EncryptionUnavailable && !protector.KeyIsUsable()
                ? Result<Guid>.Failure(opened.Error!, opened.ErrorCode)
                : Result<Guid>.Failure("The connection state is not valid.", McpFailureCodes.StateInvalid);
        }

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(opened.Value!);
        }
        catch (JsonException)
        {
            return Result<Guid>.Failure("The connection state is not valid.", McpFailureCodes.StateInvalid);
        }

        if (payload is null || payload.Tenant == Guid.Empty || !string.Equals(payload.Provider, provider, StringComparison.Ordinal))
            return Result<Guid>.Failure("The connection state is not valid.", McpFailureCodes.StateInvalid);

        if (clock.GetUtcNow().ToUnixTimeSeconds() > payload.Expires)
        {
            return Result<Guid>.Failure(
                string.Create(CultureInfo.InvariantCulture, $"The connection was started more than {Lifetime.TotalMinutes:0} minutes ago. Start it again."),
                McpFailureCodes.StateExpired);
        }

        return Result<Guid>.Success(payload.Tenant);
    }
}
