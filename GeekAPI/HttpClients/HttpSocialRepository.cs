using System.Net;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

/// <summary>
/// repo/social/* over the GeekRepository client, the <see cref="ISocialStore"/> GeekAPI's social
/// services call. A status code is read and answered as a Result carrying a
/// <see cref="SocialFailureCodes"/> value: 404 is not_found, 400 is invalid_request, 409 is conflict
/// (a lost race the caller handles), and everything else, an unreachable or slow GeekRepository
/// included, is store_unavailable. Nothing here calls EnsureSuccessStatusCode and nothing turns a
/// failure into an empty list, so a store that is down is never read as "nothing there".
/// Logs carry the status and the route, never a body or a query string: the body is a comment, a
/// token's ciphertext or a person's name.
/// </summary>
public sealed class HttpSocialRepository : ISocialStore
{
    private const int MessageLimit = 300;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly ILogger<HttpSocialRepository> _logger;

    public HttpSocialRepository(HttpClient http, ILogger<HttpSocialRepository> logger)
    {
        _http = http;
        _logger = logger;
    }

    // ----- Accounts -----

    public Task<Result<SocialAccount>> ConnectAccountAsync(ConnectSocialAccountCommand command, CancellationToken ct = default) =>
        SendAsync<SocialAccount>(HttpMethod.Post, "repo/social/accounts", command, ct);

    public async Task<Result<IReadOnlyList<SocialAccount>>> ListAccountsAsync(CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<SocialAccount>>(HttpMethod.Get, "repo/social/accounts", null, ct).ConfigureAwait(false));

    public Task<Result<SocialAccount>> GetAccountAsync(Guid accountId, CancellationToken ct = default) =>
        SendAsync<SocialAccount>(HttpMethod.Get, $"repo/social/accounts/{accountId}", null, ct);

    public Task<Result<SocialAccountCredential>> GetCredentialAsync(Guid accountId, CancellationToken ct = default) =>
        SendAsync<SocialAccountCredential>(HttpMethod.Get, $"repo/social/accounts/{accountId}/credential", null, ct);

    public Task<Result<SocialAccount>> StoreTokenAsync(Guid accountId, StoreSocialTokenCommand command, CancellationToken ct = default) =>
        SendAsync<SocialAccount>(HttpMethod.Put, $"repo/social/accounts/{accountId}/token", command, ct);

    public Task<Result<SocialAccount>> MarkReconnectRequiredAsync(Guid accountId, CancellationToken ct = default) =>
        SendAsync<SocialAccount>(HttpMethod.Post, $"repo/social/accounts/{accountId}/reconnect-required", new { }, ct);

    public Task<Result<SocialAccount>> UpdateSettingsAsync(Guid accountId, UpdateSocialAccountSettingsCommand command, CancellationToken ct = default) =>
        SendAsync<SocialAccount>(HttpMethod.Put, $"repo/social/accounts/{accountId}/settings", command, ct);

    // ----- Posts -----

    public Task<Result<SocialPost>> RecordPostAsync(RecordSocialPostCommand command, CancellationToken ct = default) =>
        SendAsync<SocialPost>(HttpMethod.Post, "repo/social/posts", command, ct);

    public async Task<Result<IReadOnlyList<SocialPost>>> ListPostsAsync(Guid accountId, int limit, CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<SocialPost>>(
            HttpMethod.Get, $"repo/social/posts?accountId={accountId}&limit={limit}", null, ct).ConfigureAwait(false));

    // ----- Engagement events -----

    public async Task<Result<bool>> RecordEventAsync(EngagementEvent engagementEvent, CancellationToken ct = default)
    {
        var result = await SendAsync<SocialEventStored>(HttpMethod.Post, "repo/social/events", engagementEvent, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Result<bool>.Success(result.Value!.Inserted)
            : Result<bool>.Failure(result.Error ?? "Recording the event failed.", result.ErrorCode ?? SocialFailureCodes.StoreUnavailable);
    }

    public Task<Result<EngagementEvent>> GetEventAsync(Guid eventId, CancellationToken ct = default) =>
        SendAsync<EngagementEvent>(HttpMethod.Get, $"repo/social/events/{eventId}", null, ct);

    public async Task<Result<IReadOnlyList<EngagementEvent>>> ListEventsAsync(string triageState, Guid? accountId, int limit, CancellationToken ct = default)
    {
        var path = new StringBuilder("repo/social/events?limit=").Append(limit)
            .Append("&triageState=").Append(Uri.EscapeDataString(triageState ?? string.Empty));
        if (accountId is not null) path.Append("&accountId=").Append(accountId.Value);
        return AsReadOnly(await SendAsync<List<EngagementEvent>>(HttpMethod.Get, path.ToString(), null, ct).ConfigureAwait(false));
    }

    public Task<Result<EngagementEvent>> RecordTriageOutcomeAsync(Guid eventId, RecordTriageOutcomeCommand command, CancellationToken ct = default) =>
        SendAsync<EngagementEvent>(HttpMethod.Post, $"repo/social/events/{eventId}/triage", command, ct);

    // ----- Reply drafts -----

    public Task<Result<ReplyDraft>> CreateDraftAsync(CreateReplyDraftCommand command, CancellationToken ct = default) =>
        SendAsync<ReplyDraft>(HttpMethod.Post, "repo/social/drafts", command, ct);

    public Task<Result<ReplyDraft>> GetDraftAsync(Guid draftId, CancellationToken ct = default) =>
        SendAsync<ReplyDraft>(HttpMethod.Get, $"repo/social/drafts/{draftId}", null, ct);

    public async Task<Result<IReadOnlyList<ReplyDraft>>> ListDraftsAsync(string? status, Guid? accountId, int limit, CancellationToken ct = default)
    {
        var path = new StringBuilder("repo/social/drafts?limit=").Append(limit);
        if (status is not null) path.Append("&status=").Append(Uri.EscapeDataString(status));
        if (accountId is not null) path.Append("&accountId=").Append(accountId.Value);
        return AsReadOnly(await SendAsync<List<ReplyDraft>>(HttpMethod.Get, path.ToString(), null, ct).ConfigureAwait(false));
    }

    public Task<Result<ReplyDraft>> EditDraftAsync(Guid draftId, EditReplyDraftCommand command, CancellationToken ct = default) =>
        SendAsync<ReplyDraft>(HttpMethod.Put, $"repo/social/drafts/{draftId}/text", command, ct);

    public Task<Result<ReplyDraft>> TransitionDraftAsync(Guid draftId, TransitionReplyDraftCommand command, CancellationToken ct = default) =>
        SendAsync<ReplyDraft>(HttpMethod.Post, $"repo/social/drafts/{draftId}/transition", command, ct);

    // ----- Audit -----

    public Task<Result<ActionAudit>> RecordAuditAsync(ActionAudit audit, CancellationToken ct = default) =>
        SendAsync<ActionAudit>(HttpMethod.Post, "repo/social/audit", audit, ct);

    public async Task<Result<IReadOnlyList<ActionAudit>>> ListAuditAsync(Guid? accountId, int limit, CancellationToken ct = default)
    {
        var path = new StringBuilder("repo/social/audit?limit=").Append(limit);
        if (accountId is not null) path.Append("&accountId=").Append(accountId.Value);
        return AsReadOnly(await SendAsync<List<ActionAudit>>(HttpMethod.Get, path.ToString(), null, ct).ConfigureAwait(false));
    }

    // ----- Retention and recovery -----

    public Task<Result<SocialPurgeResult>> PurgeExpiredAsync(DateTime asOf, CancellationToken ct = default) =>
        SendAsync<SocialPurgeResult>(HttpMethod.Post, "repo/social/maintenance/purge", new SocialPurgeCommand { AsOf = asOf }, ct);

    public Task<Result<SocialInterruptedSends>> FailInterruptedSendsAsync(DateTime at, CancellationToken ct = default) =>
        SendAsync<SocialInterruptedSends>(
            HttpMethod.Post, "repo/social/maintenance/fail-interrupted-sends", new SocialFailInterruptedSendsCommand { At = at }, ct);

    // ----- transport -----

    private static Result<IReadOnlyList<T>> AsReadOnly<T>(Result<List<T>> result) =>
        result.IsSuccess
            ? Result<IReadOnlyList<T>>.Success(result.Value!)
            : Result<IReadOnlyList<T>>.Failure(result.Error ?? "The list failed.", result.ErrorCode ?? SocialFailureCodes.StoreUnavailable);

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
            _logger.LogError(ex, "GeekRepository unreachable for {Method} {Path}.", method.Method, Route(path));
            return Unavailable<T>("GeekRepository unreachable.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("GeekRepository timed out for {Method} {Path}.", method.Method, Route(path));
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
                    return Result<T>.Failure(message, SocialFailureCodes.NotFound);
                case HttpStatusCode.BadRequest:
                    return Result<T>.Failure(message ?? "GeekRepository refused the request.", SocialFailureCodes.InvalidRequest);
                case HttpStatusCode.Conflict:
                    return Result<T>.Failure(message ?? "The record changed; read it again.", SocialFailureCodes.Conflict);
                default:
                    // A 404 with no message is a route GeekRepository does not have, not a missing record.
                    _logger.LogError("GeekRepository answered {Status} for {Method} {Path}.", (int)response.StatusCode, method.Method, Route(path));
                    return Unavailable<T>($"GeekRepository answered {(int)response.StatusCode}.");
            }
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct).ConfigureAwait(false);
            if (value is not null) return Result<T>.Success(value);

            _logger.LogError("GeekRepository answered an empty body for {Method} {Path}.", method.Method, Route(path));
            return Unavailable<T>("GeekRepository answered an empty body.");
        }
        catch (JsonException)
        {
            _logger.LogError("GeekRepository answered a body that did not parse for {Method} {Path}.", method.Method, Route(path));
            return Unavailable<T>("GeekRepository answered a body that did not parse.");
        }
    }

    /// <summary>The plain-text message GeekRepository's social controllers send with a refusal, or null when the body is empty.</summary>
    private static async Task<string?> ReadMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
        if (text.Length == 0) return null;
        return text.Length <= MessageLimit ? text : text[..MessageLimit];
    }

    private static Result<T> Unavailable<T>(string message) => Result<T>.Failure(message, SocialFailureCodes.StoreUnavailable);

    /// <summary>The route without its query string.</summary>
    private static string Route(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q < 0 ? path : path[..q];
    }
}
