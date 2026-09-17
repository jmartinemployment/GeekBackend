using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>
/// What a crawl actually did, assembled once when it commits or aborts.
///
/// Reporting is a phase, not a side effect of logging. Before this, a finished crawl left a page
/// count and — if it failed outright — one ErrorSummary string. Per-page outcomes were counted per
/// batch and discarded, so "2,000 pages did not make it, why?" had no answer anywhere.
///
/// <b>Excluded is not failed.</b> A page the crawler declined to store because robots.txt disallows
/// it, or because it is a locale duplicate, is the crawler obeying policy — counting that as an
/// error makes a correct crawl look broken and buries the real failures underneath it.
/// </summary>
public sealed record GeekCrawlerRunReport
{
    /// <summary>
    /// Pages the crawl fetched and persisted while it ran. The diagnostic number — "it reached 1,847
    /// of an expected 2,500 before dying" — and it stays true whatever happens to the data after.
    /// </summary>
    public int PagesCollected { get; init; }

    /// <summary>
    /// What became of what the crawl collected. A count is the wrong shape for this: on an aborted
    /// run a "pages retained" figure is zero by design, and a zero sitting next to PagesCollected
    /// reads like a corpus measurement when there is no corpus. The outcome is a state, so it is
    /// reported as one.
    /// </summary>
    public GeekCrawlerRunOutcome Outcome { get; init; } = GeekCrawlerRunOutcome.Discarded;

    /// <summary>Links stored alongside the pages. Shares the run's outcome.</summary>
    public int LinksStored { get; init; }

    /// <summary>Deliberate omissions. Policy working, not error.</summary>
    public GeekCrawlerExcludedByPolicy ExcludedByPolicy { get; init; } = new();

    /// <summary>Pages the crawler wanted and did not get. These are the failures.</summary>
    public GeekCrawlerFailureBreakdown Failed { get; init; } = new();

    /// <summary>HTTP status histogram across every page attempted, keyed by status code.</summary>
    public IReadOnlyDictionary<string, int> StatusCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Up to a handful of example URLs per failure reason, so a reason is actionable rather than a
    /// number. Sanitized crawler-side: no credentials, no query strings.
    /// </summary>
    public IReadOnlyList<GeekCrawlerFailureSample> Samples { get; init; } = [];

    [JsonIgnore]
    public int TotalFailed =>
        Failed.RequestFailed + Failed.ChallengePage + Failed.ExtractEmpty;

    [JsonIgnore]
    public int TotalExcluded =>
        ExcludedByPolicy.RobotsDisallowed + ExcludedByPolicy.LocaleExcluded;

    /// <summary>
    /// Attempted pages that produced nothing, as a share of everything attempted excluding policy
    /// exclusions. A crawl can finish "successfully" having lost most of the site; this is the
    /// number that says so.
    /// </summary>
    [JsonIgnore]
    public double FailureRate
    {
        get
        {
            var attempted = PagesCollected + TotalFailed;
            return attempted == 0 ? 0d : (double)TotalFailed / attempted;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static GeekCrawlerRunReport? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<GeekCrawlerRunReport>(json, JsonOpts);
        }
        catch (JsonException)
        {
            // A report that will not parse is reported as absent rather than as an empty report:
            // "no report" and "a crawl that lost nothing" must not look the same.
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Pages not stored because the crawler was told not to. Never an error.</summary>
public sealed record GeekCrawlerExcludedByPolicy
{
    /// <summary>
    /// Disallowed by robots.txt. In a crawler that reads robots before enqueueing, a non-zero count
    /// here means the disallow was discovered after the URL was queued — a redirect into a
    /// disallowed path, or a robots.txt that changed mid-crawl. Worth noticing, never an error.
    /// </summary>
    public int RobotsDisallowed { get; init; }

    /// <summary>Locale duplicates of pages already held in the primary language.</summary>
    public int LocaleExcluded { get; init; }
}

/// <summary>Pages the crawler attempted and did not get. Each cause is separately actionable.</summary>
public sealed record GeekCrawlerFailureBreakdown
{
    /// <summary>Transport-level: DNS, connection reset, timeout, non-2xx.</summary>
    public int RequestFailed { get; init; }

    /// <summary>
    /// Bot detection served an interstitial instead of the page. Distinct from RequestFailed on
    /// purpose: a wave of these means the crawl identity is being rejected, and no amount of
    /// retrying fixes it. This is what a 403 in a polite crawl usually is.
    /// </summary>
    public int ChallengePage { get; init; }

    /// <summary>Fetched successfully, but extraction produced nothing usable.</summary>
    public int ExtractEmpty { get; init; }
}

public sealed record GeekCrawlerFailureSample(string Reason, string Url, string? Detail);

/// <summary>What happened to a finished crawl's pages.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GeekCrawlerRunOutcome
{
    /// <summary>Committed. Its pages are the corpus this slot now serves.</summary>
    Published,

    /// <summary>
    /// Aborted and its pages discarded — the only other end a crawl has. There is deliberately no
    /// "discard failed" outcome: a Qdrant delete that does not succeed is a full stop, not a state
    /// the system records and continues past.
    /// </summary>
    Discarded,
}
