using GeekAPI.Auth;
using GeekAPI.HttpClients;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<AnalyticsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<AnalyticsResponse>> GetAnalytics(
        [FromQuery] Guid clientId,
        CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        _logger.LogInformation("User {UserId} fetching analytics for client {ClientId}",
            _currentUser.UserId, clientId);

        // For now, return empty analytics
        // In production, this would aggregate data from publications and performance tracking
        var response = new AnalyticsResponse(
            Metrics: new List<ContentMetricDto>(),
            Insights: new List<InsightPerformanceDto>()
        );

        return Ok(response);
    }

    public record AnalyticsResponse(
        List<ContentMetricDto> Metrics,
        List<InsightPerformanceDto> Insights
    );

    public record ContentMetricDto(
        string Id,
        string Title,
        string PublishedDate,
        int Views,
        int EngagedViews,
        int Conversions,
        double AvgTimeOnPage,
        int BounceRate,
        int QualityScore
    );

    public record InsightPerformanceDto(
        string InsightId,
        string Title,
        int UsageCount,
        int SuccessCount,
        double AverageScore,
        int SuccessRate,
        string Status,
        List<string> Strengths,
        List<string> Weaknesses,
        bool ShouldRetire
    );
}
