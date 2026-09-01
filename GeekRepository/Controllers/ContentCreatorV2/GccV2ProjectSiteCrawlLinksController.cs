using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/project-site/links")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2ProjectSiteCrawlLinksController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2ProjectSiteCrawlLinksController(ContentCreatorV2DbContext db) => _db = db;

    [HttpPost("batch")]
    public async Task<ActionResult> CreateBatch(
        [FromBody] CreateGccV2ProjectSiteCrawlLinkBatchCommand command,
        CancellationToken ct)
    {
        if (command is null || command.RunId == Guid.Empty || command.Links is null || command.Links.Count == 0)
            return BadRequest("runId and links are required");

        var now = DateTimeOffset.UtcNow;
        foreach (var link in command.Links)
        {
            _db.GccV2ProjectSiteCrawlLinks.Add(new GccV2ProjectSiteCrawlLink
            {
                Id = Guid.NewGuid(),
                RunId = command.RunId,
                PageId = link.PageId,
                FromUrl = link.FromUrl ?? "",
                LinkUrl = link.LinkUrl ?? "",
                IsSameOrigin = link.IsSameOrigin,
                DiscoveredAtUtc = now,
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { count = command.Links.Count });
    }

    public record CreateGccV2ProjectSiteCrawlLinkBatchCommand(
        Guid RunId,
        IReadOnlyList<CreateGccV2ProjectSiteCrawlLinkItemCommand> Links);

    public record CreateGccV2ProjectSiteCrawlLinkItemCommand(
        Guid PageId,
        string FromUrl,
        string LinkUrl,
        bool IsSameOrigin);
}
