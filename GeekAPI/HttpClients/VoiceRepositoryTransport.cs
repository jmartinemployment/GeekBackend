using System.Net;
using System.Text;
using System.Text.Json;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

/// <summary>
/// How GeekAPI's Geek-Receptionist clients (<see cref="HttpVoiceRepository"/>, <see cref="HttpVoiceOwnerRepository"/>) call GeekRepository's
/// /internal/v1/* over the client that sends X-Internal-Api-Key, written once so the two cannot read a status differently. A status is
/// answered as a Result carrying a <see cref="VoiceFailureCodes"/> value: 404 with a message is not_found, 400 is invalid_request, 409 is
/// conflict unless its body is JSON carrying its own code (a replayed refresh token's refresh_token_reused), and everything else, an
/// unreachable or slow GeekRepository and a rejected key included, is store_unavailable. Each call is made once. Nothing calls
/// EnsureSuccessStatusCode and nothing turns a failure into an empty value, so a store that is down is never read as "nothing there".
/// Logs carry the method, the route and the status, never a body or a query string: a body here is a person's name and number, a
/// token's ciphertext, a call's transcript, an email or a hash.
/// </summary>
public sealed class VoiceRepositoryTransport(HttpClient http, ILogger logger)
{
    private const int MessageLimit = 300;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<Result<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            return await ReadAsync<T>(response, method, path, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "GeekRepository unreachable for {Method} {Path}.", method.Method, path);
            return Unavailable<T>("GeekRepository unreachable.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("GeekRepository timed out for {Method} {Path}.", method.Method, path);
            return Unavailable<T>("GeekRepository timed out.");
        }
    }

    private async Task<Result<T>> ReadAsync<T>(HttpResponseMessage response, HttpMethod method, string path, CancellationToken ct)
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
                    return CodedRefusal(text) is { } coded
                        ? Result<T>.Failure(coded.Error, coded.Code)
                        : Result<T>.Failure(message ?? "The record changed; read it again.", VoiceFailureCodes.Conflict);
                default:
                    // A 404 with no message is a route GeekRepository does not have, and a 401 is a key it did not accept:
                    // both are the store being unavailable to GeekAPI, never a missing record.
                    logger.LogError("GeekRepository answered {Status} for {Method} {Path}.", (int)response.StatusCode, method.Method, path);
                    return Unavailable<T>($"GeekRepository answered {(int)response.StatusCode}.");
            }
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct).ConfigureAwait(false);
            if (value is not null) return Result<T>.Success(value);

            logger.LogError("GeekRepository answered an empty body for {Method} {Path}.", method.Method, path);
            return Unavailable<T>("GeekRepository answered an empty body.");
        }
        catch (JsonException)
        {
            logger.LogError("GeekRepository answered a body that did not parse for {Method} {Path}.", method.Method, path);
            return Unavailable<T>("GeekRepository answered a body that did not parse.");
        }
    }

    /// <summary>The error and code of a JSON refusal ({ "error": ..., "code": ... }), or null when the body is plain text or has no code.</summary>
    private static (string Error, string Code)? CodedRefusal(string text)
    {
        if (!text.StartsWith('{')) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(code.GetString()))
                return null;
            var error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            return (string.IsNullOrWhiteSpace(error) ? "GeekRepository refused the request." : error!, code.GetString()!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Result<T> Unavailable<T>(string message) => Result<T>.Failure(message, VoiceFailureCodes.StoreUnavailable);
}
