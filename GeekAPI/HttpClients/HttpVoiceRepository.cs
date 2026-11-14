using System.Net;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

/// <summary>
/// /internal/v1/* over the GeekRepository client that sends X-Internal-Api-Key, the <see cref="IVoiceStore"/> GeekAPI's
/// Geek-Receptionist services call (docs/Geek-Receptionist.md section 6). A status code is read and answered as a Result
/// carrying a <see cref="VoiceFailureCodes"/> value: 404 with a message is not_found, 400 is invalid_request, 409 is conflict,
/// and everything else, an unreachable or slow GeekRepository and a rejected key included, is store_unavailable. Each call is
/// made once. Nothing here calls EnsureSuccessStatusCode and nothing turns a failure into an empty list, so a store that is
/// down is never read as "nothing there". Logs carry the status and the route, never a body or a query string: the body is a
/// person's name and number, a token's ciphertext or a call's transcript.
/// </summary>
public sealed class HttpVoiceRepository : IVoiceStore
{
    private const int MessageLimit = 300;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly ILogger<HttpVoiceRepository> _logger;

    public HttpVoiceRepository(HttpClient http, ILogger<HttpVoiceRepository> logger)
    {
        _http = http;
        _logger = logger;
    }

    // ----- Tenants -----

    public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) =>
        SendAsync<VoiceTenant>(HttpMethod.Post, "internal/v1/onboarding/tenants", command, ct);

    public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        SendAsync<VoiceTenant>(HttpMethod.Get, $"internal/v1/onboarding/tenants/{tenantId}", null, ct);

    public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) =>
        SendAsync<VoiceTenant>(HttpMethod.Post, $"internal/v1/onboarding/tenants/{tenantId}/activate", command, ct);

    // ----- Telephony, configuration, calendar connections -----

    public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) =>
        SendAsync<VoiceTelephony>(HttpMethod.Get, $"internal/v1/tenants/{tenantId}/telephony", null, ct);

    public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) =>
        SendAsync<VoiceReceptionistConfig>(HttpMethod.Get, $"internal/v1/tenants/{tenantId}/config", null, ct);

    public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) =>
        SendAsync<VoiceReceptionistConfig>(HttpMethod.Put, $"internal/v1/tenants/{tenantId}/config", config, ct);

    public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) =>
        SendAsync<VoiceMcpConnection>(HttpMethod.Get, $"internal/v1/tenants/{tenantId}/mcp/{Uri.EscapeDataString(provider)}", null, ct);

    public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) =>
        SendAsync<VoiceMcpConnection>(HttpMethod.Put, $"internal/v1/tenants/{tenantId}/mcp/{Uri.EscapeDataString(provider)}", command, ct);

    // ----- Knowledge -----

    public Task<Result<VoiceKnowledgeDocumentCreated>> CreateKnowledgeDocumentAsync(Guid tenantId, CreateVoiceKnowledgeDocumentCommand command, CancellationToken ct = default) =>
        SendAsync<VoiceKnowledgeDocumentCreated>(HttpMethod.Post, $"internal/v1/tenants/{tenantId}/knowledge/documents", command, ct);

    public async Task<Result<IReadOnlyList<VoiceKnowledgeHit>>> SearchKnowledgeAsync(Guid tenantId, SearchVoiceKnowledgeCommand command, CancellationToken ct = default)
    {
        var result = await SendAsync<List<VoiceKnowledgeHit>>(HttpMethod.Post, $"internal/v1/tenants/{tenantId}/knowledge/search", command, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Result<IReadOnlyList<VoiceKnowledgeHit>>.Success(result.Value!)
            : Result<IReadOnlyList<VoiceKnowledgeHit>>.Failure(result.Error ?? "The search failed.", result.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
    }

    // ----- Calls and appointments -----

    public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) =>
        SendAsync<VoiceCallStored>(HttpMethod.Post, $"internal/v1/tenants/{tenantId}/calls", command, ct);

    public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) =>
        SendAsync<VoiceAppointmentCreated>(HttpMethod.Post, $"internal/v1/tenants/{tenantId}/appointments", command, ct);

    // ----- transport -----

    private async Task<Result<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            return await ReadAsync<T>(response, method, path, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GeekRepository unreachable for {Method} {Path}.", method.Method, path);
            return Unavailable<T>("GeekRepository unreachable.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("GeekRepository timed out for {Method} {Path}.", method.Method, path);
            return Unavailable<T>("GeekRepository timed out.");
        }
    }

    private async Task<Result<T>> ReadAsync<T>(HttpResponseMessage response, HttpMethod method, string path, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var message = await ReadMessageAsync(response, ct).ConfigureAwait(false);
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound when message is not null:
                    return Result<T>.Failure(message, VoiceFailureCodes.NotFound);
                case HttpStatusCode.BadRequest:
                    return Result<T>.Failure(message ?? "GeekRepository refused the request.", VoiceFailureCodes.InvalidRequest);
                case HttpStatusCode.Conflict:
                    return Result<T>.Failure(message ?? "The record changed; read it again.", VoiceFailureCodes.Conflict);
                default:
                    // A 404 with no message is a route GeekRepository does not have, and a 401 is a key it did not accept:
                    // both are the store being unavailable to GeekAPI, never a missing record.
                    _logger.LogError("GeekRepository answered {Status} for {Method} {Path}.", (int)response.StatusCode, method.Method, path);
                    return Unavailable<T>($"GeekRepository answered {(int)response.StatusCode}.");
            }
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct).ConfigureAwait(false);
            if (value is not null) return Result<T>.Success(value);

            _logger.LogError("GeekRepository answered an empty body for {Method} {Path}.", method.Method, path);
            return Unavailable<T>("GeekRepository answered an empty body.");
        }
        catch (JsonException)
        {
            _logger.LogError("GeekRepository answered a body that did not parse for {Method} {Path}.", method.Method, path);
            return Unavailable<T>("GeekRepository answered a body that did not parse.");
        }
    }

    /// <summary>The plain-text message GeekRepository's voice controllers send with a refusal, or null when the body is empty.</summary>
    private static async Task<string?> ReadMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
        if (text.Length == 0) return null;
        return text.Length <= MessageLimit ? text : text[..MessageLimit];
    }

    private static Result<T> Unavailable<T>(string message) => Result<T>.Failure(message, VoiceFailureCodes.StoreUnavailable);
}
