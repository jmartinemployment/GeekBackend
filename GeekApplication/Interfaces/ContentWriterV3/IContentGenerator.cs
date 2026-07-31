using System.Runtime.CompilerServices;

namespace GeekApplication.Interfaces.ContentWriterV3;

/// <summary>
/// Generates content (drafts, sections, etc.) using an LLM.
/// Supports streaming for long-running generations.
/// </summary>
public interface IContentGenerator
{
    /// <summary>
    /// Generate a complete draft for an asset given the strategy brief and research.
    /// </summary>
    Task<string> GenerateDraftAsync(
        string strategyBriefAngle,
        string audienceProfile,
        string callToAction,
        List<string> supportingEvidence,
        CancellationToken ct = default);

    /// <summary>
    /// Generate a complete draft as structured JSON matching content-writer-v3's own
    /// ContentDocument/Section/Paragraph/Run schema (lib/types.ts) — not markdown prose. This is
    /// the shape the frontend actually renders; <see cref="GenerateDraftAsync"/>'s markdown output
    /// cannot be rendered into it. Returns the validated JSON string, ready to store as a
    /// ContentAssetVersion's BodyDocumentJson.
    /// </summary>
    Task<string> GenerateStructuredDraftAsync(
        string angle,
        string audienceProfile,
        string buyingStage,
        string callToAction,
        List<string> supportingEvidence,
        CancellationToken ct = default);

    /// <summary>
    /// Generate a specific section of content for feedback/refinement.
    /// </summary>
    Task<string> GenerateSectionAsync(
        string sectionHeading,
        string context,
        string specificFeedback,
        CancellationToken ct = default);

    /// <summary>
    /// Get token usage from the last generation (for cost tracking).
    /// </summary>
    TokenUsage LastUsage { get; }
}

public class TokenUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens => InputTokens + OutputTokens;
    public decimal EstimatedCost { get; set; } // USD
}

/// <summary>Which LLM backs a given IContentGenerator implementation — lets callers pick per
/// request rather than being locked to whichever provider happens to be the sole DI registration.</summary>
public enum ContentGeneratorProvider
{
    Anthropic,
    OpenAi,
}

/// <summary>Resolves the requested IContentGenerator implementation via keyed DI — same pattern
/// content-writer-v2's IContentProviderFactory uses for its own multi-provider selection.</summary>
public interface IContentGeneratorFactory
{
    IContentGenerator Get(ContentGeneratorProvider provider);
}

/// <summary>
/// Analytics data retrieval and aggregation.
/// </summary>
public interface IAnalyticsAdapter
{
    /// <summary>
    /// Fetch performance metrics for a published URL from GA4.
    /// </summary>
    Task<PerformanceMetrics?> GetMetricsAsync(
        string url,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default);

    /// <summary>
    /// Sync the last N days of analytics data for all published URLs.
    /// </summary>
    Task<int> SyncLatestMetricsAsync(int daysBack = 7, CancellationToken ct = default);
}

public class PerformanceMetrics
{
    public string Url { get; set; } = string.Empty;
    public int PageViews { get; set; }
    public double AverageSessionDuration { get; set; }
    public double BounceRate { get; set; }
    public int Conversions { get; set; }
    public double ConversionRate { get; set; }
    public DateTime DataDate { get; set; }
}

/// <summary>
/// Publishing to external platforms (WordPress, Supabase, etc.).
/// </summary>
public interface IPublishAdapter
{
    /// <summary>
    /// Publish content to the target platform.
    /// </summary>
    Task<PublishResult> PublishAsync(
        string title,
        string bodyHtml,
        Dictionary<string, object> metadata,
        CancellationToken ct = default);

    /// <summary>
    /// Check the health/connectivity of the platform.
    /// </summary>
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
}

public class PublishResult
{
    public bool Success { get; set; }
    public string PublishedUrl { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> ResponseData { get; set; } = new();
}

/// <summary>
/// Notification delivery (email, Slack, etc.).
/// </summary>
public interface INotificationService
{
    Task SendApprovalNotificationAsync(Guid assetVersionId, string approverName, CancellationToken ct = default);
    Task SendPublicationSuccessAsync(Guid publicationId, string publishedUrl, CancellationToken ct = default);
    Task SendPublicationFailureAsync(Guid publicationId, string errorMessage, CancellationToken ct = default);
    Task SendWeeklyPerformanceReportAsync(Guid clientId, CancellationToken ct = default);
}
