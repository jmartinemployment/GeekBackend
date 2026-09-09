using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/skills")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2SkillsController(ContentCreatorV2DbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2SkillPackage>>> List(
        [FromQuery] string? state,
        [FromQuery] string? contentType,
        [FromQuery] string? stage,
        CancellationToken ct)
    {
        var query = db.GccV2SkillPackages.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(state) || !string.IsNullOrWhiteSpace(contentType) || !string.IsNullOrWhiteSpace(stage))
            query = query.Where(x => x.Versions.Any(v =>
                (state == null || v.State == state) && v.Applicability.Any(a =>
                    (contentType == null || a.ContentType == contentType) &&
                    (stage == null || a.Stage == stage))));
        return Ok(await query.Include(x => x.Versions.OrderByDescending(v => v.ImportedAtUtc))
            .ThenInclude(v => v.Applicability)
            .Include(x => x.Versions).ThenInclude(v => v.Files)
            .OrderBy(x => x.Slug).ToListAsync(ct));
    }

    [HttpGet("{packageId:guid}")]
    public async Task<ActionResult<GccV2SkillPackage>> Get(Guid packageId, CancellationToken ct)
    {
        var package = await db.GccV2SkillPackages.AsNoTracking()
            .Include(x => x.Versions.OrderByDescending(v => v.ImportedAtUtc)).ThenInclude(v => v.Files)
            .Include(x => x.Versions).ThenInclude(v => v.Applicability)
            .Include(x => x.Versions).ThenInclude(v => v.Findings)
            .SingleOrDefaultAsync(x => x.Id == packageId, ct);
        return package is null ? NotFound() : Ok(package);
    }

    [HttpGet("{packageId:guid}/audit")]
    public async Task<ActionResult<IReadOnlyList<GccV2SkillAuditEvent>>> Audit(Guid packageId, CancellationToken ct) =>
        Ok(await db.GccV2SkillAuditEvents.AsNoTracking().Where(x => x.PackageId == packageId)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(ct));

    [HttpPost("imports")]
    public async Task<ActionResult<GccV2SkillPackage>> Import([FromBody] ImportSkillCommand command, CancellationToken ct)
    {
        if (command.Files.Count == 0 || command.Applicability.Count == 0)
            return BadRequest("files and applicability are required");
        if (await db.GccV2SkillVersions.AnyAsync(x => x.PackageSha256 == command.PackageSha256, ct))
            return Conflict("This immutable package digest has already been imported.");

        var package = await db.GccV2SkillPackages.Include(x => x.Versions)
            .SingleOrDefaultAsync(x => x.Slug == command.Slug, ct);
        if (package is null)
        {
            package = new GccV2SkillPackage
            {
                Slug = command.Slug, DisplayName = command.DisplayName, Description = command.Description,
                SourceRepository = command.SourceRepository, SourcePath = command.SourcePath,
                Publisher = command.Publisher, LifecycleState = "quarantined", IsFirstParty = command.IsFirstParty,
            };
            db.GccV2SkillPackages.Add(package);
        }

        var version = new GccV2SkillVersion
        {
            PackageId = package.Id, SemanticVersion = command.SemanticVersion,
            ImmutableGitRef = command.ImmutableGitRef, PackageSha256 = command.PackageSha256,
            ManifestDigest = command.ManifestDigest, License = command.License,
            Compatibility = command.Compatibility, State = command.PermanentRejection ? "rejected" : "quarantined",
            Files = command.Files.Select(x => new GccV2SkillFile
            {
                RelativePath = x.RelativePath, MediaType = x.MediaType, ByteCount = x.ByteCount,
                Sha256 = x.Sha256, Content = x.Content,
            }).ToList(),
            Applicability = command.Applicability.Select(x => new GccV2SkillApplicability
            {
                Stage = x.Stage, ContentType = x.ContentType, Order = x.Order,
                ConflictsJson = x.ConflictsJson, RequiredToolsJson = x.RequiredToolsJson,
                ActivationMode = x.ActivationMode,
            }).ToList(),
            Findings = command.Findings.Select(x => new GccV2SkillReviewFinding
            {
                Severity = x.Severity, Scanner = x.Scanner, Rule = x.Rule, FilePath = x.FilePath,
                Line = x.Line, Message = x.Message, PermanentRejection = x.PermanentRejection,
                Disposition = x.PermanentRejection ? "rejected" : "unreviewed",
            }).ToList(),
        };
        package.Versions.Add(version);
        RefreshAggregate(package);
        db.GccV2SkillVersions.Add(version);
        db.GccV2SkillAuditEvents.Add(AuditEvent(package.Id, version.Id, command.Actor, "import",
            null, version.State, command.SourceIp, command.RequestId));
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { packageId = package.Id }, package);
    }

    [HttpPost("versions/{versionId:guid}/review")]
    public async Task<ActionResult<GccV2SkillVersion>> Review(
        Guid versionId, [FromBody] ReviewSkillCommand command, CancellationToken ct)
    {
        var version = await db.GccV2SkillVersions.Include(x => x.Package).ThenInclude(x => x.Versions)
            .Include(x => x.Findings)
            .SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (version is null) return NotFound();
        if (version.State is "published" or "deprecated") return Conflict("Published versions are immutable.");
        if (version.Findings.Any(x => x.PermanentRejection) && command.Approve)
            return Conflict("Permanent policy rejections cannot be approved.");
        foreach (var disposition in command.Findings)
        {
            var finding = version.Findings.SingleOrDefault(x => x.Id == disposition.FindingId);
            if (finding is null) return BadRequest($"Unknown finding {disposition.FindingId}.");
            finding.Disposition = disposition.Disposition;
            finding.ReviewerRationale = disposition.Rationale;
        }
        if (command.Approve && version.Findings.Any(x => x.Severity == "high" && x.Disposition == "unreviewed"))
            return Conflict("Every high-severity finding requires an explicit disposition.");

        var before = version.State;
        version.State = command.Approve ? "approved" : "rejected";
        RefreshAggregate(version.Package);
        version.ReviewedAtUtc = DateTimeOffset.UtcNow;
        version.Reviewer = command.Actor;
        version.ReviewNotes = command.Notes;
        db.GccV2SkillAuditEvents.Add(AuditEvent(version.PackageId, version.Id, command.Actor,
            command.Approve ? "approve" : "reject", before, version.State, command.SourceIp, command.RequestId));
        await db.SaveChangesAsync(ct);
        return Ok(version);
    }

    [HttpPatch("versions/{versionId:guid}/findings/{findingId:guid}")]
    public async Task<ActionResult<GccV2SkillReviewFinding>> PatchFinding(
        Guid versionId, Guid findingId, [FromBody] PatchFindingCommand command, CancellationToken ct)
    {
        if (command.Disposition is not ("accepted" or "resolved" or "false_positive"))
            return BadRequest("disposition must be accepted, resolved, or false_positive.");
        if (string.IsNullOrWhiteSpace(command.ReviewerRationale))
            return BadRequest("reviewerRationale is required.");
        var version = await db.GccV2SkillVersions.Include(x => x.Package).ThenInclude(x => x.Versions)
            .SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (version is null) return NotFound();
        if (version.State is "published" or "deprecated") return Conflict("Published versions are immutable.");
        var finding = await db.GccV2SkillReviewFindings
            .SingleOrDefaultAsync(x => x.Id == findingId && x.VersionId == versionId, ct);
        if (finding is null) return NotFound();
        if (finding.PermanentRejection && command.Disposition != "accepted")
            return Conflict("Permanent policy findings may only be accepted as a rejection reason.");
        finding.Disposition = command.Disposition;
        finding.ReviewerRationale = command.ReviewerRationale.Trim();
        db.GccV2SkillAuditEvents.Add(AuditEvent(version.PackageId, version.Id, command.Actor,
            "finding-disposition", version.State, version.State, command.SourceIp, command.RequestId));
        await db.SaveChangesAsync(ct);
        return Ok(finding);
    }

    [HttpPost("versions/{versionId:guid}/publish")]
    public Task<ActionResult<GccV2SkillVersion>> Publish(Guid versionId, [FromBody] TransitionSkillCommand command, CancellationToken ct) =>
        Transition(versionId, "approved", "published", "publish", command, ct);

    [HttpPost("versions/{versionId:guid}/deprecate")]
    public Task<ActionResult<GccV2SkillVersion>> Deprecate(Guid versionId, [FromBody] TransitionSkillCommand command, CancellationToken ct) =>
        Transition(versionId, "published", "deprecated", "deprecate", command, ct);

    [HttpGet("versions/{versionId:guid}/files/{*path}")]
    public async Task<ActionResult<GccV2SkillFile>> File(Guid versionId, string path, CancellationToken ct)
    {
        var normalized = NormalizePath(path);
        if (normalized is null) return BadRequest("Invalid path.");
        var file = await db.GccV2SkillFiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.VersionId == versionId && x.RelativePath == normalized, ct);
        return file is null ? NotFound() : Ok(file);
    }

    private async Task<ActionResult<GccV2SkillVersion>> Transition(
        Guid versionId, string required, string next, string action,
        TransitionSkillCommand command, CancellationToken ct)
    {
        var version = await db.GccV2SkillVersions.Include(x => x.Package)
            .SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (version is null) return NotFound();
        if (version.State != required) return Conflict($"Version must be {required}.");
        version.State = next;
        RefreshAggregate(version.Package);
        if (next == "published") version.PublishedAtUtc = DateTimeOffset.UtcNow;
        if (next == "deprecated" && version.Package.Versions.All(x => x.State != "published"))
            version.Package.DeprecatedAtUtc = DateTimeOffset.UtcNow;
        db.GccV2SkillAuditEvents.Add(AuditEvent(version.PackageId, version.Id, command.Actor,
            action, required, next, command.SourceIp, command.RequestId));
        await db.SaveChangesAsync(ct);
        return Ok(version);
    }

    private static GccV2SkillAuditEvent AuditEvent(Guid packageId, Guid versionId, string actor,
        string action, string? before, string after, string? sourceIp, string? requestId) => new()
        {
            PackageId = packageId, VersionId = versionId, Actor = actor, Action = action,
            BeforeState = before, AfterState = after, SourceIp = sourceIp, RequestId = requestId,
        };

    private static void RefreshAggregate(GccV2SkillPackage package)
    {
        package.LifecycleState = package.Versions.Any(x => x.State == "published") ? "published"
            : package.Versions.Any(x => x.State == "approved") ? "approved"
            : package.Versions.Any(x => x.State == "quarantined") ? "quarantined"
            : package.Versions.Any(x => x.State == "rejected") ? "rejected"
            : "deprecated";
        if (package.LifecycleState != "deprecated") package.DeprecatedAtUtc = null;
    }

    private static string? NormalizePath(string value)
    {
        var path = Uri.UnescapeDataString(value).Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(path) || path.StartsWith('/') ||
               path.Split('/').Any(x => x is "." or ".." || string.IsNullOrEmpty(x)) ? null : path;
    }

    public sealed record ImportSkillCommand(
        string Slug, string DisplayName, string Description, string SourceRepository, string SourcePath,
        string Publisher, string SemanticVersion, string ImmutableGitRef, string PackageSha256,
        string ManifestDigest, string License, string Compatibility, bool IsFirstParty,
        bool PermanentRejection, IReadOnlyList<SkillFileCommand> Files,
        IReadOnlyList<SkillApplicabilityCommand> Applicability, IReadOnlyList<SkillFindingCommand> Findings,
        string Actor, string? SourceIp, string? RequestId);
    public sealed record SkillFileCommand(string RelativePath, string MediaType, long ByteCount, string Sha256, string Content);
    public sealed record SkillApplicabilityCommand(string Stage, string ContentType, int Order,
        string ConflictsJson, string RequiredToolsJson, string ActivationMode);
    public sealed record SkillFindingCommand(string Severity, string Scanner, string Rule, string? FilePath,
        int? Line, string Message, bool PermanentRejection);
    public sealed record FindingDispositionCommand(Guid FindingId, string Disposition, string Rationale);
    public sealed record PatchFindingCommand(
        string Disposition, string ReviewerRationale, string Actor, string? SourceIp, string? RequestId);
    public sealed record ReviewSkillCommand(bool Approve, string? Notes,
        IReadOnlyList<FindingDispositionCommand> Findings, string Actor, string? SourceIp, string? RequestId);
    public sealed record TransitionSkillCommand(string Actor, string? SourceIp, string? RequestId);
}
