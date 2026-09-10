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
    private const string ContractVersion = "gcc-roi-observed.v2";

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

        var published = await CountPublishedCanvasAssetsAsync(since, ct);

        var reviewMinutes = window
            .Where(r => r.CompletedAtUtc is not null)
            .Select(r => Math.Max(0, (r.CompletedAtUtc!.Value - r.CreatedAtUtc).TotalMinutes))
            .Sum();

        var empty = generated == 0 && accepted == 0 && published == 0;
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
                lookbackDays = days,
                periodLabel = $"Last {days} days (TaskRuns + Canvas publishes)",
                source = empty ? "empty" : "telemetry",
                notes = new[]
                {
                    "Counts are owner-scoped workflow outcomes, not cash ROI.",
                    "generated/accepted/rejected come from TaskRuns in the lookback window.",
                    "publishedCount counts Canvas asset versions whose latest status is published in the window.",
                    "reviewMinutes approximates wall-clock TaskRun duration for terminal runs.",
                },
            },
        });
    }

    private async Task<int> CountPublishedCanvasAssetsAsync(
        DateTimeOffset since, CancellationToken ct)
    {
        var projects = await repo.ListCanvasProjectsAsync(Owner, ct);
        var published = 0;
        foreach (var item in projects)
        {
            var project = await repo.GetCanvasProjectAsync(item.Id, Owner, ct);
            if (project is null) continue;
            foreach (var asset in project.Assets)
            {
                var latest = asset.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault();
                if (latest is null) continue;
                if (!string.Equals(latest.Status, "published", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (latest.CreatedAtUtc >= since)
                    published++;
            }
        }

        return published;
    }
}
