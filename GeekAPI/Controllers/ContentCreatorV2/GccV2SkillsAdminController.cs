using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Generation;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

[ApiController]
[Route("api/geek-content-creator-v2/skills")]
public sealed class GccV2SkillsAdminController(
    ICurrentUserContext user,
    GccV2SkillAdminPolicy admin,
    GccV2GitHubSkillImporter importer,
    GccV2SkillSnapshotRegistry snapshots,
    HttpGccV2Repository repo) : ControllerBase
{
    [HttpGet("registry")]
    public async Task<ActionResult<IReadOnlyList<GccV2SkillPackageDto>>> Registry(
        [FromQuery] string? contentType, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        return Ok(await repo.ListSkillsAsync("published", contentType, null, ct));
    }

    [HttpGet("resolve")]
    public async Task<ActionResult<object>> Resolve([FromQuery] string? contentTypes, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var requested = (contentTypes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToList();
        var all = new List<GccV2SkillPackageDto>();
        foreach (var contentType in requested)
            all.AddRange(await repo.ListSkillsAsync("published", contentType, null, ct));
        var packages = all.DistinctBy(x => x.Id).OrderBy(x => x.Slug, StringComparer.Ordinal).ToList();
        var resolvedAt = DateTimeOffset.UtcNow;
        return Ok(new
        {
            snapshotVersion = "gcc-skill-resolution.v2",
            catalogVersion = GccV2SkillSnapshotRegistry.CatalogVersion,
            snapshotDigest = GccV2SkillApiMapper.ResolutionDigest(packages),
            resolvedAtUtc = resolvedAt,
            signatureKeyId = (string?)null,
            skills = packages.SelectMany(package => package.Versions
                .Where(version => version.State == "published")
                .Select(version => GccV2SkillApiMapper.Summary(package, version))),
        });
    }

    [HttpGet("admin")]
    public async Task<ActionResult<object>> AdminWorkspace(CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        var packages = await repo.ListSkillsAsync(ct: ct);
        var details = new List<object>();
        foreach (var package in packages)
        {
            var full = await repo.GetSkillAsync(package.Id, ct) ?? package;
            var audit = await repo.GetSkillAuditAsync(package.Id, ct);
            details.AddRange(full.Versions.OrderByDescending(x => x.ImportedAtUtc)
                .Select(version => GccV2SkillApiMapper.FlattenDetail(full, version, audit)));
        }
        return Ok(new { authorized = true, skills = details });
    }

    [HttpGet("admin/{packageId:guid}")]
    public async Task<ActionResult<GccV2SkillPackageDto>> Inspect(Guid packageId, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        var package = await repo.GetSkillAsync(packageId, ct);
        return package is null ? NotFound() : Ok(package);
    }

    [HttpGet("admin/{packageId:guid}/audit")]
    public async Task<ActionResult<IReadOnlyList<GccV2SkillAuditEventDto>>> Audit(Guid packageId, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.GetSkillAuditAsync(packageId, ct));
    }

    [HttpPost("admin/import")]
    public async Task<ActionResult<GccV2SkillPackageDto>> Import(
        [FromBody] GccV2GitHubSkillImportRequest request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        try
        {
            var command = await importer.ImportAsync(request, Actor(), SourceIp(), HttpContext.TraceIdentifier, ct);
            var package = await repo.ImportSkillAsync(command, ct);
            if (command.PermanentRejection)
                return UnprocessableEntity(new
                {
                    error = "The package was quarantined as permanently rejected.",
                    package,
                    permanentFindings = command.Findings.Where(x => x.PermanentRejection),
                });
            return Ok(package);
        }
        catch (GccV2SkillImportException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("admin/versions/{versionId:guid}/review")]
    public async Task<ActionResult<GccV2SkillVersionDto>> Review(
        Guid versionId, [FromBody] ReviewRequest request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.ReviewSkillAsync(versionId,
            new ReviewGccV2SkillCommand(request.IsApprove, request.Notes, request.Findings ?? [],
                Actor(), SourceIp(), HttpContext.TraceIdentifier), ct));
    }

    [HttpPost("admin/{versionId:guid}/review")]
    public Task<ActionResult<GccV2SkillVersionDto>> ReviewAlias(
        Guid versionId, [FromBody] ReviewRequest request, CancellationToken ct) =>
        Review(versionId, request, ct);

    [HttpPatch("admin/{versionId:guid}/findings/{findingId:guid}")]
    public async Task<ActionResult<GccV2SkillReviewFindingDto>> Finding(
        Guid versionId, Guid findingId, [FromBody] FindingRequest request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.PatchSkillFindingAsync(versionId, findingId,
            new(request.Disposition, request.ReviewerRationale, Actor(), SourceIp(), HttpContext.TraceIdentifier), ct));
    }

    [HttpPost("admin/versions/{versionId:guid}/publish")]
    public async Task<ActionResult<GccV2SkillVersionDto>> Publish(Guid versionId, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.PublishSkillAsync(versionId, Transition(), ct));
    }

    [HttpPost("admin/{versionId:guid}/publish")]
    public Task<ActionResult<GccV2SkillVersionDto>> PublishAlias(Guid versionId, CancellationToken ct) =>
        Publish(versionId, ct);

    [HttpPost("admin/versions/{versionId:guid}/deprecate")]
    public async Task<ActionResult<GccV2SkillVersionDto>> Deprecate(Guid versionId, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.DeprecateSkillAsync(versionId, Transition(), ct));
    }

    [HttpPost("admin/{versionId:guid}/deprecate")]
    public Task<ActionResult<GccV2SkillVersionDto>> DeprecateAlias(Guid versionId, CancellationToken ct) =>
        Deprecate(versionId, ct);

    [HttpPost("/api/geek-content-creator-v2/internal/skills/jobs/{jobId:guid}/attempts/{attemptId}/stages/{stage}/activate")]
    public async Task<ActionResult<object>> Activate(
        Guid jobId, string attemptId, string stage,
        [FromBody] ActivateRequest request, CancellationToken ct)
    {
        if (!ValidInternalRequest()) return Unauthorized();
        var snapshot = await LoadSnapshot(jobId, request.Reference, attemptId, stage, ct);
        if (snapshot is null) return Forbid();
        var skill = snapshot.Skills.SingleOrDefault(x => x.Id == request.SkillId);
        if (skill is null || !skill.SupportedStages.Contains(stage, StringComparer.Ordinal)) return Forbid();
        return Ok(new
        {
            skill.ActivationId,
            skill = new { id = skill.Id, skill.Name, skill.Description, skill.Version },
            instructions = skill.SkillMd,
            resources = skill.Resources.Select(x => new { x.Path, digest = x.Sha256 }),
        });
    }

    [HttpPost("/api/geek-content-creator-v2/internal/skills/jobs/{jobId:guid}/attempts/{attemptId}/stages/{stage}/resources")]
    public async Task<ActionResult<object>> Resource(
        Guid jobId, string attemptId, string stage,
        [FromBody] ResourceRequest request, CancellationToken ct)
    {
        if (!ValidInternalRequest()) return Unauthorized();
        var snapshot = await LoadSnapshot(jobId, request.Reference, attemptId, stage, ct);
        if (snapshot is null) return Forbid();
        var skill = snapshot.Skills.SingleOrDefault(x => x.Id == request.SkillId);
        if (skill is null || !skill.SupportedStages.Contains(stage, StringComparer.Ordinal)
            || request.Path.StartsWith("scripts/", StringComparison.Ordinal)
            || skill.Resources.SingleOrDefault(x => x.Path == request.Path) is not { } resource
            || !string.Equals(resource.Sha256, request.Digest, StringComparison.Ordinal))
            return Forbid();
        return Ok(new { request.Path, digest = resource.Sha256, resource.MediaType, resource.Content });
    }

    private async Task<GccV2SignedSkillExecutionEnvelopeV2?> LoadSnapshot(
        Guid jobId, GccV2SkillSnapshotReferenceV2 reference, string attemptId, string stage, CancellationToken ct)
    {
        if (reference.JobId != jobId || reference.AttemptId != attemptId || reference.Stage != stage
            || !snapshots.VerifyBinding(reference)) return null;
        var job = await repo.GetJobAsync(jobId, ct);
        if (job is null) return null;
        var snapshot = await snapshots.BuildEnvelopeAsync(job, attemptId, stage, ct);
        return snapshot.SnapshotDigest == reference.SnapshotDigest ? snapshot : null;
    }

    private bool IsAdmin(out ActionResult denied)
    {
        denied = user.IsAuthenticated
            ? StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator authorization is required.", configuration = admin.ConfigurationHint })
            : Unauthorized();
        return admin.IsAuthorized(user);
    }
    private bool ValidInternalRequest() => HttpContext.Request.Path.Value?.Contains("/internal/", StringComparison.Ordinal) == true;
    private string Actor() => user.UserId.ToString("D");
    private string? SourceIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
    private TransitionGccV2SkillCommand Transition() => new(Actor(), SourceIp(), HttpContext.TraceIdentifier);

    public sealed record ReviewRequest(
        string? Decision, string? Notes, IReadOnlyList<GccV2SkillFindingDisposition>? Findings, bool? Approve = null)
    {
        public bool IsApprove => Approve ?? string.Equals(Decision, "approve", StringComparison.OrdinalIgnoreCase);
    }
    public sealed record FindingRequest(string Disposition, string ReviewerRationale);
    public sealed record ActivateRequest(string SkillId, GccV2SkillSnapshotReferenceV2 Reference);
    public sealed record ResourceRequest(string SkillId, string Path, string Digest, GccV2SkillSnapshotReferenceV2 Reference);
}
