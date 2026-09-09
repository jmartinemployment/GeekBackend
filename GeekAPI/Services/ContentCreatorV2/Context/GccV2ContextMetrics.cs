using System.Diagnostics.Metrics;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public static class GccV2ContextMetrics
{
    private static readonly Meter Meter = new("GeekAPI.ContentCreatorV2.Context", "1.0.0");
    public static readonly Counter<long> UploadBytes =
        Meter.CreateCounter<long>("gcc_v2_context_upload_bytes");
    public static readonly Counter<long> UploadQuotaRejections =
        Meter.CreateCounter<long>("gcc_v2_context_upload_quota_rejections");
    public static readonly Counter<long> IngestionOutcomes =
        Meter.CreateCounter<long>("gcc_v2_context_ingestion_outcomes");
    public static readonly Histogram<double> IngestionLatency =
        Meter.CreateHistogram<double>("gcc_v2_context_ingestion_latency_seconds");
    public static readonly Counter<long> RetentionOutcomes =
        Meter.CreateCounter<long>("gcc_v2_context_retention_outcomes");
    public static readonly Counter<long> ResolutionOutcomes =
        Meter.CreateCounter<long>("gcc_v2_context_resolution_outcomes");
    public static readonly Histogram<long> ManifestEntries =
        Meter.CreateHistogram<long>("gcc_v2_context_manifest_entries");
}
