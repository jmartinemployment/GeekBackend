using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Rag;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed record GccV2SkillResourceSnapshotV2(
    string Path, string MediaType, long ByteCount, string Sha256, string Content);

public sealed record GccV2SkillSnapshotEntryV2(
    string Id, string Name, string Description, string Version, string ActivationId,
    string PackageDigest, string SkillMdDigest, string SkillMd,
    string SourceRepository, string SourceRef, string Lifecycle,
    IReadOnlyList<string> SupportedStages, IReadOnlyList<string> SupportedContentTypes,
    IReadOnlyList<string> ApprovedToolIds, int Order,
    IReadOnlyList<GccV2SkillResourceSnapshotV2> Resources,
    IReadOnlyList<string> Scripts);

public sealed record GccV2SignedSkillExecutionEnvelopeV2(
    string EnvelopeVersion, string CatalogVersion, string SnapshotDigest, string Signature,
    string SignatureKeyId, string ContentType, string JobId, string AttemptId,
    DateTimeOffset ResolvedAtUtc, IReadOnlyList<GccV2SkillSnapshotEntryV2> Skills)
{
    public const string CurrentEnvelopeVersion = "gcc-skill-envelope.v2";
}

internal sealed record GccV2SkillSnapshotTemplateV2(
    string EnvelopeVersion, string CatalogVersion, string ContentType, string JobId,
    DateTimeOffset ResolvedAtUtc, IReadOnlyList<GccV2SkillSnapshotEntryV2> Skills);

public sealed record GccV2SkillSnapshotReferenceV2(
    string EnvelopeVersion, Guid JobId, string AttemptId, string Stage,
    string SnapshotDigest, string SignatureKeyId, string BindingSignature);

public sealed class GccV2SkillSnapshotSigner
{
    private readonly byte[]? _key;
    public bool IsConfigured => _key is not null;
    public string KeyId { get; }

    public GccV2SkillSnapshotSigner(IConfiguration configuration)
    {
        var value = configuration["GccV2Skills:SnapshotSigningKey"]
            ?? Environment.GetEnvironmentVariable("SKILL_SNAPSHOT_SIGNING_KEY");
        if (!string.IsNullOrWhiteSpace(value))
        {
            _key = Encoding.UTF8.GetBytes(value);
            if (_key.Length < 32)
                throw new InvalidOperationException("SKILL_SNAPSHOT_SIGNING_KEY must contain at least 32 UTF-8 bytes.");
        }
        KeyId = configuration["GccV2Skills:SnapshotSigningKeyId"]
            ?? Environment.GetEnvironmentVariable("SKILL_SNAPSHOT_SIGNING_KEY_ID")
            ?? "gcc-skills-1";
    }

    public string SignDigest(string snapshotDigest)
    {
        if (_key is null)
            throw new InvalidOperationException("SKILL_SNAPSHOT_SIGNING_KEY is not configured.");
        return Convert.ToHexString(HMACSHA256.HashData(
            _key, Encoding.ASCII.GetBytes(snapshotDigest))).ToLowerInvariant();
    }

    public bool VerifyDigest(string snapshotDigest, string signature)
    {
        if (_key is null || signature.Length != 64) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(SignDigest(snapshotDigest)),
            Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));
    }

    public string Bind(Guid jobId, string attemptId, string stage, string snapshotDigest)
    {
        if (_key is null)
            throw new InvalidOperationException("SKILL_SNAPSHOT_SIGNING_KEY is not configured.");
        var value = $"{jobId:D}|{attemptId}|{stage}|{snapshotDigest}";
        return Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    public bool VerifyBinding(Guid jobId, string attemptId, string stage, string snapshotDigest, string signature)
    {
        if (_key is null || signature.Length != 64) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Bind(jobId, attemptId, stage, snapshotDigest)),
            Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));
    }
}

public sealed class GccV2SkillSnapshotRegistry(
    HttpGccV2Repository repo,
    GccV2SkillSnapshotSigner signer,
    GccV2AgentTeamResolver agentTeams,
    IGeekCrawlerRagClient rag)
{
    public const string StageName = "skill-execution-snapshot-v2";
    public const string CatalogVersion = "gcc-reviewed-skills.v2";
    public const int MaxSkills = 10;
    public const int MaxResourcesPerSkill = 32;
    public const int MaxSkillMdBytes = 32_768;
    public const int MaxResourceBytes = 131_072;
    public const int MaxTotalEmbeddedBytes = 131_072;
    private static readonly HashSet<string> SafeTools =
    [
        "search_corpus", "load_evidence_page", "get_brief_context", "get_outline_context",
        "get_completed_section_summaries", "get_specialist_artifacts",
        "activate_skill", "read_skill_resource",
    ];
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<GccV2SignedSkillExecutionEnvelopeV2> BuildEnvelopeAsync(
        GccV2JobDto job, string attemptId, string stage, CancellationToken ct)
    {
        if (!Guid.TryParse(attemptId, out _))
            throw new InvalidOperationException("v3 attemptId must be a UUID.");
        var template = await LoadOrCreateTemplateAsync(job, ct);
        var applicable = template.Skills
            .Where(x => x.SupportedStages.Contains(stage, StringComparer.Ordinal)
                     && x.SupportedContentTypes.Contains(job.ContentType, StringComparer.Ordinal))
            .OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        ValidateSkills(applicable, job.ContentType, stage);
        var unsigned = new GccV2SignedSkillExecutionEnvelopeV2(
            template.EnvelopeVersion, template.CatalogVersion, new string('0', 64), new string('0', 64),
            signer.KeyId, template.ContentType, template.JobId, attemptId, template.ResolvedAtUtc, applicable);
        var digest = ComputeSnapshotDigest(unsigned);
        return unsigned with { SnapshotDigest = digest, Signature = signer.SignDigest(digest) };
    }

    public async Task<(string ExecutionVersion, GccV2SignedSkillExecutionEnvelopeV2? Envelope)> NegotiateAsync(
        GccV2JobDto job, string attemptId, string stage, CancellationToken ct)
    {
        if (!signer.IsConfigured || stage == "complete")
            return (RagProducerCapabilities.RequiredExecutionVersion, null);
        var capabilities = await rag.GetCapabilitiesAsync(ct);
        if (capabilities?.ExecutionVersions.Contains(RagProducerCapabilities.AgentExecutionVersion, StringComparer.Ordinal) == true
            && capabilities.SkillEnvelopeVersions.Contains(GccV2SignedSkillExecutionEnvelopeV2.CurrentEnvelopeVersion, StringComparer.Ordinal)
            && capabilities.AgentTraceVersions.Contains("agent-trace.v1", StringComparer.Ordinal)
            && capabilities.AgentToolVersions.Contains("agent-tools.v1", StringComparer.Ordinal)
            && capabilities.ToolsAllowed
            && capabilities.GenerationStages.Contains(stage, StringComparer.Ordinal))
            return (RagProducerCapabilities.AgentExecutionVersion,
                await BuildEnvelopeAsync(job, attemptId, stage, ct));
        return (RagProducerCapabilities.RequiredExecutionVersion, null);
    }

    public void Validate(GccV2SignedSkillExecutionEnvelopeV2 envelope, string stage)
    {
        if (envelope.EnvelopeVersion != GccV2SignedSkillExecutionEnvelopeV2.CurrentEnvelopeVersion)
            throw new InvalidOperationException("Unsupported v2 skill envelope.");
        ValidateSkills(envelope.Skills, envelope.ContentType, stage);
        var digest = ComputeSnapshotDigest(envelope);
        if (!string.Equals(digest, envelope.SnapshotDigest, StringComparison.Ordinal)
            || !signer.VerifyDigest(digest, envelope.Signature))
            throw new InvalidOperationException("Signed skill snapshot validation failed.");
    }

    public GccV2SkillSnapshotReferenceV2 Reference(
        GccV2SignedSkillExecutionEnvelopeV2 envelope, string stage)
    {
        Validate(envelope, stage);
        var jobId = Guid.Parse(envelope.JobId);
        return new(envelope.EnvelopeVersion, jobId, envelope.AttemptId, stage,
            envelope.SnapshotDigest, envelope.SignatureKeyId,
            signer.Bind(jobId, envelope.AttemptId, stage, envelope.SnapshotDigest));
    }

    public bool VerifyBinding(GccV2SkillSnapshotReferenceV2 reference) =>
        reference.EnvelopeVersion == GccV2SignedSkillExecutionEnvelopeV2.CurrentEnvelopeVersion
        && signer.VerifyBinding(reference.JobId, reference.AttemptId, reference.Stage,
            reference.SnapshotDigest, reference.BindingSignature);

    public static string ComputeSnapshotDigest(GccV2SignedSkillExecutionEnvelopeV2 envelope)
    {
        var unsigned = new
        {
            envelope.EnvelopeVersion,
            envelope.CatalogVersion,
            envelope.SignatureKeyId,
            envelope.ContentType,
            envelope.JobId,
            envelope.AttemptId,
            resolvedAtUtc = FormatPythonDateTime(envelope.ResolvedAtUtc),
            envelope.Skills,
        };
        var element = JsonSerializer.SerializeToElement(unsigned, WireJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false,
        }))
            WriteCanonical(element, writer);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    public static string SerializeWireEnvelope(GccV2SignedSkillExecutionEnvelopeV2 envelope) =>
        JsonSerializer.Serialize(envelope, WireJson);

    private async Task<GccV2SkillSnapshotTemplateV2> LoadOrCreateTemplateAsync(
        GccV2JobDto job, CancellationToken ct)
    {
        var results = await repo.GetStageResultsAsync(job.Id, ct);
        var existing = results.Where(x => x.Stage == StageName).OrderBy(x => x.CompletedAtUtc).ToList();
        if (existing.Count > 0)
        {
            var templates = existing.Select(x => JsonSerializer.Deserialize<GccV2SkillSnapshotTemplateV2>(
                x.OutputJson, WireJson) ?? throw new InvalidOperationException("Persisted v2 skill template is malformed.")).ToList();
            if (templates.Select(x => JsonSerializer.Serialize(x, WireJson)).Distinct(StringComparer.Ordinal).Count() != 1)
                throw new InvalidOperationException("Job contains conflicting immutable v2 skill templates.");
            return templates[0];
        }

        var packages = await repo.ListSkillsAsync("published", job.ContentType, null, ct);
        var hasAgentTeam = !string.IsNullOrWhiteSpace(job.AgentTeamSnapshotJson);
        var assignedVersionIds = hasAgentTeam
            ? agentTeams.ValidatePersisted(job).Agents.SelectMany(x => x.Skills)
                .Select(x => x.SkillVersionId).ToHashSet()
            : packages.Select(package => package.Versions
                    .Where(x => x.State == "published" && x.Applicability.Any(a =>
                        a.ContentType == job.ContentType && a.ActivationMode == "automatic"))
                    .OrderByDescending(x => Semver(x.SemanticVersion))
                    .ThenByDescending(x => x.ImportedAtUtc).ThenBy(x => x.Id).FirstOrDefault())
                .Where(x => x is not null).Select(x => x!.Id).ToHashSet();
        var selectedSlugs = packages.Where(package =>
                package.Versions.Any(version => assignedVersionIds.Contains(version.Id)))
            .Select(package => package.Slug).ToHashSet(StringComparer.Ordinal);
        foreach (var (package, version) in packages.SelectMany(package => package.Versions
                     .Where(version => assignedVersionIds.Contains(version.Id))
                     .Select(version => (package, version))))
            foreach (var applicability in version.Applicability.Where(x =>
                         x.ContentType == job.ContentType && (hasAgentTeam || x.ActivationMode == "automatic")))
            {
                var conflicts = JsonSerializer.Deserialize<List<string>>(applicability.ConflictsJson) ?? [];
                if (conflicts.Any(selectedSlugs.Contains))
                    throw new InvalidOperationException($"Skill '{package.Slug}' conflicts with the selected skill set.");
            }
        var entries = packages.SelectMany(package => package.Versions
                .Where(version => version.State == "published" && assignedVersionIds.Contains(version.Id))
                .Select(version => ToEntry(package, version, job.ContentType, hasAgentTeam)))
            .Where(x => x is not null).Cast<GccV2SkillSnapshotEntryV2>()
            .OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        if (entries.Count != assignedVersionIds.Count)
            throw new InvalidOperationException(
                "Every explicitly assigned agent skill version must resolve to a published governed skill.");
        ValidateSkills(entries, job.ContentType, stage: null);
        var template = new GccV2SkillSnapshotTemplateV2(
            GccV2SignedSkillExecutionEnvelopeV2.CurrentEnvelopeVersion, CatalogVersion,
            job.ContentType, job.Id.ToString("D"), DateTimeOffset.UtcNow, entries);
        await repo.AddStageResultAsync(job.Id, new CreateGccV2StageResultCommand(
            StageName, null, JsonSerializer.Serialize(template, WireJson), 0), ct);
        return template;
    }

    private static GccV2SkillSnapshotEntryV2? ToEntry(
        GccV2SkillPackageDto package, GccV2SkillVersionDto version, string contentType,
        bool explicitlyAssigned)
    {
        var applicable = version.Applicability.Where(x => x.ContentType == contentType
            && (explicitlyAssigned || x.ActivationMode == "automatic")).ToList();
        if (applicable.Count == 0) return null;
        if (applicable.Any(x => x.ActivationMode is not ("automatic" or "explicit")))
            throw new InvalidOperationException($"Skill '{package.Slug}' has an invalid activation mode.");
        var skillMd = version.Files.SingleOrDefault(x => x.RelativePath == "SKILL.md")
            ?? throw new InvalidOperationException($"Published skill '{package.Slug}' has no SKILL.md.");
        var resources = version.Files.Where(x =>
                (x.RelativePath.StartsWith("references/", StringComparison.Ordinal)
                 || x.RelativePath.StartsWith("assets/", StringComparison.Ordinal))
                && IsAllowedTextMediaType(x.MediaType))
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .Select(x => new GccV2SkillResourceSnapshotV2(
                x.RelativePath, x.MediaType, x.ByteCount, x.Sha256, x.Content)).ToList();
        var tools = applicable.SelectMany(x =>
        {
            try { return JsonSerializer.Deserialize<List<string>>(x.RequiredToolsJson) ?? []; }
            catch (JsonException) { throw new InvalidOperationException($"Invalid required tools for {package.Slug}."); }
        }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var stages = applicable.Select(x => x.Stage).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var contentTypes = applicable.Select(x => x.ContentType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return new(package.Slug, package.DisplayName, Truncate(package.Description, 500), version.SemanticVersion,
            $"activate-{package.Slug}-{version.Id:D}", version.PackageSha256, skillMd.Sha256,
            skillMd.Content, package.SourceRepository, version.ImmutableGitRef, "published",
            stages, contentTypes, tools, applicable.Min(x => x.Order), resources, []);
    }

    private static void ValidateSkills(
        IReadOnlyList<GccV2SkillSnapshotEntryV2> skills, string contentType, string? stage)
    {
        if (skills.Count > MaxSkills) throw new InvalidOperationException($"Skill count exceeds {MaxSkills}.");
        if (skills.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != skills.Count)
            throw new InvalidOperationException("Duplicate skill IDs are prohibited.");
        if (!skills.SequenceEqual(skills.OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal)))
            throw new InvalidOperationException("Skills are not in deterministic order.");
        long total = 0;
        foreach (var skill in skills)
        {
            if (skill.Lifecycle != "published" || skill.Scripts.Count != 0)
                throw new InvalidOperationException("Runtime skill snapshots may contain only published, script-free skills.");
            if (!skill.SupportedContentTypes.Contains(contentType, StringComparer.Ordinal)
                || (stage is not null && !skill.SupportedStages.Contains(stage, StringComparer.Ordinal)))
                throw new InvalidOperationException($"Skill '{skill.Id}' is outside the requested stage/content scope.");
            if (skill.ApprovedToolIds.Except(SafeTools, StringComparer.Ordinal).Any())
                throw new InvalidOperationException($"Skill '{skill.Id}' requests an unapproved tool.");
            var skillBytes = Encoding.UTF8.GetByteCount(skill.SkillMd);
            if (skillBytes > MaxSkillMdBytes || !HashMatches(skill.SkillMd, skill.SkillMdDigest))
                throw new InvalidOperationException($"Skill '{skill.Id}' has invalid SKILL.md content.");
            if (skill.Resources.Count > MaxResourcesPerSkill)
                throw new InvalidOperationException($"Skill '{skill.Id}' has too many resources.");
            total += skillBytes;
            foreach (var resource in skill.Resources)
            {
                var bytes = Encoding.UTF8.GetByteCount(resource.Content);
                if (bytes != resource.ByteCount || bytes > MaxResourceBytes || !HashMatches(resource.Content, resource.Sha256)
                    || !IsAllowedResourcePath(resource.Path) || !IsAllowedTextMediaType(resource.MediaType))
                    throw new InvalidOperationException($"Skill '{skill.Id}' has an invalid resource '{resource.Path}'.");
                total += bytes;
            }
        }
        if (total > MaxTotalEmbeddedBytes)
            throw new InvalidOperationException($"Embedded skill content exceeds {MaxTotalEmbeddedBytes} bytes.");
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string FormatPythonDateTime(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var fractional = utc.ToString("fffffff").TrimEnd('0');
        return fractional.Length == 0
            ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            : utc.ToString("yyyy-MM-dd'T'HH:mm:ss") + "." + fractional + "Z";
    }
    private static bool IsAllowedResourcePath(string path) =>
        (path.StartsWith("references/", StringComparison.Ordinal)
         || path.StartsWith("assets/", StringComparison.Ordinal))
        && !path.Split('/').Any(x => x is "" or "." or "..");
    private static bool IsAllowedTextMediaType(string mediaType) =>
        mediaType.StartsWith("text/", StringComparison.Ordinal)
        || mediaType is "application/json" or "application/yaml" or "image/svg+xml";
    private static bool HashMatches(string content, string digest) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant(),
            digest, StringComparison.Ordinal);
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    private static Version Semver(string value) =>
        Version.TryParse(value, out var parsed) ? parsed : new Version(0, 0, 0);
}
