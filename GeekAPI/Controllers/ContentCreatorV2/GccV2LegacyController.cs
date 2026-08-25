using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>
/// Read-only view of v1 Content Creator creates — calls existing <see cref="HttpGccRepository"/>
/// (never edits v1 controllers/services). No migration of v1 data into v2 tables.
/// </summary>
[ApiController]
[Route("api/geek-content-creator-v2/legacy")]
public class GccV2LegacyController : ControllerBase
{
    private readonly ICurrentUserContext _user;
    private readonly HttpGccRepository _v1Repo;

    public GccV2LegacyController(ICurrentUserContext user, HttpGccRepository v1Repo)
    {
        _user = user;
        _v1Repo = v1Repo;
    }

    [HttpGet("creates")]
    public async Task<ActionResult<IReadOnlyList<object>>> ListCreates(CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var list = await _v1Repo.ListCreatesAsync(clientId: null, _user.UserId.ToString("D"), ct);
        var mapped = list.Select(MapCreateSummary).ToList();
        return Ok(mapped);
    }

    [HttpGet("creates/{id:guid}")]
    public async Task<ActionResult<object>> GetCreate(Guid id, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _v1Repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        var artifacts = await _v1Repo.ListArtifactsAsync(id, ct);
        var artifactViews = new List<object>();
        foreach (var artifact in artifacts)
        {
            var versions = await _v1Repo.ListVersionsAsync(artifact.Id, ct);
            var latest = versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            artifactViews.Add(new
            {
                artifact.Id,
                artifact.Type,
                artifact.Name,
                artifact.Status,
                artifact.CreatedAtUtc,
                latestVersionId = latest?.Id,
                latestVersionNumber = latest?.VersionNumber,
                bodyDocumentJson = latest?.BodyDocumentJson,
            });
        }

        return Ok(new
        {
            source = "geek-content-creator-v1",
            readOnly = true,
            create = MapCreateDetail(create),
            artifacts = artifactViews,
        });
    }

    private static object MapCreateSummary(GccCreateDto create) => new
    {
        id = create.Id,
        topic = create.Topic,
        contentType = create.StartingContentType,
        status = create.Status,
        department = create.Department,
        siteAnalysisProfileId = create.SiteAnalysisId,
        createdAtUtc = create.CreatedAtUtc,
        updatedAtUtc = create.UpdatedAtUtc,
    };

    private static object MapCreateDetail(GccCreateDto create) => new
    {
        create.Id,
        create.ClientId,
        create.OwnerUserId,
        contentType = create.StartingContentType,
        create.Topic,
        create.Notes,
        create.Department,
        siteAnalysisProfileId = create.SiteAnalysisId,
        create.SiteSectionJson,
        create.BriefJson,
        create.ResearchJson,
        create.Status,
        create.CreatedAtUtc,
        create.UpdatedAtUtc,
    };

    private bool IsOwner(Guid ownerUserId) =>
        _user.UserId != Guid.Empty && ownerUserId == _user.UserId;
}
