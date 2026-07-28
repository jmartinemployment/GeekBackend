using GeekApplication.Interfaces.ContentWriterV3;

namespace GeekAPI.Services.ContentWriterV3;

/// <summary>
/// Stub notification service. In production, integrate with SendGrid, Slack, etc.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger) => _logger = logger;

    public async Task SendApprovalNotificationAsync(Guid assetVersionId, string approverName, CancellationToken ct = default)
    {
        _logger.LogInformation("Notification: Asset {AssetVersionId} approved by {ApproverName}", assetVersionId, approverName);
        // TODO: Send email via SendGrid
        await Task.CompletedTask;
    }

    public async Task SendPublicationSuccessAsync(Guid publicationId, string publishedUrl, CancellationToken ct = default)
    {
        _logger.LogInformation("Notification: Publication {PublicationId} succeeded at {Url}", publicationId, publishedUrl);
        // TODO: Send email via SendGrid
        await Task.CompletedTask;
    }

    public async Task SendPublicationFailureAsync(Guid publicationId, string errorMessage, CancellationToken ct = default)
    {
        _logger.LogError("Notification: Publication {PublicationId} failed: {Error}", publicationId, errorMessage);
        // TODO: Send email via SendGrid
        await Task.CompletedTask;
    }

    public async Task SendWeeklyPerformanceReportAsync(Guid clientId, CancellationToken ct = default)
    {
        _logger.LogInformation("Notification: Generating weekly report for client {ClientId}", clientId);
        // TODO: Generate and send weekly performance digest
        await Task.CompletedTask;
    }
}

/// <summary>
/// Stub analytics adapter. In production, integrate with Google Analytics 4.
/// </summary>
public class GoogleAnalyticsAdapter : IAnalyticsAdapter
{
    private readonly ILogger<GoogleAnalyticsAdapter> _logger;

    public GoogleAnalyticsAdapter(ILogger<GoogleAnalyticsAdapter> logger) => _logger = logger;

    public async Task<PerformanceMetrics?> GetMetricsAsync(
        string url,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default)
    {
        _logger.LogInformation("GA4: Fetching metrics for {Url} from {StartDate} to {EndDate}", url, startDate, endDate);

        // TODO: Implement GA4 API call with OAuth2
        return new PerformanceMetrics
        {
            Url = url,
            PageViews = 0,
            AverageSessionDuration = 0,
            BounceRate = 0,
            Conversions = 0,
            ConversionRate = 0,
            DataDate = DateTime.UtcNow
        };
    }

    public async Task<int> SyncLatestMetricsAsync(int daysBack = 7, CancellationToken ct = default)
    {
        _logger.LogInformation("GA4: Syncing metrics for last {DaysBack} days", daysBack);

        // TODO: Query all published URLs and sync their latest metrics
        return 0;
    }
}

/// <summary>
/// Stub publish adapter for WordPress. In production, use WordPress REST API.
/// </summary>
public class WordPressPublishAdapter : IPublishAdapter
{
    private readonly ILogger<WordPressPublishAdapter> _logger;
    private readonly string _wordPressUrl;

    public WordPressPublishAdapter(ILogger<WordPressPublishAdapter> logger)
    {
        _logger = logger;
        _wordPressUrl = Environment.GetEnvironmentVariable("WORDPRESS_URL") ?? "http://example.com";
    }

    public async Task<PublishResult> PublishAsync(
        string title,
        string bodyHtml,
        Dictionary<string, object> metadata,
        CancellationToken ct = default)
    {
        _logger.LogInformation("WordPress: Publishing '{Title}' to {Url}", title, _wordPressUrl);

        try
        {
            // TODO: Implement WordPress REST API integration
            // - Create/update post
            // - Set featured image
            // - Add metadata
            // - Handle drafts vs published

            return new PublishResult
            {
                Success = false,
                ErrorMessage = "WordPress adapter not yet implemented"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WordPress publish failed");
            return new PublishResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("WordPress: Health check for {Url}", _wordPressUrl);
        // TODO: Hit WordPress health endpoint
        return false;
    }
}
