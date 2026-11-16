using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

/// <summary>
/// /internal/v1/owner-logins and /internal/v1/owner-refresh-tokens over the GeekRepository client that sends X-Internal-Api-Key, the
/// <see cref="IVoiceOwnerStore"/> GeekAPI's owner sign-in calls. Statuses are read by <see cref="VoiceRepositoryTransport"/>, as for every
/// Geek-Receptionist client; a replayed refresh token's 409 keeps its refresh_token_reused code there, so it is never read as an ordinary
/// conflict. Each call is made once, and a body (an email, a password hash or a token hash) is never logged.
/// </summary>
public sealed class HttpVoiceOwnerRepository(HttpClient http, ILogger<HttpVoiceOwnerRepository> logger) : IVoiceOwnerStore
{
    private readonly VoiceRepositoryTransport _transport = new(http, logger);

    public Task<Result<VoiceOwnerLogin>> CreateOwnerLoginAsync(CreateVoiceOwnerLoginCommand command, CancellationToken ct = default) =>
        Post<VoiceOwnerLogin>("internal/v1/owner-logins", command, ct);

    public Task<Result<VoiceOwnerLogin>> FindOwnerLoginByEmailAsync(string email, CancellationToken ct = default) =>
        Post<VoiceOwnerLogin>("internal/v1/owner-logins/find", new { email }, ct);

    public Task<Result<bool>> UpdateOwnerPasswordHashAsync(UpdateVoiceOwnerPasswordCommand command, CancellationToken ct = default) =>
        Post<bool>("internal/v1/owner-logins/password", command, ct);

    public Task<Result<bool>> StoreRefreshTokenAsync(StoreVoiceRefreshTokenCommand command, CancellationToken ct = default) =>
        Post<bool>("internal/v1/owner-refresh-tokens", command, ct);

    public Task<Result<RotatedVoiceRefreshToken>> RotateRefreshTokenAsync(RotateVoiceRefreshTokenCommand command, CancellationToken ct = default) =>
        Post<RotatedVoiceRefreshToken>("internal/v1/owner-refresh-tokens/rotate", command, ct);

    public Task<Result<bool>> RevokeRefreshTokenFamilyAsync(string tokenHash, CancellationToken ct = default) =>
        Post<bool>("internal/v1/owner-refresh-tokens/revoke", new { tokenHash }, ct);

    // Bodies carry an email or a hash, so every call is a POST and nothing sensitive is ever in a URL.
    private Task<Result<T>> Post<T>(string path, object body, CancellationToken ct) => _transport.SendAsync<T>(HttpMethod.Post, path, body, ct);
}
