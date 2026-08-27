using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/partner-research-records")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2PartnerResearchRecordsController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2PartnerResearchRecordsController(ContentCreatorV2DbContext db) => _db = db;

    /// <summary>Latest successful crawl for <paramref name="targetUrl"/> within <paramref name="withinHours"/>.</summary>
    [HttpGet("fresh")]
    public async Task<ActionResult<GccV2PartnerResearchRecord>> GetFresh(
        [FromQuery] string targetUrl,
        [FromQuery] int withinHours = 24,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
            return BadRequest("targetUrl is required");
        if (withinHours <= 0) withinHours = 24;

        var cutoff = DateTimeOffset.UtcNow.AddHours(-withinHours);
        var row = await _db.GccV2PartnerResearchRecords.AsNoTracking()
            .Where(r => r.IsSuccess
                        && r.TargetUrl == targetUrl.Trim()
                        && r.CrawledAtUtc >= cutoff
                        && r.PageJson != null
                        && r.PageJson != "")
            .OrderByDescending(r => r.CrawledAtUtc)
            .FirstOrDefaultAsync(ct);

        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2PartnerResearchRecord>> Create(
        [FromBody] CreateGccV2PartnerResearchRecordCommand command,
        CancellationToken ct)
    {
        if (command is null || command.CreateId == Guid.Empty || string.IsNullOrWhiteSpace(command.TargetUrl))
            return BadRequest("createId and targetUrl are required");

        var host = command.HostDomain;
        if (string.IsNullOrWhiteSpace(host))
        {
            try { host = new Uri(command.TargetUrl).Host; }
            catch { host = ""; }
        }

        var row = new GccV2PartnerResearchRecord
        {
            Id = Guid.NewGuid(),
            CreateId = command.CreateId,
            JobId = command.JobId,
            TargetUrl = command.TargetUrl.Trim(),
            HostDomain = host ?? "",
            CrawledAtUtc = DateTimeOffset.UtcNow,
            IsSuccess = command.IsSuccess,
            CrawlStatusLog = string.IsNullOrWhiteSpace(command.CrawlStatusLog)
                ? (command.IsSuccess ? "Success" : "Failed")
                : command.CrawlStatusLog.Trim(),
            ExtractedTitle = command.ExtractedTitle,
            PageJson = command.PageJson,
            FlattenedTextContent = command.FlattenedTextContent,
        };

        _db.GccV2PartnerResearchRecords.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    public record CreateGccV2PartnerResearchRecordCommand(
        Guid CreateId,
        string TargetUrl,
        bool IsSuccess,
        string? CrawlStatusLog = null,
        string? HostDomain = null,
        Guid? JobId = null,
        string? ExtractedTitle = null,
        string? PageJson = null,
        string? FlattenedTextContent = null);
}
