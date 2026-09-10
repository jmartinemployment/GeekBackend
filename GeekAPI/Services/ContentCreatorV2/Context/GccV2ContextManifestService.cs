using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed record GccV2ProductSelection(Guid ProductVersionId, IReadOnlyList<Guid> SelectedFieldIds);
public sealed record GccV2ContextSelectionRequest(
    IReadOnlyList<Guid>? KnowledgeAssetVersionIds = null,
    IReadOnlyList<Guid>? RunAttachmentIds = null,
    Guid? AudienceVersionId = null,
    Guid? StyleGuideVersionId = null,
    IReadOnlyList<GccV2ProductSelection>? ProductSelections = null,
    Guid? BrandKitVersionId = null,
    Guid? VisualGuidelineVersionId = null,
    string? Locale = null,
    string? RunNotes = null,
    bool WebSearchEnabled = false,
    bool KnowledgeSearchEnabled = true);
public sealed record GccV2ResolvedContextEntry(
    string ContextKind, Guid StableId, Guid? VersionId, int? VersionNumber,
    string ContentSha256, string LifecycleDecision, string PermissionDecision,
    string FreshnessDecision, string SelectionSource, IReadOnlyList<Guid>? SelectedFieldIds,
    DateTimeOffset? SourceModifiedAtUtc);
public sealed record GccV2ResolvedContextPreview(
    IReadOnlyList<GccV2ResolvedContextEntry> EffectiveEntries,
    IReadOnlyList<GccV2ResolvedContextEntry> InheritedEntries,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> BlockingFindings,
    IReadOnlyList<object> Freshness, int EstimatedContextSize,
    IReadOnlyList<object> AgentCompatibility);
public sealed record GccV2PreparedManifest(
    GccV2ResolvedContextPreview Preview, CreateGccV2RunContextManifestCommand Manifest);
public sealed record GccV2TaskAgentContextEnvelope(
    GccV2ResolvedContextPreview Preview,
    Guid ManifestId,
    string CanonicalJson,
    string Sha256,
    string Signature,
    string SigningKeyId,
    DateTimeOffset ResolvedAtUtc);

public sealed class GccV2ContextManifestSigner
{
    private readonly string _activeKeyId;
    private readonly IReadOnlyDictionary<string, byte[]> _keys;

    public GccV2ContextManifestSigner(IConfiguration configuration)
    {
        var section = configuration.GetSection("ContentCreatorV2:ContextManifestSigning");
        _activeKeyId = section["ActiveKeyId"]?.Trim() ?? "";
        _keys = section.GetSection("Keys").GetChildren()
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .ToDictionary(x => x.Key, x => Convert.FromBase64String(x.Value!), StringComparer.Ordinal);
    }

    public string ActiveKeyId => _activeKeyId;

    public string Sign(string digest)
    {
        if (!_keys.TryGetValue(_activeKeyId, out var key) || key.Length < 32)
            throw new InvalidOperationException("An active context manifest HMAC key of at least 256 bits is required.");
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(digest))).ToLowerInvariant();
    }

    public bool Verify(string digest, string signature, string keyId)
    {
        if (!_keys.TryGetValue(keyId, out var key) || signature.Length != 64) return false;
        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(digest));
        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature));
        }
        catch (FormatException) { return false; }
    }
}

public static class GccV2CanonicalJson
{
    public static string Serialize<T>(T value)
    {
        using var source = JsonDocument.Parse(JsonSerializer.Serialize(value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            WriteElement(writer, source.RootElement);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Sha256(string canonicalJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteElement(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
            case JsonValueKind.Number: writer.WriteRawValue(element.GetRawText(), skipInputValidation: false); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new JsonException($"Unsupported JSON kind {element.ValueKind}.");
        }
    }
}

public sealed class GccV2ContextResolver(
    HttpGccV2Repository repository,
    GccV2ContextManifestSigner signer,
    IConfiguration configuration)
{
    private readonly int _staleAfterDays = Math.Max(1,
        configuration.GetValue<int?>("ContentCreatorV2:ContextPolicy:StaleAfterDays") ?? 180);
    private readonly bool _requireFreshKnowledge =
        configuration.GetValue<bool>("ContentCreatorV2:ContextPolicy:RequireFreshKnowledge");

    public async Task<GccV2PreparedManifest> PrepareReplayAsync(
        Guid sourceJobId, Guid newJobId, Guid createId, Guid briefId, string ownerUserId,
        string? agentSnapshotDigest, CancellationToken ct)
    {
        var source = await repository.GetContextManifestByJobAsync(sourceJobId, ownerUserId, ct)
            ?? throw new InvalidOperationException("Source job has no context manifest to replay.");
        Verify(source, sourceJobId);
        var selection = new GccV2ContextSelectionRequest(
            source.Entries.Where(x => x.ContextKind == "knowledge" && x.VersionId is not null)
                .Select(x => x.VersionId!.Value).ToList(),
            source.Entries.Where(x => x.ContextKind == "run_attachment").Select(x => x.StableId).ToList(),
            source.Entries.FirstOrDefault(x => x.ContextKind == "audience")?.VersionId,
            source.Entries.FirstOrDefault(x => x.ContextKind == "style_guide")?.VersionId,
            source.Entries.Where(x => x.ContextKind == "product" && x.VersionId is not null)
                .Select(x => new GccV2ProductSelection(x.VersionId!.Value,
                    string.IsNullOrWhiteSpace(x.SelectedFieldIdsJson) ? [] :
                    JsonSerializer.Deserialize<List<Guid>>(x.SelectedFieldIdsJson) ?? [])).ToList(),
            source.Entries.FirstOrDefault(x => x.ContextKind == "brand_kit")?.VersionId,
            source.Entries.FirstOrDefault(x => x.ContextKind == "visual_guideline")?.VersionId);
        return await PrepareAsync(newJobId, createId, briefId, ownerUserId, selection,
            agentSnapshotDigest, null, source.Id, ct);
    }

    public async Task<GccV2PreparedManifest> PrepareRefreshAsync(
        Guid sourceJobId, Guid newJobId, Guid createId, Guid briefId, string ownerUserId,
        string? agentSnapshotDigest, CancellationToken ct)
    {
        var source = await repository.GetContextManifestByJobAsync(sourceJobId, ownerUserId, ct)
            ?? throw new InvalidOperationException("Source job has no context manifest to refresh.");
        Verify(source, sourceJobId);
        async Task<Guid?> Current(string kind)
        {
            var entry = source.Entries.FirstOrDefault(x => x.ContextKind == kind);
            if (entry is null) return null;
            return (await repository.GetCurrentContextVersionAsync(kind, entry.StableId, ownerUserId, ct))?.VersionId;
        }
        var knowledge = new List<Guid>();
        foreach (var entry in source.Entries.Where(x => x.ContextKind == "knowledge"))
        {
            var current = await repository.GetCurrentContextVersionAsync(
                "knowledge", entry.StableId, ownerUserId, ct);
            if (current is null) throw new InvalidOperationException(
                $"Knowledge asset {entry.StableId} has no approved current version.");
            knowledge.Add(current.VersionId);
        }
        var products = new List<GccV2ProductSelection>();
        foreach (var entry in source.Entries.Where(x => x.ContextKind == "product"))
        {
            var current = await repository.GetCurrentContextVersionAsync("product", entry.StableId, ownerUserId, ct)
                ?? throw new InvalidOperationException($"Product {entry.StableId} has no approved current version.");
            products.Add(new(current.VersionId,
                string.IsNullOrWhiteSpace(entry.SelectedFieldIdsJson) ? [] :
                JsonSerializer.Deserialize<List<Guid>>(entry.SelectedFieldIdsJson) ?? []));
        }
        var selection = new GccV2ContextSelectionRequest(
            knowledge,
            source.Entries.Where(x => x.ContextKind == "run_attachment").Select(x => x.StableId).ToList(),
            await Current("audience"),
            await Current("style_guide"),
            products,
            source.Entries.FirstOrDefault(x => x.ContextKind == "brand_kit")?.VersionId,
            await Current("visual_guideline"));
        return await PrepareAsync(newJobId, createId, briefId, ownerUserId, selection,
            agentSnapshotDigest, null, source.Id, ct);
    }

    public async Task<GccV2PreparedManifest> PrepareAsync(
        Guid jobId, Guid createId, Guid briefId, string ownerUserId,
        GccV2ContextSelectionRequest selection, string? agentSnapshotDigest,
        string? skillSnapshotDigest, Guid? replacesManifestId, CancellationToken ct)
    {
        if (!(configuration.GetValue<bool?>(
                "ContentCreatorV2:ContextFeatures:Resolution") ?? true))
            throw new InvalidOperationException("Governed context resolution is disabled.");
        var blocks = new List<string>();
        var warnings = new List<string>();
        var entries = new List<GccV2ResolvedContextEntry>();
        var create = await repository.GetCreateAsync(createId, ct);
        if (create is null || !string.Equals(create.OwnerUserId, ownerUserId, StringComparison.OrdinalIgnoreCase))
            blocks.Add("create_not_owned");
        var brief = await repository.GetBriefAsync(briefId, ct);
        if (brief is null || brief.CreateId != createId) blocks.Add("brief_not_found");
        else
        {
            try
            {
                using var briefDocument = JsonDocument.Parse(brief.RawBriefJson);
                var canonicalBrief = GccV2CanonicalJson.Serialize(briefDocument.RootElement);
                entries.Add(new("brief", createId, brief.Id, brief.Version,
                    GccV2CanonicalJson.Sha256(canonicalBrief), "approved", "owner_allowed",
                    "current", "create", null, brief.CreatedAtUtc));
            }
            catch (JsonException)
            {
                blocks.Add("brief_malformed");
            }
        }

        foreach (var id in (selection.KnowledgeAssetVersionIds ?? []).Distinct())
            await AddVersionAsync("knowledge", id, ownerUserId, null, entries, blocks, warnings, ct);
        if (selection.AudienceVersionId is { } audience)
            await AddVersionAsync("audience", audience, ownerUserId, null, entries, blocks, warnings, ct);
        if (selection.StyleGuideVersionId is { } style)
            await AddVersionAsync("style_guide", style, ownerUserId, null, entries, blocks, warnings, ct);
        if (selection.VisualGuidelineVersionId is { } visual)
            await AddVersionAsync("visual_guideline", visual, ownerUserId, null, entries, blocks, warnings, ct);
        foreach (var product in selection.ProductSelections ?? [])
            await AddVersionAsync("product", product.ProductVersionId, ownerUserId,
                product.SelectedFieldIds, entries, blocks, warnings, ct);
        if (selection.BrandKitVersionId is { } brandKitId)
        {
            var brand = await repository.GetBrandKitAsync(brandKitId, ct);
            if (brand is null || !string.Equals(brand.OwnerUserId, ownerUserId, StringComparison.OrdinalIgnoreCase))
                blocks.Add($"brand_kit:{brandKitId}:not_owned_or_missing");
            else if (brand.VoiceStatus != "accepted" || string.IsNullOrWhiteSpace(brand.CanonicalSha256))
                blocks.Add($"brand_kit:{brandKitId}:not_accepted");
            else
                entries.Add(new("brand_kit", brand.DerivedFromProfileId, brand.Id, brand.Version,
                    brand.CanonicalSha256, "approved", "owner_allowed", "current",
                    "run_override", null, brand.DerivedAtUtc));
        }

        foreach (var id in (selection.RunAttachmentIds ?? []).Distinct())
        {
            var attachment = await repository.GetRunAttachmentAsync(id, ownerUserId, ct);
            if (attachment is null || attachment.CreateId != createId)
            {
                blocks.Add($"run_attachment:{id}:not_owned");
                continue;
            }
            if (attachment.FinalizedAtUtc is null || attachment.IngestionState != "ready")
            {
                blocks.Add($"run_attachment:{id}:not_ready");
                continue;
            }
            if (attachment.RetainUntilUtc <= DateTimeOffset.UtcNow)
            {
                blocks.Add($"run_attachment:{id}:expired");
                continue;
            }
            entries.Add(new("run_attachment", attachment.Id, null, null, attachment.Sha256,
                "temporary", "owner_allowed", "current", "run_override", null, attachment.FinalizedAtUtc));
        }

        var ordered = entries.OrderBy(x => x.ContextKind, StringComparer.Ordinal)
            .ThenBy(x => x.StableId).ThenBy(x => x.VersionId).ToList();
        var preview = new GccV2ResolvedContextPreview(
            ordered, [], warnings, blocks, [], ordered.Count * 512, []);
        var resolvedAt = DateTimeOffset.UtcNow;
        var manifestId = Guid.NewGuid();
        var payload = new
        {
            schemaVersion = 1,
            manifestId,
            jobId,
            attempt = 0,
            ownerUserId,
            createId,
            briefId,
            locale = selection.Locale?.Trim() ?? "en",
            runNotes = selection.RunNotes?.Trim(),
            policies = new
            {
                selection.WebSearchEnabled,
                selection.KnowledgeSearchEnabled,
                retrievalPolicy = "manifest-allowlist.v1",
            },
            agentSnapshotDigest,
            skillSnapshotDigest,
            resolvedAtUtc = resolvedAt,
            entries = ordered,
        };
        var canonical = GccV2CanonicalJson.Serialize(payload);
        var digest = GccV2CanonicalJson.Sha256(canonical);
        var signature = signer.Sign(digest);
        var command = new CreateGccV2RunContextManifestCommand(
            manifestId, ownerUserId, jobId, 0, 1, canonical, digest, signature,
            signer.ActiveKeyId, "GeekAPI.GccV2ContextResolver/v1", resolvedAt, replacesManifestId,
            ordered.Select(x => new CreateGccV2RunContextManifestEntryCommand(
                x.ContextKind, x.StableId, x.VersionId, x.VersionNumber, x.ContentSha256,
                x.LifecycleDecision, x.PermissionDecision, x.FreshnessDecision, x.SelectionSource,
                x.SelectedFieldIds is null ? null : JsonSerializer.Serialize(x.SelectedFieldIds),
                x.SourceModifiedAtUtc)).ToList());
        return new GccV2PreparedManifest(preview, command);
    }

    public async Task<GccV2TaskAgentContextEnvelope> PrepareForTaskAgentAsync(
        string ownerUserId,
        GccV2ContextSelectionRequest selection,
        CancellationToken ct)
    {
        if (!(configuration.GetValue<bool?>(
                "ContentCreatorV2:ContextFeatures:Resolution") ?? true))
            throw new InvalidOperationException("Governed context resolution is disabled.");
        var blocks = new List<string>();
        var warnings = new List<string>();
        var entries = new List<GccV2ResolvedContextEntry>();
        if ((selection.RunAttachmentIds ?? []).Count > 0)
            blocks.Add("run_attachment:task_agent:not_supported");

        foreach (var id in (selection.KnowledgeAssetVersionIds ?? []).Distinct())
            await AddVersionAsync("knowledge", id, ownerUserId, null, entries, blocks, warnings, ct);
        if (selection.AudienceVersionId is { } audience)
            await AddVersionAsync("audience", audience, ownerUserId, null, entries, blocks, warnings, ct);
        if (selection.StyleGuideVersionId is { } style)
            await AddVersionAsync("style_guide", style, ownerUserId, null, entries, blocks, warnings, ct);
        if (selection.VisualGuidelineVersionId is { } visual)
            await AddVersionAsync("visual_guideline", visual, ownerUserId, null, entries, blocks, warnings, ct);
        foreach (var product in selection.ProductSelections ?? [])
            await AddVersionAsync("product", product.ProductVersionId, ownerUserId,
                product.SelectedFieldIds, entries, blocks, warnings, ct);
        if (selection.BrandKitVersionId is { } brandKitId)
        {
            var brand = await repository.GetBrandKitAsync(brandKitId, ct);
            if (brand is null || !string.Equals(brand.OwnerUserId, ownerUserId, StringComparison.OrdinalIgnoreCase))
                blocks.Add($"brand_kit:{brandKitId}:not_owned_or_missing");
            else if (brand.VoiceStatus != "accepted" || string.IsNullOrWhiteSpace(brand.CanonicalSha256))
                blocks.Add($"brand_kit:{brandKitId}:not_accepted");
            else
                entries.Add(new("brand_kit", brand.DerivedFromProfileId, brand.Id, brand.Version,
                    brand.CanonicalSha256, "approved", "owner_allowed", "current",
                    "run_override", null, brand.DerivedAtUtc));
        }

        var ordered = entries.OrderBy(x => x.ContextKind, StringComparer.Ordinal)
            .ThenBy(x => x.StableId).ThenBy(x => x.VersionId).ToList();
        var preview = new GccV2ResolvedContextPreview(
            ordered, [], warnings, blocks, [], ordered.Count * 512, []);
        var resolvedAt = DateTimeOffset.UtcNow;
        var manifestId = Guid.NewGuid();
        var payload = new
        {
            schemaVersion = 1,
            manifestId,
            scope = "task-agent",
            ownerUserId,
            locale = selection.Locale?.Trim() ?? "en",
            runNotes = selection.RunNotes?.Trim(),
            policies = new
            {
                selection.WebSearchEnabled,
                selection.KnowledgeSearchEnabled,
                retrievalPolicy = "manifest-allowlist.v1",
            },
            resolvedAtUtc = resolvedAt,
            entries = ordered,
        };
        var canonical = GccV2CanonicalJson.Serialize(payload);
        var digest = GccV2CanonicalJson.Sha256(canonical);
        var signature = signer.Sign(digest);
        return new GccV2TaskAgentContextEnvelope(
            preview, manifestId, canonical, digest, signature, signer.ActiveKeyId, resolvedAt);
    }

    public void VerifyTaskAgentEnvelope(
        string canonicalJson, string sha256, string signature, string signingKeyId)
    {
        var digest = GccV2CanonicalJson.Sha256(canonicalJson);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(digest), Encoding.ASCII.GetBytes(sha256)))
            throw new InvalidOperationException("Task-agent context manifest digest is invalid.");
        if (!signer.Verify(digest, signature, signingKeyId))
            throw new InvalidOperationException("Task-agent context manifest signature is invalid.");
    }

    public void Verify(GccV2RunContextManifestDto manifest, Guid jobId)
    {
        if (manifest.JobId != jobId) throw new InvalidOperationException("Context manifest job binding is invalid.");
        var digest = GccV2CanonicalJson.Sha256(manifest.CanonicalJson);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(digest), Encoding.ASCII.GetBytes(manifest.Sha256)))
            throw new InvalidOperationException("Context manifest digest is invalid.");
        if (!signer.Verify(digest, manifest.Signature, manifest.SigningKeyId))
            throw new InvalidOperationException("Context manifest signature is invalid.");
        if (manifest.Entries.Any(x => x.PermissionDecision != "owner_allowed"
            || x.LifecycleDecision == "revoked"
            || x.FreshnessDecision == "expired"))
            throw new InvalidOperationException("Context manifest contains an ineligible entry.");
    }

    private async Task AddVersionAsync(
        string kind, Guid versionId, string owner, IReadOnlyList<Guid>? fields,
        List<GccV2ResolvedContextEntry> entries, List<string> blocks, List<string> warnings,
        CancellationToken ct)
    {
        var value = await repository.GetContextVersionAsync(kind, versionId, owner, ct);
        if (value is null)
        {
            blocks.Add($"{kind}:{versionId}:not_owned_or_missing");
            return;
        }
        if (value.LifecycleState == "revoked")
        {
            blocks.Add($"{kind}:{versionId}:revoked");
            return;
        }
        if (value.LifecycleState == "deprecated")
            warnings.Add($"{kind}:{versionId}:deprecated_pinned");
        else if (value.LifecycleState != "approved")
        {
            blocks.Add($"{kind}:{versionId}:not_approved");
            return;
        }
        if (kind == "knowledge" && (value.ExtractionState != "ready" || value.IndexState != "ready"))
        {
            blocks.Add($"{kind}:{versionId}:not_indexed");
            return;
        }
        if (kind == "product")
        {
            if (value.ProductSchemaVersionId is null)
            {
                blocks.Add($"{kind}:{versionId}:schema_missing");
                return;
            }
            var schema = await repository.GetContextVersionAsync(
                "product_schema", value.ProductSchemaVersionId.Value, owner, ct);
            var allowedFields = ParseSchemaFieldIds(schema?.PayloadJson);
            if (schema is null || schema.LifecycleState is not ("approved" or "deprecated"))
            {
                blocks.Add($"{kind}:{versionId}:schema_not_eligible");
                return;
            }
            if (schema.LifecycleState == "deprecated")
                warnings.Add($"product_schema:{schema.VersionId}:deprecated_pinned");
            if (fields is { Count: > 0 } && fields.Any(x => !allowedFields.Contains(x)))
            {
                blocks.Add($"{kind}:{versionId}:selected_fields_invalid");
                return;
            }
            var claimBlocks = GccV2ProductClaimPolicy.ResolveBlockers(
                versionId, value.ApprovedClaimsJson, value.ProhibitedClaimsJson,
                value.MandatoryDisclaimersJson);
            if (claimBlocks.Count > 0)
            {
                blocks.AddRange(claimBlocks);
                return;
            }
            if (entries.All(x => x.VersionId != schema.VersionId))
                entries.Add(new("product_schema", schema.StableId, schema.VersionId,
                    schema.VersionNumber, schema.CanonicalSha256, schema.LifecycleState,
                    "owner_allowed", "current", "product_schema", null, null));
        }
        var now = DateTimeOffset.UtcNow;
        if (value.EffectiveFromUtc > now || value.EffectiveUntilUtc <= now)
        {
            blocks.Add($"{kind}:{versionId}:expired");
            return;
        }
        var freshness = value.SourceModifiedAtUtc is { } sourceModified
            && sourceModified < now.AddDays(-_staleAfterDays) ? "stale" : "current";
        if (kind == "knowledge" && freshness == "stale")
        {
            if (_requireFreshKnowledge)
            {
                blocks.Add($"{kind}:{versionId}:stale_required");
                return;
            }
            warnings.Add($"{kind}:{versionId}:stale_pinned");
        }
        entries.Add(new GccV2ResolvedContextEntry(
            kind, value.StableId, value.VersionId, value.VersionNumber,
            value.CanonicalSha256, value.LifecycleState, "owner_allowed", "current",
            "run_override", fields, value.SourceModifiedAtUtc) with
            {
                FreshnessDecision = freshness,
            });
    }

    private static IReadOnlySet<Guid> ParseSchemaFieldIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new HashSet<Guid>();
        try
        {
            using var document = JsonDocument.Parse(json);
            var fields = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement
                : document.RootElement.TryGetProperty("fields", out var nested) ? nested : default;
            return fields.ValueKind != JsonValueKind.Array
                ? new HashSet<Guid>()
                : fields.EnumerateArray()
                    .Select(x => x.TryGetProperty("id", out var id)
                        && Guid.TryParse(id.GetString(), out var value) ? value : Guid.Empty)
                    .Where(x => x != Guid.Empty).ToHashSet();
        }
        catch (JsonException)
        {
            return new HashSet<Guid>();
        }
    }
}
