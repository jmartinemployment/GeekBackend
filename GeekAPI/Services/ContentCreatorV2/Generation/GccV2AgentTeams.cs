using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.ContentTypes;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed record GccV2AgentTeamSkillPin(
    Guid SkillVersionId, string Slug, string Version, string Digest,
    IReadOnlyList<string> Stages, IReadOnlyList<string> ContentTypes,
    IReadOnlyList<string> RequiredTools, IReadOnlyList<string>? Conflicts,
    IReadOnlyList<string>? ActivationModes);
public sealed record GccV2AgentTeamParticipation(string Stage, string Role, int Order);
public sealed record GccV2AgentTeamMember(
    Guid AgentId, Guid AgentVersionId, string Slug, string Name, string Version,
    string Digest, string Objective, string Instructions, string InstructionsDigest,
    string ModelPolicyVersion, string ModelPolicyProfile, string Policy, string PolicyDigest,
    IReadOnlyList<string> ContentTypes, IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> AllowedModels, IReadOnlyList<GccV2AgentTeamSkillPin> Skills,
    IReadOnlyList<GccV2AgentTeamParticipation> Participation,
    IReadOnlyList<string>? RequiredContextKinds = null,
    IReadOnlyList<string>? AllowedContextKinds = null);
public sealed record GccV2AgentTeamSnapshot(
    string SnapshotVersion, string CatalogVersion, DateTimeOffset ResolvedAtUtc,
    IReadOnlyList<GccV2AgentTeamMember> Agents);
public sealed record GccV2SignedAgentTeam(
    GccV2AgentTeamSnapshot Snapshot, string SnapshotJson, string Digest,
    string Signature, string SignatureKeyId);

public sealed class GccV2AgentTeamSigner
{
    private readonly byte[]? _key;
    public string KeyId { get; }
    public bool IsConfigured => _key is not null;

    public GccV2AgentTeamSigner(IConfiguration configuration)
    {
        // Prefer non-empty values: empty appsettings keys must not mask Railway env.
        var value = FirstNonEmpty(
            configuration["GccV2Agents:SnapshotSigningKey"],
            Environment.GetEnvironmentVariable("AGENT_TEAM_SNAPSHOT_SIGNING_KEY"),
            configuration["GccV2Skills:SnapshotSigningKey"],
            Environment.GetEnvironmentVariable("SKILL_SNAPSHOT_SIGNING_KEY"));
        if (!string.IsNullOrWhiteSpace(value))
        {
            _key = Encoding.UTF8.GetBytes(value);
            if (_key.Length < 32) throw new InvalidOperationException("Agent team signing key must be at least 32 UTF-8 bytes.");
        }
        KeyId = FirstNonEmpty(
            configuration["GccV2Agents:SnapshotSigningKeyId"],
            Environment.GetEnvironmentVariable("AGENT_TEAM_SNAPSHOT_SIGNING_KEY_ID"),
            configuration["GccV2Skills:SnapshotSigningKeyId"],
            Environment.GetEnvironmentVariable("SKILL_SNAPSHOT_SIGNING_KEY_ID"))
            ?? "gcc-agent-teams-1";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        return null;
    }

    public string Sign(string digest)
    {
        if (_key is null) throw new InvalidOperationException("Agent team snapshot signing key is not configured.");
        return Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(digest))).ToLowerInvariant();
    }

    public bool Verify(string digest, string signature) =>
        _key is not null && signature.Length == 64
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Sign(digest)), Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));
}

public sealed class GccV2AgentTeamResolver(HttpGccV2Repository repo, GccV2AgentTeamSigner signer)
{
    public const string SnapshotVersion = "gcc-agent-team.v1";
    public const string CatalogVersion = "gcc-specialist-agents.2026-09-08";
    private static readonly HashSet<string> SafeTools =
    [
        "search_corpus", "load_evidence_page", "get_brief_context", "get_outline_context",
        "get_completed_section_summaries", "activate_skill", "read_skill_resource",
        "get_specialist_artifacts", "submit_contribution", "submit_review",
        "submit_research_plan", "submit_outline", "submit_section", "submit_repair",
        "submit_validation", "submit_final_synthesis",
    ];
    private static readonly IReadOnlyList<string> RequiredProducerStages =
        ["researchPlanning", "outline", "section", "repair", "validation", "finalSynthesis", "complete"];
    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<GccV2SignedAgentTeam> ResolveAsync(
        IReadOnlyList<Guid>? selectedVersionIds, string contentType, CancellationToken ct)
    {
        contentType = NormalizeContentType(contentType);
        if (!signer.IsConfigured)
            throw new InvalidOperationException("Agent team snapshot signing is required.");
        var catalog = await repo.ListAgentsAsync("published", contentType, ct);
        var allPublished = catalog.SelectMany(agent => agent.Versions
            .Where(v => v.State == "published").Select(version => (Agent: agent, Version: version))).ToList();
        var selected = selectedVersionIds is { Count: > 0 }
            ? allPublished.Where(x => selectedVersionIds.Contains(x.Version.Id)).ToList()
            : allPublished.GroupBy(x => x.Agent.Id).Select(group => group
                .OrderByDescending(x => Semver(x.Version.SemanticVersion))
                .ThenByDescending(x => x.Version.CreatedAtUtc).ThenBy(x => x.Version.Id).First()).ToList();
        if (selectedVersionIds is { Count: > 0 }
            && selected.Select(x => x.Version.Id).Distinct().Count() != selectedVersionIds.Distinct().Count())
            throw new InvalidOperationException("Every selected specialist version must be published and applicable.");
        var members = selected.Select(ToMember).OrderBy(x => x.Participation.Min(p => p.Order))
            .ThenBy(x => x.Slug, StringComparer.Ordinal).ToList();
        Validate(members, contentType);
        var snapshot = new GccV2AgentTeamSnapshot(
            SnapshotVersion, CatalogVersion, DateTimeOffset.UtcNow, members);
        var json = JsonSerializer.Serialize(snapshot, CanonicalJson);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new(snapshot, json, digest, signer.Sign(digest), signer.KeyId);
    }

    public async Task<GccV2SignedAgentTeam> ResolveStableAsync(
        IReadOnlyList<string>? selectedAgentIds, string contentType, CancellationToken ct)
    {
        if (selectedAgentIds is not { Count: > 0 })
            return await ResolveAsync(null, contentType, ct);
        var ids = selectedAgentIds.Select(x => x.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var catalog = await repo.ListAgentsAsync("published", NormalizeContentType(contentType), ct);
        var selected = catalog.Where(x => ids.Contains(x.Slug, StringComparer.OrdinalIgnoreCase)).ToList();
        if (selected.Count != ids.Count)
            throw new InvalidOperationException("Every selectedAgentId must identify a published applicable specialist.");
        var versions = selected.Select(agent => agent.Versions.Where(x => x.State == "published")
                .OrderByDescending(x => Semver(x.SemanticVersion)).FirstOrDefault()
                ?? throw new InvalidOperationException($"Specialist '{agent.Slug}' has no published version."))
            .Select(x => x.Id).ToList();
        return await ResolveAsync(versions, contentType, ct);
    }

    public GccV2AgentTeamSnapshot ValidatePersisted(GccV2JobDto job)
    {
        if (string.IsNullOrWhiteSpace(job.AgentTeamSnapshotJson)
            || string.IsNullOrWhiteSpace(job.AgentTeamSnapshotDigest)
            || string.IsNullOrWhiteSpace(job.AgentTeamSnapshotSignature))
            throw new InvalidOperationException("Job has no signed specialist team snapshot.");
        var digest = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(job.AgentTeamSnapshotJson))).ToLowerInvariant();
        if (!string.Equals(digest, job.AgentTeamSnapshotDigest, StringComparison.Ordinal)
            || !signer.Verify(digest, job.AgentTeamSnapshotSignature))
            throw new InvalidOperationException("Specialist team snapshot signature validation failed.");
        var snapshot = JsonSerializer.Deserialize<GccV2AgentTeamSnapshot>(
            job.AgentTeamSnapshotJson, CanonicalJson)
            ?? throw new InvalidOperationException("Specialist team snapshot is malformed.");
        Validate(snapshot.Agents, job.ContentType);
        return snapshot;
    }

    public async Task<GccV2SignedAgentTeam> ResolveChildAsync(
        GccV2JobDto parent, string childContentType, CancellationToken ct)
    {
        childContentType = NormalizeContentType(childContentType);
        var parentSnapshot = ValidatePersisted(parent);
        var inherited = parentSnapshot.Agents.Where(agent =>
            agent.ContentTypes.Contains(childContentType, StringComparer.Ordinal)
            && agent.Participation.All(participation => agent.Skills.All(skill =>
                skill.ContentTypes.Contains(childContentType, StringComparer.Ordinal)
                && skill.Stages.Contains(participation.Stage, StringComparer.Ordinal)))).ToList();
        Validate(inherited, childContentType);
        var snapshot = new GccV2AgentTeamSnapshot(
            SnapshotVersion, parentSnapshot.CatalogVersion, DateTimeOffset.UtcNow, inherited);
        var json = JsonSerializer.Serialize(snapshot, CanonicalJson);
        var digest = Hash(json);
        return new(snapshot, json, digest, signer.Sign(digest), signer.KeyId);
    }

    private static GccV2AgentTeamMember ToMember((GccV2AgentDto Agent, GccV2AgentVersionDto Version) item)
    {
        static IReadOnlyList<string> Strings(string json) =>
            JsonSerializer.Deserialize<List<string>>(json) ?? [];
        var contentTypes = Strings(item.Version.ContentTypesJson);
        var tools = Strings(item.Version.AllowedToolsJson);
        var models = Strings(item.Version.AllowedModelsJson);
        IReadOnlyList<string> requiredContextKinds = [];
        IReadOnlyList<string> allowedContextKinds =
            ["brief", "brand_kit", "audience", "style_guide", "visual_guideline", "product_schema", "product", "knowledge", "run_attachment"];
        var instructionsDigest = Hash(item.Version.Instructions);
        var policy = JsonSerializer.Serialize(new
        {
            objective = item.Version.Objective, modelPolicyVersion = item.Version.ModelPolicyVersion,
            modelPolicyProfile = item.Version.ModelPolicyProfile,
            contentTypes, tools, models, requiredContextKinds, allowedContextKinds,
            skillVersionIds = item.Version.Skills.OrderBy(x => x.Order).Select(x => x.SkillVersionId),
        }, CanonicalJson);
        return new(
            item.Agent.Id, item.Version.Id, item.Agent.Slug, item.Agent.DisplayName,
            item.Version.SemanticVersion, item.Version.VersionDigest,
            item.Version.Objective, item.Version.Instructions, instructionsDigest,
            item.Version.ModelPolicyVersion, item.Version.ModelPolicyProfile, policy, Hash(policy),
            contentTypes, tools, models,
            item.Version.Skills.OrderBy(x => x.Order).Select(x => new GccV2AgentTeamSkillPin(
                x.SkillVersionId, x.SkillVersion.Package.Slug, x.SkillVersion.SemanticVersion,
                x.SkillVersion.PackageSha256,
                x.SkillVersion.Applicability.Select(a => a.Stage).Distinct(StringComparer.Ordinal).Order().ToList(),
                x.SkillVersion.Applicability.Select(a => a.ContentType).Distinct(StringComparer.Ordinal).Order().ToList(),
                x.SkillVersion.Applicability.SelectMany(a =>
                    JsonSerializer.Deserialize<List<string>>(a.RequiredToolsJson) ?? [])
                    .Distinct(StringComparer.Ordinal).Order().ToList(),
                x.SkillVersion.Applicability.SelectMany(a =>
                    JsonSerializer.Deserialize<List<string>>(a.ConflictsJson) ?? [])
                    .Distinct(StringComparer.Ordinal).Order().ToList(),
                x.SkillVersion.Applicability.Select(a => a.ActivationMode)
                    .Distinct(StringComparer.Ordinal).Order().ToList())).ToList(),
            item.Version.StageParticipation.OrderBy(x => x.Order).Select(x =>
                new GccV2AgentTeamParticipation(x.Stage, x.Role, x.Order)).ToList(),
            requiredContextKinds, allowedContextKinds);
    }

    public static void Validate(IReadOnlyList<GccV2AgentTeamMember> agents, string contentType)
    {
        contentType = NormalizeContentType(contentType);
        if (agents.Count == 0) throw new InvalidOperationException("At least one specialist is required.");
        if (agents.Select(x => x.AgentVersionId).Distinct().Count() != agents.Count)
            throw new InvalidOperationException("Duplicate specialist versions are prohibited.");
        if (agents.Select(x => x.AgentId).Distinct().Count() != agents.Count)
            throw new InvalidOperationException("Only one immutable version of each specialist may be selected.");
        var allSkills = agents.SelectMany(x => x.Skills).ToList();
        if (allSkills.GroupBy(x => x.Slug, StringComparer.Ordinal).Any(x =>
                x.Select(skill => skill.SkillVersionId).Distinct().Count() > 1))
            throw new InvalidOperationException("Duplicate skill IDs or versions are prohibited.");
        var selectedSkillIds = allSkills.Select(x => x.Slug).ToHashSet(StringComparer.Ordinal);
        foreach (var skill in allSkills)
        {
            if ((skill.ActivationModes ?? ["explicit"]).Any(x => x is not ("automatic" or "explicit")))
                throw new InvalidOperationException($"Skill '{skill.Slug}' has an invalid activation mode.");
            if ((skill.Conflicts ?? []).Any(selectedSkillIds.Contains))
                throw new InvalidOperationException($"Skill '{skill.Slug}' conflicts with the selected team.");
        }
        if (agents.Count(x => x.Participation.Any(p => p.Role == "producer")) != 1)
            throw new InvalidOperationException("A specialist team must contain exactly one producer.");
        foreach (var stage in RequiredProducerStages)
            if (agents.SelectMany(x => x.Participation)
                    .Count(x => x.Stage == stage && x.Role == "producer") != 1)
                throw new InvalidOperationException(
                    $"A specialist team must contain exactly one producer for {stage}.");
        foreach (var agent in agents)
        {
            if (string.IsNullOrWhiteSpace(agent.Instructions)
                || string.IsNullOrWhiteSpace(agent.Objective)
                || agent.ModelPolicyVersion != ContentModelPolicy.CurrentVersion
                || string.IsNullOrWhiteSpace(agent.ModelPolicyProfile)
                || !string.Equals(Hash(agent.Instructions), agent.InstructionsDigest, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(agent.Policy)
                || !string.Equals(Hash(agent.Policy), agent.PolicyDigest, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Specialist '{agent.Slug}' has invalid instruction or policy digests.");
            if (!agent.ContentTypes.Contains(contentType, StringComparer.Ordinal))
                throw new InvalidOperationException($"Specialist '{agent.Slug}' does not support '{contentType}'.");
            if (agent.Skills.Count == 0)
                throw new InvalidOperationException($"Specialist '{agent.Slug}' must have explicitly assigned skills.");
            if (agent.AllowedTools.Except(SafeTools, StringComparer.Ordinal).Any())
                throw new InvalidOperationException($"Specialist '{agent.Slug}' requests a prohibited tool.");
            foreach (var participation in agent.Participation)
            {
                if (participation.Role is not ("contributor" or "producer" or "reviewer"))
                    throw new InvalidOperationException($"Specialist '{agent.Slug}' has an invalid role.");
                if (!agent.AllowedModels.Any(model => ContentModelPolicy.IsApproved(participation.Stage, model)))
                    throw new InvalidOperationException(
                        $"Specialist '{agent.Slug}' has no approved model intersection for {participation.Stage}.");
                foreach (var skill in agent.Skills)
                {
                    if (!skill.ContentTypes.Contains(contentType, StringComparer.Ordinal)
                        || !skill.Stages.Contains(participation.Stage, StringComparer.Ordinal))
                        throw new InvalidOperationException(
                            $"Assigned skill '{skill.Slug}' is outside {contentType}/{participation.Stage}.");
                    if (skill.RequiredTools.Except(agent.AllowedTools, StringComparer.Ordinal).Any())
                        throw new InvalidOperationException(
                            $"Specialist '{agent.Slug}' does not allow every tool required by '{skill.Slug}'.");
                }
            }
        }
    }

    private static string NormalizeContentType(string contentType) =>
        string.Equals(contentType, GccV2ChannelTypes.LinkedInCarousel, StringComparison.OrdinalIgnoreCase)
            ? GccV2ChannelTypes.LinkedInDocument
            : contentType.Trim().ToLowerInvariant();
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static Version Semver(string value) =>
        Version.TryParse(value, out var parsed) ? parsed : new Version(0, 0, 0);
}
