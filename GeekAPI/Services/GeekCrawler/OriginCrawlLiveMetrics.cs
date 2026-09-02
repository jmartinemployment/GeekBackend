namespace GeekAPI.Services.GeekCrawler;

/// <summary>Live BFS counters surfaced on host progress during an active crawl.</summary>
public sealed class OriginCrawlLiveMetrics
{
    public int QueueDepth { get; set; }
    public int InFlightCount { get; set; }
}
