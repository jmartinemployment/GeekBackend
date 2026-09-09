using System.Text.Json;
using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>Internal persistence API for owner-scoped durable canvas projects.</summary>
[ApiController]
[Route("repo/content-creator-v2/canvas-projects")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2CanvasProjectsController(ContentCreatorV2DbContext db) : ControllerBase
{
    private static readonly HashSet<string> ProjectStatuses =
        ["planning", "in-progress", "review", "complete"];
    private static readonly HashSet<string> AssetKinds =
        ["brief", "article", "social", "image", "email", "report"];
    private static readonly HashSet<string> VersionStatuses =
        ["draft", "in-review", "approved", "published"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CanvasProjectListItem>>> List(
        [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            return BadRequest("ownerUserId is required.");

        var items = await db.GccV2CanvasProjects.AsNoTracking()
            .Where(p => p.OwnerUserId == ownerUserId)
            .OrderByDescending(p => p.UpdatedAtUtc)
            .Select(p => new CanvasProjectListItem(
                p.Id, p.OwnerUserId, p.Name, p.Description, p.Status,
                p.CreatedAtUtc, p.UpdatedAtUtc, p.ActivityJson, p.Assets.Count))
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2CanvasProject>> Get(
        Guid id, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            return BadRequest("ownerUserId is required.");

        var project = await IncludeGraph(db.GccV2CanvasProjects.AsNoTracking())
            .SingleOrDefaultAsync(p => p.Id == id && p.OwnerUserId == ownerUserId, ct);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2CanvasProject>> Create(
        CreateCanvasProjectCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId) || string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("ownerUserId and name are required.");
        var status = string.IsNullOrWhiteSpace(command.Status) ? "planning" : command.Status.Trim();
        if (!ProjectStatuses.Contains(status))
            return BadRequest($"status must be one of: {string.Join(", ", ProjectStatuses)}.");

        var now = DateTimeOffset.UtcNow;
        var project = new GccV2CanvasProject
        {
            OwnerUserId = command.OwnerUserId.Trim(),
            Name = command.Name.Trim(),
            Description = (command.Description ?? string.Empty).Trim(),
            Status = status,
            ActivityJson = string.IsNullOrWhiteSpace(command.ActivityJson) ? "[]" : command.ActivityJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Add(project);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = project.Id, ownerUserId = project.OwnerUserId }, project);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GccV2CanvasProject>> Patch(
        Guid id, PatchCanvasProjectCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId))
            return BadRequest("ownerUserId is required.");

        var project = await db.GccV2CanvasProjects
            .SingleOrDefaultAsync(p => p.Id == id && p.OwnerUserId == command.OwnerUserId, ct);
        if (project is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(command.Name))
            project.Name = command.Name.Trim();
        if (command.Description is not null)
            project.Description = command.Description.Trim();
        if (!string.IsNullOrWhiteSpace(command.Status))
        {
            var status = command.Status.Trim();
            if (!ProjectStatuses.Contains(status))
                return BadRequest($"status must be one of: {string.Join(", ", ProjectStatuses)}.");
            project.Status = status;
        }
        if (command.ActivityJson is not null)
            project.ActivityJson = command.ActivityJson;
        project.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(await IncludeGraph(db.GccV2CanvasProjects.AsNoTracking())
            .SingleAsync(p => p.Id == project.Id, ct));
    }

    [HttpPost("{id:guid}/assets")]
    public async Task<ActionResult<GccV2CanvasAsset>> CreateAsset(
        Guid id, CreateCanvasAssetCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId) || string.IsNullOrWhiteSpace(command.Title))
            return BadRequest("ownerUserId and title are required.");
        var kind = string.IsNullOrWhiteSpace(command.Kind) ? "brief" : command.Kind.Trim();
        if (!AssetKinds.Contains(kind))
            return BadRequest($"kind must be one of: {string.Join(", ", AssetKinds)}.");

        var project = await db.GccV2CanvasProjects
            .Include(p => p.Assets)
            .SingleOrDefaultAsync(p => p.Id == id && p.OwnerUserId == command.OwnerUserId, ct);
        if (project is null) return NotFound();

        var parentIds = command.ParentAssetIds ?? [];
        if (parentIds.Count > 0)
        {
            var known = project.Assets.Select(a => a.Id).ToHashSet();
            if (parentIds.Any(pid => !known.Contains(pid)))
                return BadRequest("All parentAssetIds must belong to the same project.");
        }

        var now = DateTimeOffset.UtcNow;
        var asset = new GccV2CanvasAsset
        {
            ProjectId = project.Id,
            Title = command.Title.Trim(),
            Kind = kind,
            ParentAssetIdsJson = JsonSerializer.Serialize(parentIds),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        project.UpdatedAtUtc = now;
        db.Add(asset);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = project.Id, ownerUserId = project.OwnerUserId }, asset);
    }

    [HttpPost("{id:guid}/assets/{assetId:guid}/versions")]
    public async Task<ActionResult<GccV2CanvasAssetVersion>> AppendVersion(
        Guid id, Guid assetId, AppendCanvasAssetVersionCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId) || string.IsNullOrWhiteSpace(command.CreatedBy))
            return BadRequest("ownerUserId and createdBy are required.");
        var status = string.IsNullOrWhiteSpace(command.Status) ? "draft" : command.Status.Trim();
        if (!VersionStatuses.Contains(status))
            return BadRequest($"status must be one of: {string.Join(", ", VersionStatuses)}.");

        var project = await db.GccV2CanvasProjects
            .SingleOrDefaultAsync(p => p.Id == id && p.OwnerUserId == command.OwnerUserId, ct);
        if (project is null) return NotFound();

        var asset = await db.GccV2CanvasAssets
            .Include(a => a.Versions)
            .SingleOrDefaultAsync(a => a.Id == assetId && a.ProjectId == id, ct);
        if (asset is null) return NotFound();

        var next = (asset.Versions.Count == 0 ? 0 : asset.Versions.Max(v => v.VersionNumber)) + 1;
        var now = DateTimeOffset.UtcNow;
        var version = new GccV2CanvasAssetVersion
        {
            AssetId = asset.Id,
            VersionNumber = next,
            Status = status,
            Summary = (command.Summary ?? string.Empty).Trim(),
            EvidenceJson = string.IsNullOrWhiteSpace(command.EvidenceJson) ? "[]" : command.EvidenceJson,
            ProvenanceJson = string.IsNullOrWhiteSpace(command.ProvenanceJson) ? "{}" : command.ProvenanceJson,
            CreatedBy = command.CreatedBy.Trim(),
            CreatedAtUtc = now,
        };
        asset.UpdatedAtUtc = now;
        project.UpdatedAtUtc = now;
        db.Add(version);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = project.Id, ownerUserId = project.OwnerUserId }, version);
    }

    private static IQueryable<GccV2CanvasProject> IncludeGraph(IQueryable<GccV2CanvasProject> query) =>
        query.Include(p => p.Assets).ThenInclude(a => a.Versions);

    public sealed record CanvasProjectListItem(
        Guid Id, string OwnerUserId, string Name, string Description, string Status,
        DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string ActivityJson, int AssetCount);

    public sealed record CreateCanvasProjectCommand(
        string OwnerUserId, string Name, string? Description = null, string? Status = null,
        string? ActivityJson = null);

    public sealed record PatchCanvasProjectCommand(
        string OwnerUserId, string? Name = null, string? Description = null, string? Status = null,
        string? ActivityJson = null);

    public sealed record CreateCanvasAssetCommand(
        string OwnerUserId, string Title, string? Kind = null, IReadOnlyList<Guid>? ParentAssetIds = null);

    public sealed record AppendCanvasAssetVersionCommand(
        string OwnerUserId, string CreatedBy, string? Status = null, string? Summary = null,
        string? EvidenceJson = null, string? ProvenanceJson = null);
}
