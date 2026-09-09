using GeekAPI.Auth;
using GeekAPI.HttpClients;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>
/// Transparent projected-versus-observed ROI helpers. Observed metrics are workflow outcomes
/// (counts/rates), not dollar claims.
/// </summary>
[ApiController]
[Route("api/geek-content-creator-v2/roi")]
public sealed class GccV2RoiController(
    ICurrentUserContext user,
    HttpGccV2Repository repo) : ControllerBase
{
    private const string ContractVersion = "gcc-roi-observed.v1";

    private string Owner => user.UserId.ToString("D");

    [HttpGet("observed")]
    public async Task<ActionResult<object>> Observed(
        [FromQuery] int lookbackDays = 90, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var days = Math.Clamp(lookbackDays, 1, 365);
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var runs = await repo.ListTaskRunsAsync(Owner, ct: ct);
        var window = runs.Where(r => r.CreatedAtUtc >= since).ToList();

        var generated = window.Count;
        var accepted = window.Count(r =>
            string.Equals(r.Status, "succeeded", StringComparison.OrdinalIgnoreCase));
        var rejected = window.Count(r =>
            string.Equals(r.Status, "failed", StringComparison.OrdinalIgnoreCase));
        var cancelled = window.Count(r =>
            string.Equals(r.Status, "cancelled", StringComparison.OrdinalIgnoreCase));
        // Until CMS publish telemetry is wired, treat successful TaskRuns as the closest
        // observed "published" handoff count and label the source honestly.
        var published = accepted;

        var reviewMinutes = window
            .Where(r => r.CompletedAtUtc is not null)
            .Select(r => Math.Max(0, (r.CompletedAtUtc!.Value - r.CreatedAtUtc).TotalMinutes))
            .Sum();

        return Ok(new
        {
            contractVersion = ContractVersion,
            observed = new
            {
                generatedCount = generated,
                acceptedCount = accepted,
                publishedCount = published,
                rejectedCount = rejected,
                cancelledCount = cancelled,
                reviewMinutes = (int)Math.Round(reviewMinutes),
                periodLabel = $"Last {days} days (TaskRuns)",
                source = generated == 0 && accepted == 0 ? "empty" : "telemetry",
                notes = new[]
                {
                    "Counts are owner-scoped TaskRun outcomes, not cash ROI.",
                    "publishedCount currently mirrors succeeded TaskRuns until CMS publish events are linked.",
                    "reviewMinutes approximates wall-clock run duration for terminal runs.",
                },
            },
        });
    }
}
