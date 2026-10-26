using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using GeekApplication.Models.Visitors;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

/// <summary>
/// repo/visitors/* over the GeekRepository client. Status codes are read and answered as Results;
/// nothing here calls EnsureSuccessStatusCode, so a store that is down is a Failure the processor
/// reads and the request still ends in a status the site can see. Logs carry the status code and
/// the route, never a body: the body is a visitor's record.
/// </summary>
public sealed class HttpVisitorRepository : IVisitorRepositoryClient
{
    public const int PageSize = 500;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly ILogger<HttpVisitorRepository> _logger;

    public HttpVisitorRepository(HttpClient http, ILogger<HttpVisitorRepository> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task<Result<VisitorSession>> PageViewAsync(VisitorPageViewCommand command, CancellationToken ct) =>
        PostAsync<VisitorSession>("repo/visitors/sessions/page-view", command, ct);

    public Task<Result<VisitorSession>> PageEndAsync(Guid sessionId, VisitorPageEndCommand command, CancellationToken ct) =>
        PostAsync<VisitorSession>($"repo/visitors/sessions/{sessionId}/page-end", command, ct);

    public Task<Result<VisitorSession>> ClickAsync(Guid sessionId, VisitorClickCommand command, CancellationToken ct) =>
        PostAsync<VisitorSession>($"repo/visitors/sessions/{sessionId}/clicks", command, ct);

    public Task<Result<VisitorSession>> VitalsAsync(Guid sessionId, VisitorVitalsCommand command, CancellationToken ct) =>
        PostAsync<VisitorSession>($"repo/visitors/sessions/{sessionId}/vitals", command, ct);

    public Task<Result<VisitorSession>> SchedulerStepAsync(Guid sessionId, VisitorSchedulerStepCommand command, CancellationToken ct) =>
        PostAsync<VisitorSession>($"repo/visitors/sessions/{sessionId}/scheduler", command, ct);

    public Task<Result<VisitorSession>> ContactSubmittedAsync(Guid sessionId, VisitorContactCommand command, CancellationToken ct) =>
        PostAsync<VisitorSession>($"repo/visitors/sessions/{sessionId}/contact", command, ct);

    public Task<Result<VisitorSession>> BrowserGeoAsync(Guid sessionId, VisitorBrowserGeoCommand command, CancellationToken ct) =>
        PostAsync<VisitorSession>($"repo/visitors/sessions/{sessionId}/browser-geo", command, ct);

    public Task<Result<VisitorSession>> ScoreAsync(Guid sessionId, VisitorScoreCommand command, CancellationToken ct) =>
        PostAsync<VisitorSession>($"repo/visitors/sessions/{sessionId}/score", command, ct);

    public Task<Result<VisitorSession>> GetSessionAsync(Guid sessionId, CancellationToken ct) =>
        GetAsync<VisitorSession>($"repo/visitors/sessions/{sessionId}", ct);

    public async Task<Result<IReadOnlyList<VisitorSession>>> ListSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var all = new List<VisitorSession>();
        DateTime? afterStartedAt = null;
        long? afterSequence = null;
        while (true)
        {
            var path = new StringBuilder("repo/visitors/sessions?fromUtc=")
                .Append(Uri.EscapeDataString(Iso(fromUtc)))
                .Append("&toUtc=").Append(Uri.EscapeDataString(Iso(toUtc)))
                .Append("&limit=").Append(PageSize);
            if (afterStartedAt is not null && afterSequence is not null)
            {
                path.Append("&afterStartedAt=").Append(Uri.EscapeDataString(Iso(afterStartedAt.Value)))
                    .Append("&afterSequence=").Append(afterSequence.Value.ToString(CultureInfo.InvariantCulture));
            }

            var page = await GetAsync<List<VisitorSession>>(path.ToString(), ct).ConfigureAwait(false);
            if (!page.IsSuccess) return Result<IReadOnlyList<VisitorSession>>.Failure(page.Error ?? "Session list failed.");
            var rows = page.Value ?? new List<VisitorSession>();
            all.AddRange(rows);
            if (rows.Count < PageSize) break;
            var last = rows[^1];
            afterStartedAt = last.StartedAt;
            afterSequence = last.Sequence;
        }
        return Result<IReadOnlyList<VisitorSession>>.Success(all);
    }

    public async Task<Result<IReadOnlyList<VisitorSession>>> ListSessionsForVisitorAsync(Guid visitorId, DateTime sinceUtc, CancellationToken ct)
    {
        var result = await GetAsync<List<VisitorSession>>(
            $"repo/visitors/sessions/for-visitor?visitorId={visitorId}&sinceUtc={Uri.EscapeDataString(Iso(sinceUtc))}&limit=500",
            ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Result<IReadOnlyList<VisitorSession>>.Success(result.Value ?? new List<VisitorSession>())
            : Result<IReadOnlyList<VisitorSession>>.Failure(result.Error ?? "Sessions for visitor failed.");
    }

    public async Task<Result<bool>> MarkIntentReachedAsync(Guid sessionId, VisitorIntentReachedCommand command, CancellationToken ct)
    {
        var result = await PostAsync<VisitorClaimResult>($"repo/visitors/sessions/{sessionId}/intent-reached", command, ct).ConfigureAwait(false);
        if (result.IsSuccess) return Result<bool>.Success(result.Value!.Claimed);
        return result.Status == ResultStatus.NotFound
            ? Result<bool>.NotFound(result.Error ?? "Session not found.")
            : Result<bool>.Failure(result.Error ?? "Intent mark failed.");
    }

    public Task<Result<VisitorProfile>> IdentifyAsync(VisitorIdentifyCommand command, CancellationToken ct) =>
        PostAsync<VisitorProfile>("repo/visitors/profiles/identify", command, ct);

    public Task<Result<VisitorProfile>> GetProfileAsync(Guid visitorId, CancellationToken ct) =>
        GetAsync<VisitorProfile>($"repo/visitors/profiles/{visitorId}", ct);

    public async Task<Result<IReadOnlyList<VisitorProfile>>> GetProfilesAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return Result<IReadOnlyList<VisitorProfile>>.Success(Array.Empty<VisitorProfile>());
        var result = await PostAsync<List<VisitorProfile>>(
            "repo/visitors/profiles/by-ids", new VisitorProfileIdsRequest { Ids = ids.ToList() }, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Result<IReadOnlyList<VisitorProfile>>.Success(result.Value ?? new List<VisitorProfile>())
            : Result<IReadOnlyList<VisitorProfile>>.Failure(result.Error ?? "Profiles failed.");
    }

    public Task<Result> CountDropAsync(VisitorDropCommand command, CancellationToken ct) =>
        PostVoidAsync("repo/visitors/filter-stats/drops", command, ct);

    public Task<Result<VisitorFilterDay>> GetFilterDayAsync(string day, CancellationToken ct) =>
        GetAsync<VisitorFilterDay>($"repo/visitors/filter-stats/{Uri.EscapeDataString(day)}", ct);

    public async Task<Result<bool>> ClaimDailyReportAsync(string date, VisitorDailyReportClaimCommand command, CancellationToken ct)
    {
        var result = await PostAsync<VisitorClaimResult>($"repo/visitors/daily-reports/{Uri.EscapeDataString(date)}/claim", command, ct).ConfigureAwait(false);
        return result.IsSuccess ? Result<bool>.Success(result.Value!.Claimed) : Result<bool>.Failure(result.Error ?? "Claim failed.");
    }

    public Task<Result> MarkDailyReportBuiltAsync(string date, VisitorDailyReportBuiltCommand command, CancellationToken ct) =>
        PostVoidAsync($"repo/visitors/daily-reports/{Uri.EscapeDataString(date)}/built", command, ct);

    public Task<Result> ReleaseDailyReportAsync(string date, VisitorDailyReportReleaseCommand command, CancellationToken ct) =>
        PostVoidAsync($"repo/visitors/daily-reports/{Uri.EscapeDataString(date)}/release", command, ct);

    public Task<Result<VisitorDailyReport>> GetDailyReportAsync(string date, CancellationToken ct) =>
        GetAsync<VisitorDailyReport>($"repo/visitors/daily-reports/{Uri.EscapeDataString(date)}", ct);

    public Task<Result<VisitorPurgeResult>> PurgeAsync(VisitorPurgeCommand command, CancellationToken ct) =>
        PostAsync<VisitorPurgeResult>("repo/visitors/retention/purge", command, ct);

    // ----- transport -----

    private async Task<Result<T>> GetAsync<T>(string path, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
            return await ReadAsync<T>(response, path, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GeekRepository unreachable for GET {Path}.", Route(path));
            return Result<T>.Failure("GeekRepository unreachable.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("GeekRepository timed out for GET {Path}.", Route(path));
            return Result<T>.Failure("GeekRepository timed out.");
        }
    }

    private async Task<Result<T>> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(path, content, ct).ConfigureAwait(false);
            return await ReadAsync<T>(response, path, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GeekRepository unreachable for POST {Path}.", Route(path));
            return Result<T>.Failure("GeekRepository unreachable.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("GeekRepository timed out for POST {Path}.", Route(path));
            return Result<T>.Failure("GeekRepository timed out.");
        }
    }

    private async Task<Result> PostVoidAsync(string path, object body, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(path, content, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return Result.Success();
            if (response.StatusCode == HttpStatusCode.NotFound) return Result.Failure("Not found.", "not_found");
            if (response.StatusCode == HttpStatusCode.BadRequest) return Result.Failure("GeekRepository refused the request.", "bad_request");
            _logger.LogError("GeekRepository answered {Status} for POST {Path}.", (int)response.StatusCode, Route(path));
            return Result.Failure($"GeekRepository answered {(int)response.StatusCode}.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GeekRepository unreachable for POST {Path}.", Route(path));
            return Result.Failure("GeekRepository unreachable.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("GeekRepository timed out for POST {Path}.", Route(path));
            return Result.Failure("GeekRepository timed out.");
        }
    }

    private async Task<Result<T>> ReadAsync<T>(HttpResponseMessage response, string path, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.NotFound) return Result<T>.NotFound("Not found.");
        if (response.StatusCode == HttpStatusCode.BadRequest) return Result<T>.Failure("GeekRepository refused the request.");
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GeekRepository answered {Status} for {Path}.", (int)response.StatusCode, Route(path));
            return Result<T>.Failure($"GeekRepository answered {(int)response.StatusCode}.");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct).ConfigureAwait(false);
            return value is null ? Result<T>.Failure("GeekRepository answered an empty body.") : Result<T>.Success(value);
        }
        catch (JsonException)
        {
            _logger.LogError("GeekRepository answered a body that did not parse for {Path}.", Route(path));
            return Result<T>.Failure("GeekRepository answered a body that did not parse.");
        }
    }

    private static string Iso(DateTime utc) =>
        VisitorTime.Normalize(utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>The route without its query string, which can carry a visitor id.</summary>
    private static string Route(string path)
    {
        var q = path.IndexOf('?');
        return q < 0 ? path : path[..q];
    }
}
