using System.Net;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

/// <summary>
/// repo/crm/* over the GeekRepository client: the <see cref="ICrmStore"/> GeekAPI calls. 404 with a
/// message is not_found, 400 is invalid_request, 409 is conflict, and anything else (an unreachable
/// GeekRepository included) is store_unavailable. Logs carry the route and status, never a body:
/// bodies hold people's names and numbers.
/// </summary>
public sealed class HttpCrmRepository(HttpClient http, ILogger<HttpCrmRepository> logger) : ICrmStore
{
    private const int MessageLimit = 300;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public Task<Result<EnsureParticipantResult>> EnsureParticipantByPhoneAsync(EnsureParticipantByPhoneCommand command, CancellationToken ct = default) =>
        SendAsync<EnsureParticipantResult>(HttpMethod.Post, "repo/crm/participants/ensure-by-phone", command, ct);

    public Task<Result<RecordActivityResult>> RecordActivityAsync(RecordActivityCommand command, CancellationToken ct = default) =>
        SendAsync<RecordActivityResult>(HttpMethod.Post, "repo/crm/activities", command, ct);

    public Task<Result<bool>> SetSmsOptOutAsync(SetSmsOptOutCommand command, CancellationToken ct = default) =>
        SendAsync<bool>(HttpMethod.Post, "repo/crm/contact-points/sms-opt-out", command, ct);

    public Task<Result<ImportProspectsResult>> ImportProspectsAsync(ImportProspectsCommand command, CancellationToken ct = default) =>
        SendAsync<ImportProspectsResult>(HttpMethod.Post, "repo/crm/imports", command, ct);

    public async Task<Result<IReadOnlyList<CrmParticipantSummary>>> ListParticipantsAsync(Guid tenantId, string? lifecycleStage, string? kind, int limit, CancellationToken ct = default)
    {
        var query = new List<string> { $"limit={limit}" };
        if (lifecycleStage is not null) query.Add("stage=" + Uri.EscapeDataString(lifecycleStage));
        if (kind is not null) query.Add("kind=" + Uri.EscapeDataString(kind));
        return AsReadOnly(await SendAsync<List<CrmParticipantSummary>>(HttpMethod.Get,
            $"repo/crm/tenants/{tenantId}/participants?{string.Join('&', query)}", null, ct).ConfigureAwait(false));
    }

    public Task<Result<CrmParticipant>> GetParticipantAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
        SendAsync<CrmParticipant>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/participants/{participantId}", null, ct);

    public Task<Result<CrmDeal>> CreateDealAsync(CreateDealCommand command, CancellationToken ct = default) =>
        SendAsync<CrmDeal>(HttpMethod.Post, "repo/crm/deals", command, ct);

    public Task<Result<CrmDeal>> MoveDealStageAsync(Guid dealId, MoveDealStageCommand command, CancellationToken ct = default) =>
        SendAsync<CrmDeal>(HttpMethod.Post, $"repo/crm/deals/{dealId}/stage", command, ct);

    public async Task<Result<IReadOnlyList<CrmDeal>>> ListDealsAsync(Guid tenantId, bool openOnly, int limit, CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<CrmDeal>>(HttpMethod.Get,
            $"repo/crm/tenants/{tenantId}/deals?openOnly={(openOnly ? "true" : "false")}&limit={limit}", null, ct).ConfigureAwait(false));

    private static Result<IReadOnlyList<T>> AsReadOnly<T>(Result<List<T>> result) =>
        result.IsSuccess
            ? Result<IReadOnlyList<T>>.Success(result.Value!)
            : Result<IReadOnlyList<T>>.Failure(result.Error ?? "The list failed.", result.ErrorCode ?? CrmFailureCodes.StoreUnavailable);

    private async Task<Result<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var text = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
                var message = text.Length == 0 ? null : text.Length <= MessageLimit ? text : text[..MessageLimit];
                switch (response.StatusCode)
                {
                    case HttpStatusCode.NotFound when message is not null:
                        return Result<T>.Failure(message, CrmFailureCodes.NotFound);
                    case HttpStatusCode.BadRequest:
                        return Result<T>.Failure(message ?? "GeekRepository refused the request.", CrmFailureCodes.InvalidRequest);
                    case HttpStatusCode.Conflict:
                        return Result<T>.Failure(message ?? "The record changed; read it again.", CrmFailureCodes.Conflict);
                    default:
                        logger.LogError("GeekRepository answered {Status} for {Method} {Path}.", (int)response.StatusCode, method.Method, Route(path));
                        return Unavailable<T>($"GeekRepository answered {(int)response.StatusCode}.");
                }
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct).ConfigureAwait(false);
            if (value is not null) return Result<T>.Success(value);
            logger.LogError("GeekRepository answered an empty body for {Method} {Path}.", method.Method, Route(path));
            return Unavailable<T>("GeekRepository answered an empty body.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "GeekRepository unreachable for {Method} {Path}.", method.Method, Route(path));
            return Unavailable<T>("GeekRepository unreachable.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("GeekRepository timed out for {Method} {Path}.", method.Method, Route(path));
            return Unavailable<T>("GeekRepository timed out.");
        }
        catch (JsonException)
        {
            logger.LogError("GeekRepository answered a body that did not parse for {Method} {Path}.", method.Method, Route(path));
            return Unavailable<T>("GeekRepository answered a body that did not parse.");
        }
    }

    private static Result<T> Unavailable<T>(string message) => Result<T>.Failure(message, CrmFailureCodes.StoreUnavailable);

    private static string Route(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q < 0 ? path : path[..q];
    }
}
