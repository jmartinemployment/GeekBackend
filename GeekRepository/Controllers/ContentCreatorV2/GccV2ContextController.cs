using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/context")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2ContextController(ContentCreatorV2DbContext db) : ControllerBase
{
    private const string IngestionNotifyChannel = "gcc_v2_context_ingestion";
    private static readonly HashSet<string> LifecycleStates =
        [GccV2ContextLifecycle.Draft, GccV2ContextLifecycle.InReview, GccV2ContextLifecycle.Approved,
         GccV2ContextLifecycle.Deprecated, GccV2ContextLifecycle.Revoked];

    [HttpGet("knowledge")]
    public async Task<ActionResult<IReadOnlyList<GccV2KnowledgeAsset>>> ListKnowledge(
        [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return BadRequest("ownerUserId is required");
        return Ok(await db.GccV2KnowledgeAssets.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && !x.IsRetired)
            .Include(x => x.Versions.OrderByDescending(v => v.VersionNumber))
            .OrderBy(x => x.Name).ToListAsync(ct));
    }

    [HttpGet("quotas")]
    public async Task<ActionResult<GccV2ContextQuotaUsage>> GetQuotaUsage(
        [FromQuery] string ownerUserId, [FromQuery] Guid? createId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return BadRequest("ownerUserId is required");
        var knowledgeBytes = await db.GccV2KnowledgeResources.AsNoTracking()
            .Where(x => x.Version.Asset.OwnerUserId == ownerUserId && x.ResourceKind == "original")
            .SumAsync(x => (long?)x.ByteSize, ct) ?? 0;
        var ownerAttachmentBytes = await db.GccV2RunAttachments.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && x.DeletedAtUtc == null)
            .SumAsync(x => (long?)x.ByteSize, ct) ?? 0;
        var runAttachmentBytes = createId is null ? 0 : await db.GccV2RunAttachments.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && x.CreateId == createId && x.DeletedAtUtc == null)
            .SumAsync(x => (long?)x.ByteSize, ct) ?? 0;
        return Ok(new GccV2ContextQuotaUsage(
            knowledgeBytes, ownerAttachmentBytes, runAttachmentBytes));
    }

    [HttpGet("knowledge/{assetId:guid}")]
    public async Task<ActionResult<GccV2KnowledgeAsset>> GetKnowledge(
        Guid assetId, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        var asset = await db.GccV2KnowledgeAssets.AsNoTracking()
            .Where(x => x.Id == assetId && x.OwnerUserId == ownerUserId)
            .Include(x => x.Versions.OrderByDescending(v => v.VersionNumber))
            .ThenInclude(x => x.Resources).SingleOrDefaultAsync(ct);
        return asset is null ? NotFound() : Ok(asset);
    }

    [HttpPost("knowledge")]
    public async Task<ActionResult<GccV2KnowledgeAsset>> CreateKnowledge(
        [FromBody] CreateKnowledgeCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId) || string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("ownerUserId and name are required");
        var asset = new GccV2KnowledgeAsset
        {
            OwnerUserId = command.OwnerUserId, Name = command.Name.Trim(),
            Description = command.Description?.Trim(), TagsJson = command.TagsJson ?? "[]",
            Kind = string.IsNullOrWhiteSpace(command.Kind) ? "document" : command.Kind.Trim(),
        };
        db.GccV2KnowledgeAssets.Add(asset);
        AddAudit(command.OwnerUserId, "knowledge", asset.Id, null, command.ActorUserId, "create", null, "draft");
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetKnowledge), new { assetId = asset.Id, ownerUserId = command.OwnerUserId }, asset);
    }

    [HttpPatch("knowledge/{assetId:guid}")]
    public async Task<ActionResult<GccV2KnowledgeAsset>> PatchKnowledge(
        Guid assetId, [FromBody] PatchCatalogCommand command, CancellationToken ct)
    {
        var asset = await db.GccV2KnowledgeAssets.SingleOrDefaultAsync(
            x => x.Id == assetId && x.OwnerUserId == command.OwnerUserId, ct);
        if (asset is null) return NotFound();
        if (command.Name is not null) asset.Name = command.Name.Trim();
        if (command.Description is not null) asset.Description = command.Description.Trim();
        if (command.IsRetired is not null) asset.IsRetired = command.IsRetired.Value;
        asset.Revision++;
        asset.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(asset.OwnerUserId, "knowledge", asset.Id, null, command.ActorUserId, "update", null, null);
        await db.SaveChangesAsync(ct);
        return Ok(asset);
    }

    [HttpPost("knowledge/{assetId:guid}/versions")]
    public async Task<ActionResult<GccV2KnowledgeAssetVersion>> CreateKnowledgeVersion(
        Guid assetId, [FromBody] CreateKnowledgeVersionCommand command, CancellationToken ct)
    {
        var asset = await db.GccV2KnowledgeAssets.Include(x => x.Versions)
            .SingleOrDefaultAsync(x => x.Id == assetId && x.OwnerUserId == command.OwnerUserId, ct);
        if (asset is null) return NotFound();
        if (asset.IsRetired) return Conflict("Retired assets cannot receive new versions.");
        if (!IsSha256(command.CanonicalSha256) || !IsSha256(command.ContentSha256))
            return BadRequest("canonicalSha256 and contentSha256 must be lowercase SHA-256 hex.");
        if (asset.Versions.Any(x => x.CanonicalSha256 == command.CanonicalSha256))
            return Conflict("This immutable version digest already exists for the asset.");
        var version = new GccV2KnowledgeAssetVersion
        {
            AssetId = asset.Id, VersionNumber = asset.Versions.Count == 0 ? 1 : asset.Versions.Max(x => x.VersionNumber) + 1,
            SchemaVersion = command.SchemaVersion, CanonicalSha256 = command.CanonicalSha256,
            ContentSha256 = command.ContentSha256, SourceDescriptorJson = command.SourceDescriptorJson ?? "{}",
            MediaType = command.MediaType, Language = command.Language,
            SourceModifiedAtUtc = command.SourceModifiedAtUtc, CreatedBy = command.ActorUserId,
            ProvenanceJson = command.ProvenanceJson ?? "{}",
        };
        db.GccV2KnowledgeAssetVersions.Add(version);
        AddAudit(asset.OwnerUserId, "knowledge", asset.Id, version.Id, command.ActorUserId, "version_create", null, "draft");
        await db.SaveChangesAsync(ct);
        return Created("", version);
    }

    [HttpPost("knowledge/versions/{versionId:guid}/resources")]
    public async Task<ActionResult<GccV2KnowledgeResource>> AddKnowledgeResource(
        Guid versionId, [FromBody] AddKnowledgeResourceCommand command, CancellationToken ct)
    {
        var version = await db.GccV2KnowledgeAssetVersions.Include(x => x.Asset)
            .SingleOrDefaultAsync(x => x.Id == versionId && x.Asset.OwnerUserId == command.OwnerUserId, ct);
        if (version is null) return NotFound();
        if (!IsSha256(command.Sha256)) return BadRequest("sha256 is invalid");
        var resource = new GccV2KnowledgeResource
        {
            KnowledgeAssetVersionId = versionId, ResourceKind = command.ResourceKind,
            ObjectKey = command.ObjectKey, ByteSize = command.ByteSize, Sha256 = command.Sha256,
            MediaType = command.MediaType, SafeFileName = command.SafeFileName,
            ScanState = command.ScanState, ParserName = command.ParserName,
            ParserVersion = command.ParserVersion, ExtractionSha256 = command.ExtractionSha256,
            CoordinatesJson = command.CoordinatesJson ?? "{}",
        };
        db.GccV2KnowledgeResources.Add(resource);
        await db.SaveChangesAsync(ct);
        return Created("", resource);
    }

    [HttpPost("knowledge/versions/{versionId:guid}/queue-ingestion")]
    public async Task<ActionResult<GccV2ContextIngestionJob>> QueueKnowledgeIngestion(
        Guid versionId, [FromBody] QueueKnowledgeIngestionCommand command, CancellationToken ct)
    {
        var version = await db.GccV2KnowledgeAssetVersions.Include(x => x.Asset).Include(x => x.Resources)
            .SingleOrDefaultAsync(x => x.Id == versionId && x.Asset.OwnerUserId == command.OwnerUserId, ct);
        if (version is null) return NotFound();
        var resource = version.Resources.SingleOrDefault(x => x.ResourceKind == "original"
            && x.ObjectKey == command.ObjectKey && x.ByteSize == command.ByteSize && x.Sha256 == command.Sha256);
        if (resource is null) return Conflict("Final object metadata does not match the registered resource.");

        var existingJob = await db.GccV2ContextIngestionJobs
            .Include(x => x.Events)
            .Where(x => x.TargetKind == "knowledge" && x.TargetId == versionId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (existingJob is not null)
        {
            // Idempotent re-promote: wake in-flight jobs; revive failed/canceled ones so stuck
            // "processing" promotions can recover without creating a duplicate queue row.
            if (existingJob.Status is "failed" or "canceled")
            {
                existingJob.Status = "queued";
                existingJob.ProgressPercent = 0;
                existingJob.TerminalError = null;
                existingJob.ClaimedByInstanceId = null;
                existingJob.ClaimedAtUtc = null;
                existingJob.LeaseUntilUtc = null;
                existingJob.HeartbeatAtUtc = null;
                existingJob.CompletedAtUtc = null;
                existingJob.UpdatedAtUtc = DateTimeOffset.UtcNow;
                version.ExtractionState = "queued";
                version.IndexState = "pending";
                var seq = existingJob.Events.Count == 0 ? 1 : existingJob.Events.Max(x => x.Seq) + 1;
                existingJob.Events.Add(new GccV2ContextIngestionEvent
                {
                    Seq = seq,
                    Type = "Requeued",
                    PayloadJson = """{"reason":"promote-to-source retry"}""",
                });
                await db.SaveChangesAsync(ct);
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_notify({IngestionNotifyChannel}, {existingJob.Id.ToString()})", ct);
            return Ok(existingJob);
        }

        version.ExtractionState = "queued";
        version.IndexState = "pending";
        var job = new GccV2ContextIngestionJob
            { OwnerUserId = command.OwnerUserId, TargetKind = "knowledge", TargetId = versionId };
        db.GccV2ContextIngestionJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_notify({IngestionNotifyChannel}, {job.Id.ToString()})", ct);
        return Created("", job);
    }

    [HttpGet("ingestion-jobs/{id:guid}")]
    public async Task<ActionResult<GccV2ContextIngestionJob>> GetIngestionJob(Guid id, CancellationToken ct)
    {
        var value = await db.GccV2ContextIngestionJobs.AsNoTracking().Include(x => x.Events)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpGet("ingestion-jobs/by-status/{status}")]
    public async Task<ActionResult<IReadOnlyList<GccV2ContextIngestionJob>>> ListIngestionJobs(
        string status, [FromQuery] DateTimeOffset? leaseBefore, [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var query = db.GccV2ContextIngestionJobs.AsNoTracking().Where(x => x.Status == status);
        if (leaseBefore is not null) query = query.Where(x => x.LeaseUntilUtc < leaseBefore);
        return Ok(await query.OrderBy(x => x.CreatedAtUtc).Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct));
    }

    [HttpPost("ingestion-jobs/{id:guid}/claim")]
    public async Task<ActionResult<GccV2ContextIngestionJob>> ClaimIngestionJob(
        Guid id, [FromQuery] string instanceId, [FromQuery] int leaseSeconds = 120,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return BadRequest("instanceId is required");
        var now = DateTimeOffset.UtcNow;
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE content_creator_v2.gcc_v2_context_ingestion_jobs
            SET ""ClaimedByInstanceId"" = {instanceId}, ""ClaimedAtUtc"" = {now},
                ""HeartbeatAtUtc"" = {now}, ""LeaseUntilUtc"" = {now.AddSeconds(Math.Max(10, leaseSeconds))},
                ""Status"" = 'running', ""AttemptCount"" = ""AttemptCount"" + 1, ""UpdatedAtUtc"" = {now}
            WHERE ""Id"" = {id} AND (
                ""Status"" = 'queued' OR
                (""Status"" = 'running' AND ""LeaseUntilUtc"" IS NOT NULL AND ""LeaseUntilUtc"" < {now})
            )", ct);
        if (rows == 0) return Conflict();
        return Ok(await db.GccV2ContextIngestionJobs.AsNoTracking().SingleAsync(x => x.Id == id, ct));
    }

    [HttpPost("ingestion-jobs/{id:guid}/transition")]
    public async Task<ActionResult<GccV2ContextIngestionJob>> TransitionIngestionJob(
        Guid id, [FromBody] TransitionIngestionJobCommand command, CancellationToken ct)
    {
        var job = await db.GccV2ContextIngestionJobs.Include(x => x.Events).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (job is null) return NotFound();
        if (command.ProgressPercent is < 0 or > 100) return BadRequest("progressPercent must be 0..100");
        var terminal = command.Status is "ready" or "failed" or "cancelled";
        if (job.Status is "ready" or "failed" or "cancelled") return Conflict("Ingestion is already terminal.");
        job.Status = command.Status;
        job.ProgressPercent = command.ProgressPercent;
        job.TerminalError = command.TerminalError;
        job.HeartbeatAtUtc = DateTimeOffset.UtcNow;
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (terminal)
        {
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.ClaimedByInstanceId = null;
            job.LeaseUntilUtc = null;
        }
        var seq = job.Events.Count == 0 ? 1 : job.Events.Max(x => x.Seq) + 1;
        job.Events.Add(new GccV2ContextIngestionEvent
            { Seq = seq, Type = command.EventType, PayloadJson = command.EventPayloadJson ?? "{}" });
        if (job.TargetKind == "knowledge")
        {
            var version = await db.GccV2KnowledgeAssetVersions.SingleAsync(x => x.Id == job.TargetId, ct);
            version.ExtractionState = command.Status;
            if (command.Status == "ready") version.IndexState = command.IndexState ?? "ready";
            else if (command.Status == "failed") version.IndexState = "failed";
        }
        else if (job.TargetKind == "run_attachment")
        {
            var attachment = await db.GccV2RunAttachments.SingleAsync(x => x.Id == job.TargetId, ct);
            attachment.IngestionState = command.Status;
        }
        await db.SaveChangesAsync(ct);
        return Ok(job);
    }

    [HttpPost("knowledge/versions/{versionId:guid}/{transition}")]
    public async Task<ActionResult<GccV2KnowledgeAssetVersion>> TransitionKnowledge(
        Guid versionId, string transition, [FromBody] TransitionCommand command, CancellationToken ct)
    {
        var version = await db.GccV2KnowledgeAssetVersions.Include(x => x.Asset)
            .SingleOrDefaultAsync(x => x.Id == versionId && x.Asset.OwnerUserId == command.OwnerUserId, ct);
        if (version is null) return NotFound();
        var result = ApplyTransition(version, transition, command.ActorUserId);
        if (result is not null) return Conflict(result);
        if (version.LifecycleState == GccV2ContextLifecycle.Approved)
        {
            if (version.ExtractionState != "ready" || version.IndexState != "ready")
                return Conflict("Knowledge must be extracted and indexed before approval.");
            version.Asset.CurrentVersionId = version.Id;
        }
        AddAudit(version.Asset.OwnerUserId, "knowledge", version.AssetId, version.Id,
            command.ActorUserId, transition, null, version.LifecycleState);
        await db.SaveChangesAsync(ct);
        return Ok(version);
    }

    [HttpPost("catalogs")]
    public async Task<ActionResult<object>> CreateCatalog([FromBody] CreateCatalogCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId) || string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("ownerUserId and name are required");
        object catalog = command.Kind switch
        {
            "audience" => new GccV2Audience { OwnerUserId = command.OwnerUserId, Name = command.Name, Description = command.Description },
            "style_guide" => new GccV2StyleGuide { OwnerUserId = command.OwnerUserId, Name = command.Name, Description = command.Description },
            "visual_guideline" => new GccV2VisualGuideline { OwnerUserId = command.OwnerUserId, Name = command.Name, Description = command.Description },
            "product_schema" => new GccV2ProductSchema { OwnerUserId = command.OwnerUserId, Name = command.Name, Description = command.Description },
            "product" => new GccV2Product { OwnerUserId = command.OwnerUserId, Name = command.Name, Description = command.Description },
            _ => null!,
        };
        if (catalog is null) return BadRequest("Unsupported catalog kind.");
        db.Add(catalog);
        var id = ((GccV2OwnedCatalog)catalog).Id;
        AddAudit(command.OwnerUserId, command.Kind, id, null, command.ActorUserId, "create", null, "draft");
        await db.SaveChangesAsync(ct);
        return Created("", catalog);
    }

    [HttpGet("catalogs/{kind}")]
    public async Task<ActionResult<object>> ListCatalogs(
        string kind, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        object? result = kind switch
        {
            "audience" => await db.GccV2Audiences.AsNoTracking().Where(x => x.OwnerUserId == ownerUserId && !x.IsRetired)
                .Include(x => x.Versions).OrderBy(x => x.Name).ToListAsync(ct),
            "style_guide" => await db.GccV2StyleGuides.AsNoTracking().Where(x => x.OwnerUserId == ownerUserId && !x.IsRetired)
                .Include(x => x.Versions).OrderBy(x => x.Name).ToListAsync(ct),
            "visual_guideline" => await db.GccV2VisualGuidelines.AsNoTracking().Where(x => x.OwnerUserId == ownerUserId && !x.IsRetired)
                .Include(x => x.Versions).OrderBy(x => x.Name).ToListAsync(ct),
            "product_schema" => await db.GccV2ProductSchemas.AsNoTracking().Where(x => x.OwnerUserId == ownerUserId && !x.IsRetired)
                .Include(x => x.Versions).OrderBy(x => x.Name).ToListAsync(ct),
            "product" => await db.GccV2Products.AsNoTracking().Where(x => x.OwnerUserId == ownerUserId && !x.IsRetired)
                .Include(x => x.Versions).OrderBy(x => x.Name).ToListAsync(ct),
            _ => null,
        };
        return result is null ? BadRequest("Unsupported catalog kind.") : Ok(result);
    }

    [HttpGet("catalogs/{kind}/{catalogId:guid}")]
    public async Task<ActionResult<object>> GetCatalog(
        string kind, Guid catalogId, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        object? result = kind switch
        {
            "audience" => await db.GccV2Audiences.AsNoTracking().Include(x => x.Versions)
                .SingleOrDefaultAsync(x => x.Id == catalogId && x.OwnerUserId == ownerUserId, ct),
            "style_guide" => await db.GccV2StyleGuides.AsNoTracking().Include(x => x.Versions)
                .SingleOrDefaultAsync(x => x.Id == catalogId && x.OwnerUserId == ownerUserId, ct),
            "visual_guideline" => await db.GccV2VisualGuidelines.AsNoTracking().Include(x => x.Versions)
                .SingleOrDefaultAsync(x => x.Id == catalogId && x.OwnerUserId == ownerUserId, ct),
            "product_schema" => await db.GccV2ProductSchemas.AsNoTracking().Include(x => x.Versions)
                .SingleOrDefaultAsync(x => x.Id == catalogId && x.OwnerUserId == ownerUserId, ct),
            "product" => await db.GccV2Products.AsNoTracking().Include(x => x.Versions)
                .SingleOrDefaultAsync(x => x.Id == catalogId && x.OwnerUserId == ownerUserId, ct),
            _ => null,
        };
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPatch("catalogs/{kind}/{catalogId:guid}")]
    public async Task<ActionResult<object>> PatchCatalog(
        string kind, Guid catalogId, [FromBody] PatchCatalogCommand command, CancellationToken ct)
    {
        GccV2OwnedCatalog? value = kind switch
        {
            "audience" => await db.GccV2Audiences.SingleOrDefaultAsync(
                x => x.Id == catalogId && x.OwnerUserId == command.OwnerUserId, ct),
            "style_guide" => await db.GccV2StyleGuides.SingleOrDefaultAsync(
                x => x.Id == catalogId && x.OwnerUserId == command.OwnerUserId, ct),
            "visual_guideline" => await db.GccV2VisualGuidelines.SingleOrDefaultAsync(
                x => x.Id == catalogId && x.OwnerUserId == command.OwnerUserId, ct),
            "product_schema" => await db.GccV2ProductSchemas.SingleOrDefaultAsync(
                x => x.Id == catalogId && x.OwnerUserId == command.OwnerUserId, ct),
            "product" => await db.GccV2Products.SingleOrDefaultAsync(
                x => x.Id == catalogId && x.OwnerUserId == command.OwnerUserId, ct),
            _ => null,
        };
        if (value is null) return NotFound();
        if (command.Name is not null) value.Name = command.Name.Trim();
        if (command.Description is not null) value.Description = command.Description.Trim();
        if (command.IsRetired is not null) value.IsRetired = command.IsRetired.Value;
        value.Revision++;
        value.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(command.OwnerUserId, kind, catalogId, null, command.ActorUserId, "update", null, null);
        await db.SaveChangesAsync(ct);
        return Ok(value);
    }

    [HttpPost("catalogs/{kind}/{catalogId:guid}/versions")]
    public async Task<ActionResult<object>> CreateCatalogVersion(
        string kind, Guid catalogId, [FromBody] CreateCatalogVersionCommand command, CancellationToken ct)
    {
        if (!IsSha256(command.CanonicalSha256)) return BadRequest("canonicalSha256 is invalid");
        object? version;
        switch (kind)
        {
            case "audience":
            {
                var parent = await db.GccV2Audiences.Include(x => x.Versions)
                    .SingleOrDefaultAsync(x => x.Id == catalogId && x.OwnerUserId == command.OwnerUserId, ct);
                if (parent is null) return NotFound();
                version = NewVersion(new GccV2AudienceVersion
                    { AudienceId = catalogId, DefinitionJson = command.PayloadJson, Locale = command.Locale ?? "en" },
                    parent.Versions, command);
                break;
            }
            case "style_guide":
            {
                var parent = await db.GccV2StyleGuides.Include(x => x.Versions)
                    .SingleOrDefaultAsync(x => x.Id == catalogId && x.OwnerUserId == command.OwnerUserId, ct);
                if (parent is null) return NotFound();
                version = NewVersion(new GccV2StyleGuideVersion
                    { StyleGuideId = catalogId, PolicyJson = command.PayloadJson, Locale = command.Locale ?? "en" },
                    parent.Versions, command);
                break;
            }
            case "visual_guideline":
            {
                var parent = await db.GccV2VisualGuidelines.Include(x => x.Versions)
                    .SingleOrDefaultAsync(x => x.Id == catalogId && x.OwnerUserId == command.OwnerUserId, ct);
                if (parent is null) return NotFound();
                version = NewVersion(new GccV2VisualGuidelineVersion
                    { VisualGuidelineId = catalogId, PolicyJson = command.PayloadJson, Locale = command.Locale ?? "en" },
                    parent.Versions, command);
                break;
            }
            case "product_schema":
            {
                var parent = await db.GccV2ProductSchemas.Include(x => x.Versions)
                    .SingleOrDefaultAsync(x => x.Id == catalogId && x.OwnerUserId == command.OwnerUserId, ct);
                if (parent is null) return NotFound();
                version = NewVersion(new GccV2ProductSchemaVersion
                    { ProductSchemaId = catalogId, FieldsJson = command.PayloadJson }, parent.Versions, command);
                break;
            }
            case "product":
            {
                var parent = await db.GccV2Products.Include(x => x.Versions)
                    .SingleOrDefaultAsync(x => x.Id == catalogId && x.OwnerUserId == command.OwnerUserId, ct);
                if (parent is null) return NotFound();
                if (command.ProductSchemaVersionId is null) return BadRequest("productSchemaVersionId is required");
                var schemaOwned = await db.GccV2ProductSchemaVersions.AnyAsync(x =>
                    x.Id == command.ProductSchemaVersionId &&
                    db.GccV2ProductSchemas.Any(p => p.Id == x.ProductSchemaId && p.OwnerUserId == command.OwnerUserId), ct);
                if (!schemaOwned) return NotFound();
                version = NewVersion(new GccV2ProductVersion
                {
                    ProductId = catalogId, ProductSchemaVersionId = command.ProductSchemaVersionId.Value,
                    FieldValuesJson = command.PayloadJson, AttributeProvenanceJson = command.ProvenanceJson ?? "{}",
                    ApprovedClaimsJson = command.ApprovedClaimsJson ?? "[]",
                    ProhibitedClaimsJson = command.ProhibitedClaimsJson ?? "[]",
                    MandatoryDisclaimersJson = command.MandatoryDisclaimersJson ?? "[]",
                }, parent.Versions, command);
                break;
            }
            default: return BadRequest("Unsupported catalog kind.");
        }
        db.Add(version);
        AddAudit(command.OwnerUserId, kind, catalogId, ((GccV2GovernedVersion)version).Id,
            command.ActorUserId, "version_create", null, "draft");
        await db.SaveChangesAsync(ct);
        return Created("", version);
    }

    [HttpPost("catalogs/{kind}/versions/{versionId:guid}/{transition}")]
    public async Task<ActionResult<object>> TransitionCatalogVersion(
        string kind, Guid versionId, string transition, [FromBody] TransitionCommand command, CancellationToken ct)
    {
        GccV2GovernedVersion? version;
        GccV2OwnedCatalog? parent;
        switch (kind)
        {
            case "audience":
                var av = await db.GccV2AudienceVersions.Include(x => x.Audience)
                    .SingleOrDefaultAsync(x => x.Id == versionId && x.Audience.OwnerUserId == command.OwnerUserId, ct);
                version = av; parent = av?.Audience; break;
            case "style_guide":
                var sv = await db.GccV2StyleGuideVersions.Include(x => x.StyleGuide)
                    .SingleOrDefaultAsync(x => x.Id == versionId && x.StyleGuide.OwnerUserId == command.OwnerUserId, ct);
                version = sv; parent = sv?.StyleGuide; break;
            case "visual_guideline":
                var vv = await db.GccV2VisualGuidelineVersions.Include(x => x.VisualGuideline)
                    .SingleOrDefaultAsync(x => x.Id == versionId && x.VisualGuideline.OwnerUserId == command.OwnerUserId, ct);
                version = vv; parent = vv?.VisualGuideline; break;
            case "product_schema":
                var psv = await db.GccV2ProductSchemaVersions.Include(x => x.ProductSchema)
                    .SingleOrDefaultAsync(x => x.Id == versionId && x.ProductSchema.OwnerUserId == command.OwnerUserId, ct);
                version = psv; parent = psv?.ProductSchema; break;
            case "product":
                var pv = await db.GccV2ProductVersions.Include(x => x.Product)
                    .SingleOrDefaultAsync(x => x.Id == versionId && x.Product.OwnerUserId == command.OwnerUserId, ct);
                version = pv; parent = pv?.Product; break;
            default: return BadRequest("Unsupported catalog kind.");
        }
        if (version is null || parent is null) return NotFound();
        var conflict = ApplyTransition(version, transition, command.ActorUserId);
        if (conflict is not null) return Conflict(conflict);
        if (version.LifecycleState == GccV2ContextLifecycle.Approved) parent.CurrentVersionId = version.Id;
        AddAudit(command.OwnerUserId, kind, parent.Id, version.Id, command.ActorUserId,
            transition, null, version.LifecycleState);
        await db.SaveChangesAsync(ct);
        return Ok(version);
    }

    [HttpPost("attachments")]
    public async Task<ActionResult<GccV2RunAttachment>> CreateAttachment(
        [FromBody] CreateAttachmentCommand command, CancellationToken ct)
    {
        if (await db.GccV2RunAttachments.AnyAsync(x => x.ObjectKey == command.ObjectKey, ct))
            return Conflict("Object key already registered.");
        var attachment = new GccV2RunAttachment
        {
            OwnerUserId = command.OwnerUserId, CreateId = command.CreateId, ObjectKey = command.ObjectKey,
            SafeFileName = command.SafeFileName, MediaType = command.MediaType, ByteSize = command.ByteSize,
            Sha256 = command.Sha256, UploadExpiresAtUtc = command.UploadExpiresAtUtc,
            RetainUntilUtc = command.RetainUntilUtc,
        };
        db.GccV2RunAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return Created("", attachment);
    }

    [HttpPost("attachments/{id:guid}/finalize")]
    public async Task<ActionResult<GccV2RunAttachment>> FinalizeAttachment(
        Guid id, [FromBody] FinalizeAttachmentCommand command, CancellationToken ct)
    {
        var attachment = await db.GccV2RunAttachments.SingleOrDefaultAsync(
            x => x.Id == id && x.OwnerUserId == command.OwnerUserId, ct);
        if (attachment is null) return NotFound();
        if (attachment.FinalizedAtUtc is not null) return Conflict("Upload was already finalized.");
        if (attachment.UploadExpiresAtUtc <= DateTimeOffset.UtcNow) return Conflict("Upload expired.");
        if (attachment.ByteSize != command.ByteSize || attachment.Sha256 != command.Sha256)
            return Conflict("Final object metadata does not match the upload grant.");
        attachment.FinalizedAtUtc = DateTimeOffset.UtcNow;
        attachment.IngestionState = "queued";
        var job = new GccV2ContextIngestionJob
            { OwnerUserId = attachment.OwnerUserId, TargetKind = "run_attachment", TargetId = attachment.Id };
        db.GccV2ContextIngestionJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_notify({IngestionNotifyChannel}, {job.Id.ToString()})", ct);
        return Ok(attachment);
    }

    [HttpGet("attachments/{id:guid}")]
    public async Task<ActionResult<GccV2RunAttachment>> GetAttachment(
        Guid id, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        var value = await db.GccV2RunAttachments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId && x.DeletedAtUtc == null, ct);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpGet("attachments/retention-due")]
    public async Task<ActionResult<IReadOnlyList<GccV2RunAttachment>>> ListAttachmentsDueForRetention(
        [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return Ok(await db.GccV2RunAttachments.AsNoTracking()
            .Where(x => x.DeletedAtUtc == null && x.RetainUntilUtc <= now)
            .OrderBy(x => x.RetainUntilUtc)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(ct));
    }

    [HttpDelete("attachments/{id:guid}")]
    public async Task<IActionResult> DeleteAttachment(
        Guid id, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        var value = await db.GccV2RunAttachments.SingleOrDefaultAsync(
            x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
        if (value is null) return NotFound();
        var pinned = await db.GccV2RunContextManifestEntries.AnyAsync(
            x => x.ContextKind == "run_attachment" && x.StableId == id, ct);
        if (pinned && value.RetainUntilUtc > DateTimeOffset.UtcNow)
            return Conflict("Attachment is pinned by a manifest until retention expires.");
        value.DeletedAtUtc = DateTimeOffset.UtcNow;
        return await db.SaveChangesAsync(ct) > 0 ? NoContent() : Conflict();
    }

    [HttpPost("selections")]
    public async Task<ActionResult<GccV2ContextSelection>> CreateSelection(
        [FromBody] CreateSelectionCommand command, CancellationToken ct)
    {
        var value = new GccV2ContextSelection
        {
            OwnerUserId = command.OwnerUserId, CreateId = command.CreateId,
            SelectionJson = command.SelectionJson, CreatedBy = command.ActorUserId,
        };
        db.GccV2ContextSelections.Add(value);
        await db.SaveChangesAsync(ct);
        return Created("", value);
    }

    [HttpPost("manifests")]
    public async Task<ActionResult<GccV2RunContextManifest>> CreateManifest(
        [FromBody] CreateManifestCommand command, CancellationToken ct)
    {
        if (!IsSha256(command.Sha256) || string.IsNullOrWhiteSpace(command.Signature))
            return BadRequest("A valid digest and signature are required.");
        if (await db.GccV2RunContextManifests.AnyAsync(x => x.JobId == command.JobId, ct))
            return Conflict("The job already has a context manifest.");
        var job = await db.GccV2Jobs.SingleOrDefaultAsync(
            x => x.Id == command.JobId && x.OwnerUserId == command.OwnerUserId, ct);
        if (job is null) return NotFound();
        var manifest = new GccV2RunContextManifest
        {
            Id = command.Id, OwnerUserId = command.OwnerUserId, JobId = command.JobId,
            Attempt = command.Attempt, SchemaVersion = command.SchemaVersion,
            CanonicalJson = command.CanonicalJson, Sha256 = command.Sha256,
            Signature = command.Signature, SigningKeyId = command.SigningKeyId,
            ResolverIdentity = command.ResolverIdentity, ResolvedAtUtc = command.ResolvedAtUtc,
            ReplacesManifestId = command.ReplacesManifestId,
            Entries = command.Entries.Select(x => new GccV2RunContextManifestEntry
            {
                ContextKind = x.ContextKind, StableId = x.StableId, VersionId = x.VersionId,
                VersionNumber = x.VersionNumber, ContentSha256 = x.ContentSha256,
                LifecycleDecision = x.LifecycleDecision, PermissionDecision = x.PermissionDecision,
                FreshnessDecision = x.FreshnessDecision, SelectionSource = x.SelectionSource,
                SelectedFieldIdsJson = x.SelectedFieldIdsJson, SourceModifiedAtUtc = x.SourceModifiedAtUtc,
            }).ToList(),
        };
        db.GccV2RunContextManifests.Add(manifest);
        AddAudit(command.OwnerUserId, "manifest", manifest.Id, null, command.OwnerUserId,
            "resolved", null, "immutable");
        await db.SaveChangesAsync(ct);
        return Created("", manifest);
    }

    [HttpGet("manifests/by-job/{jobId:guid}")]
    public async Task<ActionResult<GccV2RunContextManifest>> GetManifest(
        Guid jobId, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        var value = await db.GccV2RunContextManifests.AsNoTracking()
            .Include(x => x.Entries)
            .SingleOrDefaultAsync(x => x.JobId == jobId && x.OwnerUserId == ownerUserId, ct);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpGet("audit/{kind}/{targetId:guid}")]
    public async Task<ActionResult<IReadOnlyList<GccV2ContextAuditEvent>>> GetAudit(
        string kind, Guid targetId, [FromQuery] string ownerUserId, CancellationToken ct) =>
        Ok(await db.GccV2ContextAuditEvents.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && x.TargetKind == kind && x.TargetId == targetId)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(ct));

    [HttpGet("findings/{kind}/{targetId:guid}")]
    public async Task<ActionResult<IReadOnlyList<GccV2ContextFinding>>> GetFindings(
        string kind, Guid targetId, [FromQuery] string ownerUserId, CancellationToken ct) =>
        Ok(await db.GccV2ContextFindings.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && x.TargetKind == kind && x.TargetId == targetId)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(ct));

    [HttpPost("findings")]
    public async Task<ActionResult<GccV2ContextFinding>> CreateFinding(
        [FromBody] CreateFindingCommand command, CancellationToken ct)
    {
        var finding = new GccV2ContextFinding
        {
            OwnerUserId = command.OwnerUserId, TargetKind = command.TargetKind,
            TargetId = command.TargetId, Severity = command.Severity,
            Code = command.Code, Message = command.Message,
        };
        db.GccV2ContextFindings.Add(finding);
        AddAudit(command.OwnerUserId, command.TargetKind, command.TargetId, null,
            command.ActorUserId, "finding_create", null, "open");
        await db.SaveChangesAsync(ct);
        return Created("", finding);
    }

    [HttpPatch("findings/{findingId:guid}")]
    public async Task<ActionResult<GccV2ContextFinding>> DisposeFinding(
        Guid findingId, [FromBody] DisposeFindingCommand command, CancellationToken ct)
    {
        if (command.Disposition is not ("accepted" or "resolved" or "false_positive"))
            return BadRequest("Invalid disposition.");
        if (string.IsNullOrWhiteSpace(command.ReviewerRationale))
            return BadRequest("reviewerRationale is required.");
        var finding = await db.GccV2ContextFindings.SingleOrDefaultAsync(
            x => x.Id == findingId && x.OwnerUserId == command.OwnerUserId, ct);
        if (finding is null) return NotFound();
        finding.Disposition = command.Disposition;
        finding.ReviewerUserId = command.ActorUserId;
        finding.ReviewerRationale = command.ReviewerRationale;
        finding.DisposedAtUtc = DateTimeOffset.UtcNow;
        finding.Revision++;
        AddAudit(command.OwnerUserId, finding.TargetKind, finding.TargetId, null,
            command.ActorUserId, "finding_dispose", "open", command.Disposition);
        await db.SaveChangesAsync(ct);
        return Ok(finding);
    }

    [HttpGet("versions/{kind}/{versionId:guid}")]
    public async Task<ActionResult<object>> GetVersion(
        string kind, Guid versionId, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        GccV2GovernedVersionLookup? value = kind switch
        {
            "knowledge" => await db.GccV2KnowledgeAssetVersions.AsNoTracking()
                .Where(x => x.Id == versionId && x.Asset.OwnerUserId == ownerUserId)
                .Select(x => new GccV2GovernedVersionLookup(kind, x.AssetId, x.Id, x.VersionNumber,
                    x.SchemaVersion, x.CanonicalSha256, x.LifecycleState, x.SourceModifiedAtUtc,
                    x.EffectiveFromUtc, x.EffectiveUntilUtc, x.ExtractionState, x.IndexState, null))
                .SingleOrDefaultAsync(ct),
            "audience" => await db.GccV2AudienceVersions.AsNoTracking()
                .Where(x => x.Id == versionId && x.Audience.OwnerUserId == ownerUserId)
                .Select(x => new GccV2GovernedVersionLookup(kind, x.AudienceId, x.Id, x.VersionNumber,
                    x.SchemaVersion, x.CanonicalSha256, x.LifecycleState, null,
                    x.EffectiveFromUtc, x.EffectiveUntilUtc, "ready", "ready", x.DefinitionJson))
                .SingleOrDefaultAsync(ct),
            "style_guide" => await db.GccV2StyleGuideVersions.AsNoTracking()
                .Where(x => x.Id == versionId && x.StyleGuide.OwnerUserId == ownerUserId)
                .Select(x => new GccV2GovernedVersionLookup(kind, x.StyleGuideId, x.Id, x.VersionNumber,
                    x.SchemaVersion, x.CanonicalSha256, x.LifecycleState, null,
                    x.EffectiveFromUtc, x.EffectiveUntilUtc, "ready", "ready", x.PolicyJson))
                .SingleOrDefaultAsync(ct),
            "visual_guideline" => await db.GccV2VisualGuidelineVersions.AsNoTracking()
                .Where(x => x.Id == versionId && x.VisualGuideline.OwnerUserId == ownerUserId)
                .Select(x => new GccV2GovernedVersionLookup(kind, x.VisualGuidelineId, x.Id, x.VersionNumber,
                    x.SchemaVersion, x.CanonicalSha256, x.LifecycleState, null,
                    x.EffectiveFromUtc, x.EffectiveUntilUtc, "ready", "ready", x.PolicyJson))
                .SingleOrDefaultAsync(ct),
            "product_schema" => await db.GccV2ProductSchemaVersions.AsNoTracking()
                .Where(x => x.Id == versionId && x.ProductSchema.OwnerUserId == ownerUserId)
                .Select(x => new GccV2GovernedVersionLookup(kind, x.ProductSchemaId, x.Id, x.VersionNumber,
                    x.SchemaVersion, x.CanonicalSha256, x.LifecycleState, null,
                    x.EffectiveFromUtc, x.EffectiveUntilUtc, "ready", "ready", x.FieldsJson))
                .SingleOrDefaultAsync(ct),
            "product" => await db.GccV2ProductVersions.AsNoTracking()
                .Where(x => x.Id == versionId && x.Product.OwnerUserId == ownerUserId)
                .Select(x => new GccV2GovernedVersionLookup(kind, x.ProductId, x.Id, x.VersionNumber,
                    x.SchemaVersion, x.CanonicalSha256, x.LifecycleState, null,
                    x.EffectiveFromUtc, x.EffectiveUntilUtc, "ready", "ready", x.FieldValuesJson,
                    x.ApprovedClaimsJson, x.ProhibitedClaimsJson, x.MandatoryDisclaimersJson,
                    x.ProductSchemaVersionId))
                .SingleOrDefaultAsync(ct),
            _ => null,
        };
        return value is null ? NotFound() : Ok(value);
    }

    [HttpGet("current-versions/{kind}/{stableId:guid}")]
    public async Task<ActionResult<GccV2GovernedVersionLookup>> GetCurrentVersion(
        string kind, Guid stableId, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        Guid? versionId = kind switch
        {
            "knowledge" => await db.GccV2KnowledgeAssets.Where(x => x.Id == stableId && x.OwnerUserId == ownerUserId)
                .Select(x => x.CurrentVersionId).SingleOrDefaultAsync(ct),
            "audience" => await db.GccV2Audiences.Where(x => x.Id == stableId && x.OwnerUserId == ownerUserId)
                .Select(x => x.CurrentVersionId).SingleOrDefaultAsync(ct),
            "style_guide" => await db.GccV2StyleGuides.Where(x => x.Id == stableId && x.OwnerUserId == ownerUserId)
                .Select(x => x.CurrentVersionId).SingleOrDefaultAsync(ct),
            "visual_guideline" => await db.GccV2VisualGuidelines.Where(x => x.Id == stableId && x.OwnerUserId == ownerUserId)
                .Select(x => x.CurrentVersionId).SingleOrDefaultAsync(ct),
            "product_schema" => await db.GccV2ProductSchemas.Where(x => x.Id == stableId && x.OwnerUserId == ownerUserId)
                .Select(x => x.CurrentVersionId).SingleOrDefaultAsync(ct),
            "product" => await db.GccV2Products.Where(x => x.Id == stableId && x.OwnerUserId == ownerUserId)
                .Select(x => x.CurrentVersionId).SingleOrDefaultAsync(ct),
            _ => null,
        };
        if (versionId is null) return NotFound();
        var result = await GetVersion(kind, versionId.Value, ownerUserId, ct);
        return result.Result is OkObjectResult { Value: GccV2GovernedVersionLookup lookup }
            ? Ok(lookup)
            : NotFound();
    }

    private static T NewVersion<T>(T version, IReadOnlyCollection<T> existing, CreateCatalogVersionCommand command)
        where T : GccV2GovernedVersion
    {
        version.VersionNumber = existing.Count == 0 ? 1 : existing.Max(x => x.VersionNumber) + 1;
        version.SchemaVersion = command.SchemaVersion;
        version.CanonicalSha256 = command.CanonicalSha256;
        version.CreatedBy = command.ActorUserId;
        version.EffectiveFromUtc = command.EffectiveFromUtc;
        version.EffectiveUntilUtc = command.EffectiveUntilUtc;
        return version;
    }

    private static string? ApplyTransition(GccV2GovernedVersion version, string transition, string actor)
    {
        var next = transition switch
        {
            "review" when version.LifecycleState == GccV2ContextLifecycle.Draft => GccV2ContextLifecycle.InReview,
            "approve" when version.LifecycleState == GccV2ContextLifecycle.InReview => GccV2ContextLifecycle.Approved,
            "deprecate" when version.LifecycleState == GccV2ContextLifecycle.Approved => GccV2ContextLifecycle.Deprecated,
            "revoke" when version.LifecycleState is GccV2ContextLifecycle.Approved or GccV2ContextLifecycle.Deprecated
                => GccV2ContextLifecycle.Revoked,
            _ => null,
        };
        if (next is null || !LifecycleStates.Contains(next))
            return $"Transition '{transition}' is invalid from '{version.LifecycleState}'.";
        version.LifecycleState = next;
        if (next == GccV2ContextLifecycle.Approved)
        {
            version.ReviewedAtUtc = DateTimeOffset.UtcNow;
            version.ReviewedBy = actor;
        }
        return null;
    }

    private void AddAudit(string owner, string kind, Guid targetId, Guid? versionId, string actor,
        string action, string? before, string? after) =>
        db.GccV2ContextAuditEvents.Add(new GccV2ContextAuditEvent
        {
            OwnerUserId = owner, TargetKind = kind, TargetId = targetId, VersionId = versionId,
            ActorUserId = actor, Action = action, BeforeState = before, AfterState = after,
            RequestId = HttpContext.TraceIdentifier,
        });

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public sealed record CreateKnowledgeCommand(
        string OwnerUserId, string Name, string? Description, string? TagsJson,
        string? Kind, string ActorUserId);
    public sealed record PatchCatalogCommand(
        string OwnerUserId, string ActorUserId, string? Name, string? Description, bool? IsRetired);
    public sealed record CreateKnowledgeVersionCommand(
        string OwnerUserId, int SchemaVersion, string CanonicalSha256, string ContentSha256,
        string MediaType, string? Language, string? SourceDescriptorJson, string? ProvenanceJson,
        DateTimeOffset? SourceModifiedAtUtc, string ActorUserId);
    public sealed record AddKnowledgeResourceCommand(
        string OwnerUserId, string ResourceKind, string ObjectKey, long ByteSize, string Sha256,
        string MediaType, string SafeFileName, string ScanState, string? ParserName,
        string? ParserVersion, string? ExtractionSha256, string? CoordinatesJson);
    public sealed record QueueKnowledgeIngestionCommand(
        string OwnerUserId, string ObjectKey, long ByteSize, string Sha256);
    public sealed record TransitionIngestionJobCommand(
        string Status, int ProgressPercent, string EventType, string? EventPayloadJson,
        string? TerminalError, string? IndexState);
    public sealed record TransitionCommand(string OwnerUserId, string ActorUserId, string? Reason);
    public sealed record CreateCatalogCommand(
        string Kind, string OwnerUserId, string Name, string? Description, string ActorUserId);
    public sealed record CreateCatalogVersionCommand(
        string OwnerUserId, int SchemaVersion, string CanonicalSha256, string PayloadJson,
        string? Locale, string? ProvenanceJson, Guid? ProductSchemaVersionId,
        string? ApprovedClaimsJson, string? ProhibitedClaimsJson, string? MandatoryDisclaimersJson,
        DateTimeOffset? EffectiveFromUtc, DateTimeOffset? EffectiveUntilUtc, string ActorUserId);
    public sealed record CreateAttachmentCommand(
        string OwnerUserId, Guid CreateId, string ObjectKey, string SafeFileName, string MediaType,
        long ByteSize, string Sha256, DateTimeOffset UploadExpiresAtUtc, DateTimeOffset RetainUntilUtc);
    public sealed record FinalizeAttachmentCommand(string OwnerUserId, long ByteSize, string Sha256);
    public sealed record CreateSelectionCommand(
        string OwnerUserId, Guid? CreateId, string SelectionJson, string ActorUserId);
    public sealed record CreateManifestCommand(
        Guid Id, string OwnerUserId, Guid JobId, int Attempt, int SchemaVersion, string CanonicalJson,
        string Sha256, string Signature, string SigningKeyId, string ResolverIdentity,
        DateTimeOffset ResolvedAtUtc, Guid? ReplacesManifestId, IReadOnlyList<CreateManifestEntryCommand> Entries);
    public sealed record CreateManifestEntryCommand(
        string ContextKind, Guid StableId, Guid? VersionId, int? VersionNumber, string ContentSha256,
        string LifecycleDecision, string PermissionDecision, string FreshnessDecision,
        string SelectionSource, string? SelectedFieldIdsJson, DateTimeOffset? SourceModifiedAtUtc);
    public sealed record CreateFindingCommand(
        string OwnerUserId, string TargetKind, Guid TargetId, string Severity,
        string Code, string Message, string ActorUserId);
    public sealed record DisposeFindingCommand(
        string OwnerUserId, string Disposition, string ReviewerRationale, string ActorUserId);
    public sealed record GccV2GovernedVersionLookup(
        string Kind, Guid StableId, Guid VersionId, int VersionNumber, int SchemaVersion,
        string CanonicalSha256, string LifecycleState, DateTimeOffset? SourceModifiedAtUtc,
        DateTimeOffset? EffectiveFromUtc, DateTimeOffset? EffectiveUntilUtc,
        string ExtractionState, string IndexState, string? PayloadJson,
        string? ApprovedClaimsJson = null, string? ProhibitedClaimsJson = null,
        string? MandatoryDisclaimersJson = null, Guid? ProductSchemaVersionId = null);
    public sealed record GccV2ContextQuotaUsage(
        long KnowledgeBytes, long OwnerAttachmentBytes, long RunAttachmentBytes);
}
