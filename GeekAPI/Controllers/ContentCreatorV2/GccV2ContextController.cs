using System.Text;
using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Context;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GeekAPI.Controllers.ContentCreatorV2;

[ApiController]
[Route("api/geek-content-creator-v2")]
public sealed class GccV2ContextController(
    ICurrentUserContext user,
    HttpGccV2Repository repository,
    IGccV2ContextObjectStore objectStore,
    IGccV2KnowledgeIndexer knowledgeIndexer,
    GccV2ContextConnectorRegistry connectors,
    GccV2ContextResolver resolver,
    GccV2ContextIngestionWake ingestionWake,
    GccV2UrlKnowledgeService urlKnowledge,
    GccV2GscKnowledgeService gscKnowledge,
    GccV2DriveKnowledgeService driveKnowledge,
    GccV2UrlAttachmentService urlAttachment,
    GccV2JobEventWriter jobEvents,
    GccV2JobWake jobWake,
    IConfiguration configuration) : ControllerBase, IAsyncActionFilter
{
    private const long DefaultMaxFileBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> SupportedMediaTypes =
    [
        "text/plain", "text/markdown", "text/html", "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    ];

    [HttpGet("context/capabilities")]
    public ActionResult<object> Capabilities()
    {
        if (!user.IsAuthenticated) return Unauthorized();
        return Ok(new
        {
            manifestSchema = "run-context-manifest.v1",
            authorization = "owner-only",
            features = new
            {
                catalogs = FeatureEnabled("Catalogs"),
                ingestion = FeatureEnabled("Ingestion"),
                resolution = FeatureEnabled("Resolution"),
                manifestRetrieval = FeatureEnabled("ManifestRetrieval"),
            },
            supportedMediaTypes = SupportedMediaTypes.Order(StringComparer.Ordinal),
            blockedModalities = new
            {
                image = "No approved local OCR/vision model is configured.",
                audio = "No approved local transcription model is configured.",
                video = "No approved local transcript/keyframe pipeline is configured.",
            },
            connectorIds = connectors.ConnectorIds,
            quotas = new { maxFileBytes = MaxFileBytes, maxOwnerBytes = MaxOwnerBytes, maxRunBytes = MaxRunBytes },
        });
    }

    [HttpGet("knowledge")]
    public async Task<ActionResult<IReadOnlyList<GccV2KnowledgeAssetDto>>> ListKnowledge(CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        return Ok(await repository.ListKnowledgeAsync(Owner, ct));
    }

    [HttpGet("knowledge/{assetId:guid}")]
    public async Task<ActionResult<GccV2KnowledgeAssetDto>> GetKnowledge(Guid assetId, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var value = await repository.GetKnowledgeAsync(assetId, Owner, ct);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpPost("knowledge")]
    public async Task<ActionResult<GccV2KnowledgeAssetDto>> CreateKnowledge(
        [FromBody] CreateKnowledgeRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "name is required" });
        return Ok(await repository.CreateKnowledgeAsync(new(
            Owner, request.Name, request.Description,
            JsonSerializer.Serialize(request.Tags ?? []), request.Kind, Owner), ct));
    }

    [HttpPatch("knowledge/{assetId:guid}")]
    public async Task<ActionResult<GccV2KnowledgeAssetDto>> PatchKnowledge(
        Guid assetId, [FromBody] PatchKnowledgeRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (await repository.GetKnowledgeAsync(assetId, Owner, ct) is null) return NotFound();
        return Ok(await repository.PatchKnowledgeAsync(assetId,
            new(Owner, Owner, request.Name, request.Description, request.IsRetired), ct));
    }

    [HttpPost("knowledge/{assetId:guid}/versions")]
    public async Task<ActionResult<GccV2KnowledgeAssetVersionDto>> CreateKnowledgeVersion(
        Guid assetId, [FromBody] CreateKnowledgeVersionRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (await repository.GetKnowledgeAsync(assetId, Owner, ct) is null) return NotFound();
        if (!IsSha256(request.ContentSha256)) return BadRequest(new { error = "contentSha256 is invalid" });
        var canonical = GccV2CanonicalJson.Serialize(new
        {
            assetId, schemaVersion = request.SchemaVersion ?? 1, request.ContentSha256,
            mediaType = NormalizeMediaType(request.MediaType), request.Language,
            sourceDescriptor = request.SourceDescriptor,
        });
        return Ok(await repository.CreateKnowledgeVersionAsync(assetId, new(
            Owner, request.SchemaVersion ?? 1, GccV2CanonicalJson.Sha256(canonical),
            request.ContentSha256, NormalizeMediaType(request.MediaType), request.Language,
            request.SourceDescriptor?.GetRawText(), request.Provenance?.GetRawText(),
            request.SourceModifiedAtUtc, Owner), ct));
    }

    [HttpPost("knowledge/{assetId:guid}/{transition:regex(^review|approve|deprecate|revoke$)}")]
    public async Task<ActionResult<GccV2KnowledgeAssetVersionDto>> TransitionKnowledge(
        Guid assetId, string transition, [FromBody] TransitionContextRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var asset = await repository.GetKnowledgeAsync(assetId, Owner, ct);
        if (asset is null) return NotFound();
        var versionId = request.VersionId ?? asset.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault()?.Id;
        if (versionId is null) return BadRequest(new { error = "versionId is required" });
        var transitioned = await repository.TransitionKnowledgeAsync(versionId.Value, transition,
            new(Owner, Owner, request.Reason), ct);
        if (transition == "revoke")
        {
            var resources = asset.Versions.Single(x => x.Id == versionId.Value).Resources
                ?.Where(x => x.ResourceKind == "normalized_text").ToList() ?? [];
            foreach (var resource in resources)
                await knowledgeIndexer.DeleteAsync(
                    new(Owner, versionId.Value, resource.Id), ct);
        }
        return Ok(transitioned);
    }

    [HttpPost("knowledge/{assetId:guid}/versions/{versionId:guid}/reindex")]
    public async Task<ActionResult<object>> ReindexKnowledge(
        Guid assetId, Guid versionId, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var asset = await repository.GetKnowledgeAsync(assetId, Owner, ct);
        var version = asset?.Versions.SingleOrDefault(x => x.Id == versionId);
        var resource = version?.Resources?.SingleOrDefault(x => x.ResourceKind == "normalized_text");
        if (version is null || resource is null) return NotFound();
        if (version.LifecycleState == "revoked") return Conflict(new { error = "Revoked Knowledge cannot be reindexed." });
        await using var stream = await objectStore.OpenReadAsync(resource.ObjectKey, ct);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(ct);
        if (content.Length > 2_000_000) return Conflict(new { error = "Normalized Knowledge exceeds index limits." });
        await knowledgeIndexer.IndexAsync(new(
            Owner, assetId, versionId, resource.Id, version.ContentSha256,
            resource.Sha256, resource.ObjectKey, resource.MediaType,
            resource.ParserName ?? "GeekAPI.LocalTextExtractor",
            resource.ParserVersion ?? "1", content, version.LifecycleState,
            JsonSerializer.Deserialize<JsonElement>(resource.CoordinatesJson)), ct);
        return Accepted(new { assetId, versionId, resourceId = resource.Id, state = "indexed" });
    }

    [HttpPost("knowledge/uploads")]
    public async Task<ActionResult<object>> IssueKnowledgeUpload(
        [FromBody] KnowledgeUploadRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var error = ValidateUpload(request.FileName, request.MediaType, request.ByteSize, request.Sha256);
        if (error is not null) return BadRequest(new { error });
        var usage = await repository.GetContextQuotaUsageAsync(Owner, ct: ct);
        if (usage is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (usage.KnowledgeBytes + usage.OwnerAttachmentBytes + request.ByteSize > MaxOwnerBytes)
        {
            GccV2ContextMetrics.UploadQuotaRejections.Add(
                1, new KeyValuePair<string, object?>("scope", "owner"));
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { error = $"Owner context quota of {MaxOwnerBytes} bytes would be exceeded." });
        }
        GccV2ContextMetrics.UploadBytes.Add(
            request.ByteSize, new KeyValuePair<string, object?>("kind", "knowledge"));
        var asset = request.AssetId is { } assetId
            ? await repository.GetKnowledgeAsync(assetId, Owner, ct)
            : await repository.CreateKnowledgeAsync(new(
                Owner, Path.GetFileNameWithoutExtension(SafeFileName(request.FileName)),
                "Uploaded governed Knowledge source.", "[]", "uploaded_document", Owner), ct);
        if (asset is null) return NotFound();
        var objectKey = $"content-creator-v2/{Owner}/knowledge/{asset.Id}/{Guid.NewGuid():N}/original";
        var canonical = GccV2CanonicalJson.Serialize(new
        {
            assetId = asset.Id, contentSha256 = request.Sha256, mediaType = request.MediaType,
            source = "direct_upload", safeFileName = SafeFileName(request.FileName),
        });
        var version = await repository.CreateKnowledgeVersionAsync(asset.Id, new(
            Owner, 1, GccV2CanonicalJson.Sha256(canonical), request.Sha256,
            NormalizeMediaType(request.MediaType), request.Language,
            JsonSerializer.Serialize(new { type = "direct_upload", objectKey }),
            JsonSerializer.Serialize(new { uploadedBy = Owner }), null, Owner), ct);
        await repository.AddKnowledgeResourceAsync(version.Id, new(
            Owner, "original", objectKey, request.ByteSize, request.Sha256,
            NormalizeMediaType(request.MediaType), SafeFileName(request.FileName), "quarantined"), ct);
        var grant = objectStore.IssuePut(objectKey, request.ByteSize,
            NormalizeMediaType(request.MediaType), TimeSpan.FromMinutes(10));
        return Ok(new
        {
            uploadId = version.Id, assetId = asset.Id, versionId = version.Id,
            uploadUrl = grant.UploadUrl, method = "PUT", headers = grant.RequiredHeaders,
            grant.ExpiresAtUtc, maxBytes = MaxFileBytes,
        });
    }

    [HttpPost("knowledge/uploads/{uploadId:guid}/complete")]
    public async Task<ActionResult<GccV2ContextIngestionJobDto>> CompleteKnowledgeUpload(
        Guid uploadId, [FromBody] CompleteUploadRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var version = await repository.GetContextVersionAsync("knowledge", uploadId, Owner, ct);
        if (version is null) return NotFound();
        var asset = await repository.GetKnowledgeAsync(version.StableId, Owner, ct);
        var resource = asset?.Versions.Single(x => x.Id == uploadId).Resources?.Single(x => x.ResourceKind == "original");
        if (resource is null) return Conflict(new { error = "Upload resource is missing." });
        var verified = await objectStore.VerifyAsync(resource.ObjectKey, resource.ByteSize, ct);
        if (verified.ByteSize != request.ByteSize || verified.Sha256 != request.Sha256
            || verified.ByteSize != resource.ByteSize || verified.Sha256 != resource.Sha256)
            return Conflict(new { error = "Stored object size or checksum does not match the upload grant." });
        var job = await repository.QueueKnowledgeIngestionAsync(uploadId,
            new(Owner, resource.ObjectKey, verified.ByteSize, verified.Sha256), ct);
        ingestionWake.Wake(job.Id);
        return Accepted(new
        {
            uploadId, assetId = version.StableId, versionId = version.VersionId,
            ingestionJobId = job.Id, state = job.Status,
        });
    }

    /// <summary>
    /// Create Knowledge from a public http(s) URL via the URL connector (SSRF-gated HTTP,
    /// with mobile Playwright fallback when extraction is thin or empty).
    /// </summary>
    [HttpPost("knowledge/from-url")]
    public async Task<ActionResult<object>> CreateKnowledgeFromUrl(
        [FromBody] KnowledgeFromUrlRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var (result, status, error, errorCode) = await urlKnowledge.IngestAsync(
            Owner, request.Url, request.Name, request.Tags, request.AssetId, ct);
        if (result is null)
        {
            return StatusCode((int)status, new
            {
                contractVersion = "gcc-knowledge-from-url.v1",
                error,
                errorCode,
            });
        }

        return Accepted(new
        {
            contractVersion = "gcc-knowledge-from-url.v1",
            connectorId = GccV2UrlContextConnector.ConnectorId,
            assetId = result.AssetId,
            versionId = result.VersionId,
            resourceId = result.ResourceId,
            finalUrl = result.FinalUrl,
            title = result.Title,
            contentCompleteness = result.ContentCompleteness,
            statusCode = result.StatusCode,
            byteSize = result.ByteSize,
            contentSha256 = result.ContentSha256,
            ingestionJobId = result.IngestionJobId,
            state = result.IngestionState,
            hydrateEngine = result.HydrateEngine,
        });
    }

    /// <summary>
    /// Create Knowledge from an owner-owned Google Search Console connection (observed queries markdown).
    /// </summary>
    [HttpPost("knowledge/from-gsc")]
    public async Task<ActionResult<object>> CreateKnowledgeFromGsc(
        [FromBody] KnowledgeFromGscRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var (result, status, error, errorCode) = await gscKnowledge.IngestAsync(
            Owner,
            request.GscConnectionId ?? Guid.Empty,
            request.StartDate,
            request.EndDate,
            request.RowLimit,
            request.Name,
            request.Tags,
            request.AssetId,
            ct);
        if (result is null)
        {
            return StatusCode((int)status, new
            {
                contractVersion = "gcc-knowledge-from-gsc.v1",
                error,
                errorCode,
            });
        }

        return Accepted(new
        {
            contractVersion = "gcc-knowledge-from-gsc.v1",
            connectorId = GccV2GscContextConnector.ConnectorId,
            assetId = result.AssetId,
            versionId = result.VersionId,
            resourceId = result.ResourceId,
            gscConnectionId = result.GscConnectionId,
            siteUrl = result.SiteUrl,
            title = result.Title,
            queryCount = result.QueryCount,
            byteSize = result.ByteSize,
            contentSha256 = result.ContentSha256,
            ingestionJobId = result.IngestionJobId,
            state = result.IngestionState,
        });
    }

    /// <summary>
    /// Create Knowledge from an owner-owned Google Drive connection (approved media types only).
    /// </summary>
    [HttpPost("knowledge/from-drive")]
    public async Task<ActionResult<object>> CreateKnowledgeFromDrive(
        [FromBody] KnowledgeFromDriveRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var (result, status, error, errorCode) = await driveKnowledge.IngestAsync(
            Owner,
            request.DriveConnectionId ?? Guid.Empty,
            request.FileIdOrUrl,
            request.Name,
            request.Tags,
            request.AssetId,
            ct);
        if (result is null)
        {
            return StatusCode((int)status, new
            {
                contractVersion = "gcc-knowledge-from-drive.v1",
                error,
                errorCode,
            });
        }

        return Accepted(new
        {
            contractVersion = "gcc-knowledge-from-drive.v1",
            connectorId = GccV2DriveContextConnector.ConnectorId,
            assetId = result.AssetId,
            versionId = result.VersionId,
            resourceId = result.ResourceId,
            driveConnectionId = result.DriveConnectionId,
            accountLabel = result.AccountLabel,
            fileId = result.FileId,
            title = result.Title,
            mediaType = result.MediaType,
            byteSize = result.ByteSize,
            contentSha256 = result.ContentSha256,
            ingestionJobId = result.IngestionJobId,
            state = result.IngestionState,
        });
    }

    [HttpGet("brand-kits")]
    public async Task<ActionResult<object>> ListBrandKits(CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var kits = await repository.ListBrandKitsByOwnerAsync(Owner, ct);
        return Ok(kits.GroupBy(x => x.DerivedFromProfileId).Select(group =>
        {
            var ordered = group.OrderByDescending(x => x.Version).ToList();
            var current = ordered.FirstOrDefault(x => x.VoiceStatus == "accepted") ?? ordered[0];
            return new
            {
                id = group.Key,
                kind = "brand-kit",
                name = BrandName(current.KitJson),
                description = "Project-site-derived Brand Kit",
                currentVersionId = current.Id,
                versions = ordered.Select(x => new
                {
                    id = x.Id,
                    versionNumber = x.Version,
                    lifecycle = x.VoiceStatus == "accepted" ? "approved" : "draft",
                    digest = x.CanonicalSha256 ?? GccV2CanonicalJson.Sha256(x.KitJson),
                    createdAtUtc = x.DerivedAtUtc,
                    createdBy = x.AcceptedByUserId ?? x.OwnerUserId,
                    freshness = "current",
                    data = JsonSerializer.Deserialize<JsonElement>(x.KitJson),
                }),
            };
        }));
    }

    /// <summary>
    /// Create an accepted Brand Kit revision with an updated typed voicePolicy overlay.
    /// Accepted kits are immutable; this always appends a new version for the profile.
    /// </summary>
    [HttpPost("brand-kits/{profileId:guid}/versions")]
    public async Task<ActionResult<object>> CreateBrandKitVersion(
        Guid profileId, [FromBody] CreateBrandKitVersionRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (request.VoicePolicy.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            || request.VoicePolicy.ValueKind != JsonValueKind.Object)
        {
            return BadRequest(new { error = "voicePolicy object is required." });
        }

        var voiceValidation = GccV2BrandVoicePolicy.Validate(request.VoicePolicy);
        if (voiceValidation is not null) return BadRequest(new { error = voiceValidation });

        var kits = (await repository.ListBrandKitsByOwnerAsync(Owner, ct))
            .Where(x => x.DerivedFromProfileId == profileId)
            .OrderByDescending(x => x.Version)
            .ToList();
        var source = kits.FirstOrDefault(x => x.VoiceStatus == "accepted") ?? kits.FirstOrDefault();
        if (source is null) return NotFound(new { error = "Brand Kit profile was not found." });

        var mergedJson = MergeVoicePolicyIntoKitJson(source.KitJson, request.VoicePolicy);
        var kitValidation = GccV2BrandVoicePolicy.ValidateKitJson(mergedJson);
        if (kitValidation is not null) return BadRequest(new { error = kitValidation });

        var created = await repository.CreateBrandKitAsync(new(
            profileId,
            source.ClientId,
            mergedJson,
            "accepted",
            Owner), ct);
        created = await repository.PatchBrandKitAsync(created.Id, new(
            VoiceStatus: "accepted",
            AcceptedAtUtc: DateTimeOffset.UtcNow,
            ActorUserId: Owner), ct);

        return Ok(new
        {
            id = created.Id,
            profileId,
            versionNumber = created.Version,
            lifecycle = "approved",
            digest = created.CanonicalSha256 ?? GccV2CanonicalJson.Sha256(created.KitJson),
            data = JsonSerializer.Deserialize<JsonElement>(created.KitJson),
        });
    }

    [HttpGet("{route:regex(^audiences|style-guides|visual-guidelines|product-schemas|products$)}")]
    public async Task<ActionResult<IReadOnlyList<JsonElement>>> ListCatalog(
        string route, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        return Ok(await repository.ListContextCatalogsAsync(ToKind(route), Owner, ct));
    }

    [HttpPost("{route:regex(^audiences|style-guides|visual-guidelines|product-schemas|products$)}")]
    public async Task<ActionResult<JsonElement>> CreateCatalog(
        string route, [FromBody] CreateCatalogRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "name is required" });
        return Ok(await repository.CreateContextCatalogAsync(
            new(ToKind(route), Owner, request.Name.Trim(), request.Description, Owner), ct));
    }

    [HttpGet("{route:regex(^audiences|style-guides|visual-guidelines|product-schemas|products$)}/{catalogId:guid}")]
    public async Task<ActionResult<JsonElement>> GetCatalog(
        string route, Guid catalogId, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var value = await repository.GetContextCatalogAsync(ToKind(route), catalogId, Owner, ct);
        return value is null ? NotFound() : Ok(value.Value);
    }

    [HttpPatch("{route:regex(^audiences|style-guides|visual-guidelines|product-schemas|products$)}/{catalogId:guid}")]
    public async Task<ActionResult<JsonElement>> PatchCatalog(
        string route, Guid catalogId, [FromBody] PatchKnowledgeRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (await repository.GetContextCatalogAsync(ToKind(route), catalogId, Owner, ct) is null)
            return NotFound();
        return Ok(await repository.PatchContextCatalogAsync(ToKind(route), catalogId,
            new(Owner, Owner, request.Name, request.Description, request.IsRetired), ct));
    }

    [HttpPost("{route:regex(^audiences|style-guides|visual-guidelines|product-schemas|products$)}/{catalogId:guid}/versions")]
    public async Task<ActionResult<JsonElement>> CreateCatalogVersion(
        string route, Guid catalogId, [FromBody] CreateCatalogVersionRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (route == "style-guides")
        {
            var styleValidation = GccV2StyleGuidePolicy.Validate(request.Payload);
            if (styleValidation is not null) return BadRequest(new { error = styleValidation });
        }
        if (route == "visual-guidelines")
        {
            var visualValidation = GccV2VisualGuidelinePolicy.Validate(request.Payload);
            if (visualValidation is not null) return BadRequest(new { error = visualValidation });
        }
        if (route == "audiences")
        {
            var audienceValidation = GccV2AudiencePolicy.Validate(request.Payload);
            if (audienceValidation is not null) return BadRequest(new { error = audienceValidation });
        }
        if (route == "product-schemas")
        {
            var schemaValidation = GccV2ProductSchemaPolicy.Validate(request.Payload);
            if (schemaValidation is not null) return BadRequest(new { error = schemaValidation });
        }
        if (route == "products")
        {
            if (request.ProductSchemaVersionId is null)
                return BadRequest(new { error = "productSchemaVersionId is required" });
            var schema = await repository.GetContextVersionAsync(
                "product_schema", request.ProductSchemaVersionId.Value, Owner, ct);
            if (schema is null || schema.LifecycleState != "approved"
                || string.IsNullOrWhiteSpace(schema.PayloadJson))
                return Conflict(new { error = "Product Schema must be owner-accessible and approved." });
            var validation = ValidateProduct(request, schema.PayloadJson);
            if (validation is not null) return BadRequest(new { error = validation });
        }
        var payloadJson = GccV2CanonicalJson.Serialize(request.Payload);
        var canonical = route == "products"
            ? GccV2CanonicalJson.Serialize(new
            {
                payload = request.Payload,
                request.ProductSchemaVersionId,
                approvedClaims = request.ApprovedClaims ?? [],
                prohibitedClaims = request.ProhibitedClaims ?? [],
                mandatoryDisclaimers = request.MandatoryDisclaimers ?? [],
            })
            : payloadJson;
        return Ok(await repository.CreateContextCatalogVersionAsync(ToKind(route), catalogId, new(
            Owner, request.SchemaVersion ?? 1, GccV2CanonicalJson.Sha256(canonical),
            payloadJson, request.Locale, request.Provenance?.GetRawText(), request.ProductSchemaVersionId,
            request.ApprovedClaims is null ? null : JsonSerializer.Serialize(request.ApprovedClaims),
            request.ProhibitedClaims is null ? null : JsonSerializer.Serialize(request.ProhibitedClaims),
            request.MandatoryDisclaimers is null ? null : JsonSerializer.Serialize(request.MandatoryDisclaimers),
            request.EffectiveFromUtc, request.EffectiveUntilUtc, Owner), ct));
    }

    [HttpPost("{route:regex(^audiences|style-guides|visual-guidelines|product-schemas|products$)}/versions/{versionId:guid}/{transition:regex(^review|approve|deprecate|revoke$)}")]
    public async Task<ActionResult<JsonElement>> TransitionCatalog(
        string route, Guid versionId, string transition, [FromBody] TransitionContextRequest request,
        CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (await repository.GetContextVersionAsync(ToKind(route), versionId, Owner, ct) is null) return NotFound();
        return Ok(await repository.TransitionContextCatalogVersionAsync(ToKind(route), versionId, transition,
            new(Owner, Owner, request.Reason), ct));
    }

    [HttpPost("creates/{createId:guid}/attachments/uploads")]
    public async Task<ActionResult<object>> IssueAttachmentUpload(
        Guid createId, [FromBody] AttachmentUploadRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var create = await repository.GetCreateAsync(createId, ct);
        if (create is null || !string.Equals(create.OwnerUserId, Owner, StringComparison.OrdinalIgnoreCase))
            return NotFound();
        var error = ValidateUpload(request.FileName, request.MediaType, request.ByteSize, request.Sha256);
        if (error is not null) return BadRequest(new { error });
        var usage = await repository.GetContextQuotaUsageAsync(Owner, createId, ct);
        if (usage is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (usage.KnowledgeBytes + usage.OwnerAttachmentBytes + request.ByteSize > MaxOwnerBytes)
        {
            GccV2ContextMetrics.UploadQuotaRejections.Add(
                1, new KeyValuePair<string, object?>("scope", "owner"));
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { error = $"Owner context quota of {MaxOwnerBytes} bytes would be exceeded." });
        }
        if (usage.RunAttachmentBytes + request.ByteSize > MaxRunBytes)
        {
            GccV2ContextMetrics.UploadQuotaRejections.Add(
                1, new KeyValuePair<string, object?>("scope", "run"));
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { error = $"Run attachment quota of {MaxRunBytes} bytes would be exceeded." });
        }
        GccV2ContextMetrics.UploadBytes.Add(
            request.ByteSize, new KeyValuePair<string, object?>("kind", "run_attachment"));
        var objectKey = $"content-creator-v2/{Owner}/attachments/{createId}/{Guid.NewGuid():N}/original";
        var grant = objectStore.IssuePut(objectKey, request.ByteSize,
            NormalizeMediaType(request.MediaType), TimeSpan.FromMinutes(10));
        var attachment = await repository.CreateRunAttachmentAsync(new(
            Owner, createId, objectKey, SafeFileName(request.FileName), NormalizeMediaType(request.MediaType),
            request.ByteSize, request.Sha256, grant.ExpiresAtUtc, DateTimeOffset.UtcNow.AddDays(30)), ct);
        return Ok(new { uploadId = attachment.Id, attachmentId = attachment.Id,
            uploadUrl = grant.UploadUrl, method = "PUT", headers = grant.RequiredHeaders,
            grant.ExpiresAtUtc, maxBytes = MaxFileBytes });
    }

    [HttpPost("creates/{createId:guid}/attachments/uploads/{uploadId:guid}/complete")]
    public async Task<ActionResult<GccV2RunAttachmentDto>> CompleteAttachmentUpload(
        Guid createId, Guid uploadId, [FromBody] CompleteUploadRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var attachment = await repository.GetRunAttachmentAsync(uploadId, Owner, ct);
        if (attachment is null || attachment.CreateId != createId) return NotFound();
        var verified = await objectStore.VerifyAsync(attachment.ObjectKey, attachment.ByteSize, ct);
        if (verified.ByteSize != request.ByteSize || verified.Sha256 != request.Sha256
            || verified.ByteSize != attachment.ByteSize || verified.Sha256 != attachment.Sha256)
            return Conflict(new { error = "Stored object size or checksum does not match the upload grant." });
        var result = await repository.FinalizeRunAttachmentAsync(uploadId,
            new(Owner, verified.ByteSize, verified.Sha256), ct);
        var jobs = await repository.ListContextIngestionJobsAsync("queued", limit: 200, ct: ct);
        var job = jobs.SingleOrDefault(x => x.TargetKind == "run_attachment" && x.TargetId == uploadId);
        if (job is null) return Conflict(new { error = "Attachment ingestion job was not queued." });
        ingestionWake.Wake(job.Id);
        return Accepted(new
        {
            uploadId, attachmentId = result.Id, ingestionJobId = job.Id, state = job.Status,
        });
    }

    /// <summary>
    /// Create a temporary run attachment from a public http(s) URL (SSRF-gated HTTP, no Playwright).
    /// </summary>
    [HttpPost("creates/{createId:guid}/attachments/from-url")]
    public async Task<ActionResult<object>> CreateAttachmentFromUrl(
        Guid createId, [FromBody] AttachmentFromUrlRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var (result, status, error, errorCode) = await urlAttachment.IngestAsync(
            Owner, createId, request.Url, request.Name, ct);
        if (result is null)
        {
            return StatusCode((int)status, new
            {
                contractVersion = "gcc-attachment-from-url.v1",
                error,
                errorCode,
            });
        }

        return Accepted(new
        {
            contractVersion = "gcc-attachment-from-url.v1",
            attachmentId = result.AttachmentId,
            createId = result.CreateId,
            finalUrl = result.FinalUrl,
            title = result.Title,
            safeFileName = result.SafeFileName,
            contentCompleteness = result.ContentCompleteness,
            statusCode = result.StatusCode,
            byteSize = result.ByteSize,
            contentSha256 = result.ContentSha256,
            ingestionJobId = result.IngestionJobId,
            state = result.IngestionState,
        });
    }

    [HttpDelete("creates/{createId:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DeleteAttachment(Guid createId, Guid attachmentId, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var attachment = await repository.GetRunAttachmentAsync(attachmentId, Owner, ct);
        if (attachment is null || attachment.CreateId != createId) return NotFound();
        await repository.DeleteRunAttachmentAsync(attachmentId, Owner, ct);
        await knowledgeIndexer.DeleteAsync(new(Owner, attachmentId, attachmentId), ct);
        await objectStore.DeleteAsync(attachment.ObjectKey, ct);
        return NoContent();
    }

    [HttpGet("context/ingestion/events")]
    public async Task<ActionResult<object>> ListIngestionEvents(CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var jobs = new List<GccV2ContextIngestionJobDto>();
        foreach (var status in new[] { "queued", "running", "ready", "failed", "cancelled" })
            jobs.AddRange(await repository.ListContextIngestionJobsAsync(status, limit: 200, ct: ct));
        return Ok(jobs
            .Where(x => string.Equals(x.OwnerUserId, Owner, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(GccV2ContextIngestionNotifier.ToEvent));
    }

    [HttpPost("context/resolve")]
    public async Task<ActionResult<GccV2ResolvedContextPreview>> Resolve(
        [FromBody] ResolveContextRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (request.CreateId == Guid.Empty)
            return BadRequest(new { error = "Save the create before requesting a context preview." });
        var create = await repository.GetCreateAsync(request.CreateId, ct);
        if (create is null || !string.Equals(create.OwnerUserId, Owner, StringComparison.OrdinalIgnoreCase))
            return NotFound(new { error = "The saved create is unavailable for the current owner." });
        var brief = request.BriefId is { } id ? await repository.GetBriefAsync(id, ct)
            : (await repository.ListBriefsByCreateAsync(request.CreateId, ct)).FirstOrDefault();
        if (brief is null) return BadRequest(new { error = "A persisted brief is required." });
        await repository.CreateContextSelectionAsync(new(
            Owner, request.CreateId, JsonSerializer.Serialize(request.Selection), Owner), ct);
        var prepared = await resolver.PrepareAsync(Guid.NewGuid(), request.CreateId, brief.Id, Owner,
            request.Selection, null, null, null, ct);
        GccV2ContextMetrics.ResolutionOutcomes.Add(1,
            new KeyValuePair<string, object?>(
                "outcome", prepared.Preview.BlockingFindings.Count == 0 ? "ready" : "blocked"));
        GccV2ContextMetrics.ManifestEntries.Record(prepared.Preview.EffectiveEntries.Count);
        return Ok(prepared.Preview with
        {
            AgentCompatibility = (request.SelectedAgentIds ?? []).Distinct(StringComparer.Ordinal)
                .Select(id => (object)new
                {
                    agentId = id,
                    compatible = true,
                    message = "Selected agent version allows all governed context kinds in this manifest.",
                }).ToList(),
        });
    }

    [HttpPost("context/resolve-task-agent")]
    public async Task<ActionResult<object>> ResolveTaskAgent(
        [FromBody] ResolveTaskAgentContextRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var prepared = await resolver.PrepareForTaskAgentAsync(Owner, request.Selection, ct);
        GccV2ContextMetrics.ResolutionOutcomes.Add(1,
            new KeyValuePair<string, object?>(
                "outcome", prepared.Preview.BlockingFindings.Count == 0 ? "ready" : "blocked"));
        GccV2ContextMetrics.ManifestEntries.Record(prepared.Preview.EffectiveEntries.Count);
        return Ok(new
        {
            preview = prepared.Preview with
            {
                AgentCompatibility = (request.SelectedAgentIds ?? []).Distinct(StringComparer.Ordinal)
                    .Select(id => (object)new
                    {
                        agentId = id,
                        compatible = true,
                        message = "Task-agent run can pin the selected governed context kinds.",
                    }).ToList(),
            },
            envelope = new
            {
                manifestId = prepared.ManifestId,
                digest = prepared.Sha256,
                signature = prepared.Signature,
                signingKeyId = prepared.SigningKeyId,
                resolvedAtUtc = prepared.ResolvedAtUtc,
                canonicalJson = prepared.CanonicalJson,
            },
        });
    }

    [HttpGet("jobs/{jobId:guid}/context-manifest")]
    public async Task<ActionResult<object>> GetManifest(Guid jobId, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var job = await repository.GetJobAsync(jobId, ct);
        if (job is null || !string.Equals(job.OwnerUserId, Owner, StringComparison.OrdinalIgnoreCase))
            return NotFound();
        var manifest = await repository.GetContextManifestByJobAsync(jobId, Owner, ct);
        if (manifest is null) return NotFound();
        resolver.Verify(manifest, jobId);
        return Ok(new
        {
            manifest.Id, manifest.JobId, manifest.Attempt, manifest.SchemaVersion,
            manifest.Sha256, manifest.SigningKeyId, manifest.ResolverIdentity,
            manifest.ResolvedAtUtc, manifest.ReplacesManifestId, manifest.Entries,
        });
    }

    [HttpPost("jobs/{jobId:guid}/refresh-context-and-rerun")]
    public async Task<ActionResult<object>> RefreshContextAndRerun(Guid jobId, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var source = await repository.GetJobAsync(jobId, ct);
        if (source is null || !string.Equals(source.OwnerUserId, Owner, StringComparison.OrdinalIgnoreCase))
            return NotFound();
        if (string.IsNullOrWhiteSpace(source.AgentTeamSnapshotJson)
            || string.IsNullOrWhiteSpace(source.AgentTeamSnapshotDigest)
            || string.IsNullOrWhiteSpace(source.AgentTeamSnapshotSignature))
            return Conflict(new { error = "Source job has no immutable agent-team snapshot." });
        IReadOnlyList<Guid> agentVersionIds;
        try
        {
            using var document = JsonDocument.Parse(source.AgentTeamSnapshotJson);
            agentVersionIds = document.RootElement.GetProperty("agents").EnumerateArray()
                .Select(x => x.GetProperty("agentVersionId").GetGuid()).ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return Conflict(new { error = "Source agent-team snapshot is malformed." });
        }
        var newJobId = Guid.NewGuid();
        GccV2PreparedManifest prepared;
        try
        {
            prepared = await resolver.PrepareRefreshAsync(
                source.Id, newJobId, source.CreateId, source.BriefId, Owner,
                source.AgentTeamSnapshotDigest, ct);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        if (prepared.Preview.BlockingFindings.Count > 0)
            return Conflict(new { error = "Refreshed context is not eligible.", prepared.Preview });
        var job = await repository.CreateJobAsync(new(
            source.CreateId, Owner, source.ContentType, source.BriefId,
            source.SiteAnalysisProfileId, source.ProjectSiteCrawlRunId, "plan",
            agentVersionIds, source.AgentTeamSnapshotJson, source.AgentTeamSnapshotDigest,
            source.AgentTeamSnapshotSignature, source.AgentTeamSnapshotKeyId,
            newJobId, prepared.Manifest), ct);
        await jobEvents.AppendAsync(job.Id, user.UserId, "ContextRefreshedAndRerun", new
        {
            sourceJobId = source.Id, jobId = job.Id, manifestId = prepared.Manifest.Id,
            replacesManifestId = prepared.Manifest.ReplacesManifestId,
        }, ct: ct);
        jobWake.Wake(job.Id);
        return Accepted(new { jobId = job.Id, manifestId = prepared.Manifest.Id, prepared.Preview });
    }

    private string Owner => user.UserId.ToString("D");
    private long MaxFileBytes => configuration.GetValue<long?>(
        "ContentCreatorV2:ContextObjectStore:MaxFileBytes") ?? DefaultMaxFileBytes;
    private long MaxOwnerBytes => configuration.GetValue<long?>(
        "ContentCreatorV2:ContextObjectStore:MaxOwnerBytes") ?? 1024L * 1024 * 1024;
    private long MaxRunBytes => configuration.GetValue<long?>(
        "ContentCreatorV2:ContextObjectStore:MaxRunBytes") ?? 100L * 1024 * 1024;
    private bool FeatureEnabled(string feature) => configuration.GetValue<bool?>(
        $"ContentCreatorV2:ContextFeatures:{feature}") ?? true;
    [NonAction]
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var action = context.ActionDescriptor.RouteValues["action"] ?? string.Empty;
        var feature = action switch
        {
            nameof(Capabilities) => null,
            nameof(IssueKnowledgeUpload) or nameof(CompleteKnowledgeUpload)
                or nameof(IssueAttachmentUpload) or nameof(CompleteAttachmentUpload)
                or nameof(ListIngestionEvents) or nameof(ReindexKnowledge)
                or nameof(DeleteAttachment) => "Ingestion",
            nameof(Resolve) or nameof(RefreshContextAndRerun) => "Resolution",
            nameof(GetManifest) => "ManifestRetrieval",
            _ => "Catalogs",
        };
        if (feature is not null && !FeatureEnabled(feature))
        {
            context.Result = NotFound();
            return;
        }
        await next();
    }
    private string? ValidateUpload(string fileName, string mediaType, long byteSize, string sha256)
    {
        if (SafeFileName(fileName) != fileName.Trim()) return "fileName contains unsafe path or control characters";
        if (!SupportedMediaTypes.Contains(NormalizeMediaType(mediaType))) return "mediaType is not supported";
        if (byteSize <= 0 || byteSize > MaxFileBytes) return $"byteSize must be 1..{MaxFileBytes}";
        return IsSha256(sha256) ? null : "sha256 is invalid";
    }
    private static string SafeFileName(string value)
    {
        var name = Path.GetFileName(value.Trim());
        return new string(name.Where(c => !char.IsControl(c) && c is not '/' and not '\\').ToArray());
    }
    private static string NormalizeMediaType(string value) => value.Split(';', 2)[0].Trim().ToLowerInvariant();
    private static string BrandName(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("companyName", out var name)
                && !string.IsNullOrWhiteSpace(name.GetString()))
                return name.GetString()!;
        }
        catch (JsonException) { }
        return "Brand Kit";
    }

    private static string MergeVoicePolicyIntoKitJson(string? existingKitJson, JsonElement voicePolicy)
    {
        using var document = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(existingKitJson) ? "{}" : existingKitJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "voicePolicy", StringComparison.Ordinal))
                    continue;
                property.WriteTo(writer);
            }
            writer.WritePropertyName("voicePolicy");
            voicePolicy.WriteTo(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string? ValidateProduct(CreateCatalogVersionRequest request, string schemaJson)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        var fields = schema.RootElement.ValueKind == JsonValueKind.Array
            ? schema.RootElement
            : schema.RootElement.TryGetProperty("fields", out var nested) ? nested : default;
        if (fields.ValueKind != JsonValueKind.Array)
            return "Product Schema must contain a fields array.";
        var definitions = fields.EnumerateArray().Select(field => new
        {
            Id = field.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var parsed)
                ? parsed : Guid.Empty,
            Required = field.TryGetProperty("required", out var required)
                && required.ValueKind == JsonValueKind.True,
        }).ToList();
        if (definitions.Count == 0 || definitions.Any(x => x.Id == Guid.Empty)
            || definitions.Select(x => x.Id).Distinct().Count() != definitions.Count)
            return "Product Schema field IDs must be unique UUIDs.";
        if (request.Payload.ValueKind != JsonValueKind.Object)
            return "Product payload must be an object keyed by schema field UUID.";
        var supplied = request.Payload.EnumerateObject().Select(x =>
            Guid.TryParse(x.Name, out var id) ? id : Guid.Empty).ToHashSet();
        if (supplied.Contains(Guid.Empty) || supplied.Except(definitions.Select(x => x.Id)).Any())
            return "Product payload contains a field outside its Product Schema.";
        if (definitions.Where(x => x.Required).Select(x => x.Id).Except(supplied).Any())
            return "Product payload is missing a required Product Schema field.";
        return GccV2ProductClaimPolicy.ValidateAuthoring(
            request.ApprovedClaims, request.ProhibitedClaims, request.MandatoryDisclaimers);
    }
    private static string ToKind(string route) => route switch
    {
        "audiences" => "audience", "style-guides" => "style_guide",
        "visual-guidelines" => "visual_guideline",
        "product-schemas" => "product_schema", "products" => "product",
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    public sealed record CreateKnowledgeRequest(string Name, string? Description, IReadOnlyList<string>? Tags, string? Kind);
    public sealed record PatchKnowledgeRequest(string? Name, string? Description, bool? IsRetired);
    public sealed record CreateKnowledgeVersionRequest(
        string ContentSha256, string MediaType, string? Language, JsonElement? SourceDescriptor,
        JsonElement? Provenance, DateTimeOffset? SourceModifiedAtUtc, int? SchemaVersion);
    public sealed record TransitionContextRequest(Guid? VersionId, string? Reason);
    public sealed record KnowledgeUploadRequest(
        Guid? AssetId, string FileName, string MediaType, long ByteSize, string Sha256, string? Language);
    public sealed record KnowledgeFromUrlRequest(
        string? Url, string? Name = null, IReadOnlyList<string>? Tags = null, Guid? AssetId = null);
    public sealed record KnowledgeFromGscRequest(
        Guid? GscConnectionId,
        DateOnly? StartDate = null,
        DateOnly? EndDate = null,
        int? RowLimit = null,
        string? Name = null,
        IReadOnlyList<string>? Tags = null,
        Guid? AssetId = null);
    public sealed record KnowledgeFromDriveRequest(
        Guid? DriveConnectionId,
        string? FileIdOrUrl,
        string? Name = null,
        IReadOnlyList<string>? Tags = null,
        Guid? AssetId = null);
    public sealed record AttachmentFromUrlRequest(string? Url, string? Name = null);
    public sealed record CreateBrandKitVersionRequest(JsonElement VoicePolicy);
    public sealed record AttachmentUploadRequest(
        string FileName, string MediaType, long ByteSize, string Sha256);
    public sealed record CompleteUploadRequest(long ByteSize, string Sha256);
    public sealed record CreateCatalogRequest(string Name, string? Description);
    public sealed record CreateCatalogVersionRequest(
        JsonElement Payload, int? SchemaVersion, string? Locale, JsonElement? Provenance,
        Guid? ProductSchemaVersionId, IReadOnlyList<string>? ApprovedClaims,
        IReadOnlyList<string>? ProhibitedClaims, IReadOnlyList<string>? MandatoryDisclaimers,
        DateTimeOffset? EffectiveFromUtc, DateTimeOffset? EffectiveUntilUtc);
    public sealed record ResolveContextRequest(
        Guid CreateId, Guid? BriefId, GccV2ContextSelectionRequest Selection,
        IReadOnlyList<string>? SelectedAgentIds = null);
    public sealed record ResolveTaskAgentContextRequest(
        GccV2ContextSelectionRequest Selection,
        IReadOnlyList<string>? SelectedAgentIds = null);
}
