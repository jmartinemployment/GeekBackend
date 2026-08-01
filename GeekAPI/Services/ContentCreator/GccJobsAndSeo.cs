using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Optional facade over Geek-SEO Niche Analyzer. Falls back to null when unset/unreachable
/// so Content Creator day-one smoke can use demo gaps.
/// </summary>
public class HttpGeekSeoNicheClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly ILogger<HttpGeekSeoNicheClient> _logger;
    private readonly bool _enabled;

    public HttpGeekSeoNicheClient(HttpClient http, ILogger<HttpGeekSeoNicheClient> logger)
    {
        _http = http;
        _logger = logger;
        _enabled = _http.BaseAddress is not null;
    }

    public bool IsEnabled => _enabled;

    public async Task<IReadOnlyList<LiveGap>?> GetGapsAsync(
        Guid profileId,
        string? bearerToken,
        CancellationToken ct)
    {
        if (!_enabled) return null;
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get,
                $"api/seo/niche-analyzer/{profileId}/gaps?quickWinsOnly=false");
            if (!string.IsNullOrWhiteSpace(bearerToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Geek-SEO gaps {Status} for {ProfileId}", res.StatusCode, profileId);
                return null;
            }

            var json = await res.Content.ReadAsStringAsync(ct);
            var gaps = JsonSerializer.Deserialize<List<LiveGapDto>>(json, JsonOpts) ?? [];
            return gaps.Select(g => new LiveGap(
                g.SubtopicId == Guid.Empty ? Guid.NewGuid().ToString("N") : g.SubtopicId.ToString("D"),
                string.IsNullOrWhiteSpace(g.TargetKeyword) ? g.SubtopicTitle : g.TargetKeyword,
                g.PillarTopic,
                g.IsQuickWin
                    ? "Quick-win topical gap"
                    : $"Gap under {g.PillarTopic} ({g.RecommendedFormat})",
                string.Equals(g.RecommendedFormat, "pillar", StringComparison.OrdinalIgnoreCase)
                    || g.RecommendedFormat.Contains("pillar", StringComparison.OrdinalIgnoreCase))).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Geek-SEO gaps call failed");
            return null;
        }
    }

    private sealed record LiveGapDto(
        Guid SubtopicId,
        string PillarTopic,
        string SubtopicTitle,
        string TargetKeyword,
        bool IsQuickWin,
        string RecommendedFormat);

    public sealed record LiveGap(
        string Id,
        string Topic,
        string? SectionPath,
        string Reason,
        bool SuggestPillar);
}

public class GccJobStore
{
    private readonly ConcurrentDictionary<Guid, GccJob> _jobs = new();

    public GccJob Create(string kind, Guid createId)
    {
        var job = new GccJob(Guid.NewGuid(), kind, createId, "running", null, null, DateTime.UtcNow, null);
        _jobs[job.Id] = job;
        return job;
    }

    public void Complete(Guid id, object? result) =>
        Update(id, j => j with { Status = "ready", ResultJson = JsonSerializer.Serialize(result), CompletedAtUtc = DateTime.UtcNow });

    public void Fail(Guid id, string error) =>
        Update(id, j => j with { Status = "failed", Error = error, CompletedAtUtc = DateTime.UtcNow });

    public GccJob? Get(Guid id) => _jobs.TryGetValue(id, out var j) ? j : null;

    private void Update(Guid id, Func<GccJob, GccJob> mutator)
    {
        _jobs.AddOrUpdate(id, _ => throw new KeyNotFoundException(), (_, cur) => mutator(cur));
    }
}

public sealed record GccJob(
    Guid Id,
    string Kind,
    Guid CreateId,
    string Status,
    string? ResultJson,
    string? Error,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);
