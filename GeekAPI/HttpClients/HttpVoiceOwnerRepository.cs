using System.Net;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

/// <summary>
/// /internal/v1/owner-logins and /internal/v1/owner-refresh-tokens over the GeekRepository client that sends X-Internal-Api-Key, the
/// <see cref="IVoiceOwnerStore"/> GeekAPI's owner sign-in calls. Statuses are read as <see cref="HttpVoiceRepository"/> reads them: 404 with
/// a message is not_found, 400 is invalid_request, 409 is conflict, and everything else is store_unavailable. A 409 whose body carries
/// refresh_token_reused keeps that code, so a replayed refresh token is never read as an ordinary conflict. Each call is made once. Logs
/// carry the status and the route, never a body: a body here is an email, a password hash or a token hash.
/// </summary>
public sealed class HttpVoiceOwnerRepository(HttpClient http, ILogger<HttpVoiceOwnerRepository> logger) : IVoiceOwnerStore
{
    private const int MessageLimit = 300;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public Task<Result<VoiceOwnerLogin>> CreateOwnerLoginAsync(CreateVoiceOwnerLoginCommand command, CancellationToken ct = default) =>
        SendAsync<VoiceOwnerLogin>("internal/v1/owner-logins", command, ct);

    public Task<Result<VoiceOwnerLogin>> FindOwnerLoginByEmailAsync(string email, CancellationToken ct = default) =>
        SendAsync<VoiceOwnerLogin>("internal/v1/owner-logins/find", new { email }, ct);

    public Task<Result<bool>> StoreRefreshTokenAsync(StoreVoiceRefreshTokenCommand command, CancellationToken ct = default) =>
        SendAsync<bool>("internal/v1/owner-refresh-tokens", command, ct);

    public Task<Result<ConsumedVoiceRefreshToken>> ConsumeRefreshTokenAsync(string tokenHash, CancellationToken ct = default) =>
        SendAsync<ConsumedVoiceRefreshToken>("internal/v1/owner-refresh-tokens/consume", new { tokenHash }, ct);

    public Task<Result<bool>> RevokeRefreshTokenFamilyAsync(string tokenHash, CancellationToken ct = default) =>
        SendAsync<bool>("internal/v1/owner-refresh-tokens/revoke", new { tokenHash }, ct);

    private async Task<Result<T>> SendAsync<T>(string path, object body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json"),
            };
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            return await ReadAsync<T>(response, path, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "GeekRepository unreachable for POST {Path}.", path);
            return Unavailable<T>("GeekRepository unreachable.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("GeekRepository timed out for POST {Path}.", path);
            return Unavailable<T>("GeekRepository timed out.");
        }
    }

    private async Task<Result<T>> ReadAsync<T>(HttpResponseMessage response, string path, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var text = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
            var message = text.Length == 0 ? null : text.Length <= MessageLimit ? text : text[..MessageLimit];
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound when message is not null:
                    return Result<T>.Failure(message, VoiceFailureCodes.NotFound);
                case HttpStatusCode.BadRequest:
                    return Result<T>.Failure(message ?? "GeekRepository refused the request.", VoiceFailureCodes.InvalidRequest);
                case HttpStatusCode.Conflict:
                    return CodeOf(text) == VoiceOwnerFailureCodes.RefreshTokenReused
                        ? Result<T>.Failure("That refresh token was already used.", VoiceOwnerFailureCodes.RefreshTokenReused)
                        : Result<T>.Failure(message ?? "That record already exists.", VoiceFailureCodes.Conflict);
                default:
                    // A 404 with no message is a route GeekRepository does not have, and a 401 is a key it did not accept.
                    logger.LogError("GeekRepository answered {Status} for POST {Path}.", (int)response.StatusCode, path);
                    return Unavailable<T>($"GeekRepository answered {(int)response.StatusCode}.");
            }
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct).ConfigureAwait(false);
            if (value is not null) return Result<T>.Success(value);

            logger.LogError("GeekRepository answered an empty body for POST {Path}.", path);
            return Unavailable<T>("GeekRepository answered an empty body.");
        }
        catch (JsonException)
        {
            logger.LogError("GeekRepository answered a body that did not parse for POST {Path}.", path);
            return Unavailable<T>("GeekRepository answered a body that did not parse.");
        }
    }

    /// <summary>The code in a JSON refusal ({ "error": ..., "code": ... }), or null when the body is plain text.</summary>
    private static string? CodeOf(string text)
    {
        if (!text.StartsWith('{')) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Result<T> Unavailable<T>(string message) => Result<T>.Failure(message, VoiceFailureCodes.StoreUnavailable);
}
