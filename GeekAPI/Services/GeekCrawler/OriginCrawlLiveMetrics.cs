namespace GeekAPI.Services.GeekCrawler;

/// <summary>Live BFS counters surfaced on host progress during an active crawl.</summary>
public sealed class OriginCrawlLiveMetrics
{
    private long _bytesFetched;

    public int QueueDepth { get; set; }
    public int InFlightCount { get; set; }

    public long BytesFetched => Interlocked.Read(ref _bytesFetched);

    public void AddBytes(long byteCount)
    {
        if (byteCount > 0)
            Interlocked.Add(ref _bytesFetched, byteCount);
    }
}
