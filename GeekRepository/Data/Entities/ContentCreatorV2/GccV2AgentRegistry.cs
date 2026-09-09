using System.Text.Json.Serialization;

namespace GeekRepository.Data.Entities.ContentCreatorV2;

public static class GccV2AgentLifecycle
{
    public static readonly IReadOnlySet<string> Values =
        new HashSet<string>(["draft", "approved", "published", "deprecated", "revoked"], StringComparer.Ordinal);
}

/// <summary>Stable specialist identity. Runtime jobs always pin an immutable version.</summary>
public class GccV2Agent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LifecycleState { get; set; } = "draft";
    public bool IsFirstParty { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public List<GccV2AgentVersion> Versions { get; set; } = [];
}

/// <summary>
/// Immutable once published. Instructions and policy are digest-pinned; changes create a new version.
/// </summary>
public class GccV2AgentVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AgentId { get; set; }
    public string SemanticVersion { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string ContentTypesJson { get; set; } = "[]";
    public string AllowedToolsJson { get; set; } = "[]";
    public string AllowedModelsJson { get; set; } = "[]";
    public string ModelPolicyVersion { get; set; } = "content-model-policy.v1";
    public string ModelPolicyProfile { get; set; } = "default";
    public string VersionDigest { get; set; } = string.Empty;
    public string State { get; set; } = "draft";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public DateTimeOffset? TestedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public DateTimeOffset? DeprecatedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string? Reviewer { get; set; }
    public string? ReviewNotes { get; set; }
    public string? TestResultJson { get; set; }
    [JsonIgnore] public GccV2Agent Agent { get; set; } = null!;
    public List<GccV2AgentVersionSkillVersion> Skills { get; set; } = [];
    public List<GccV2AgentStageParticipation> StageParticipation { get; set; } = [];
    public List<GccV2AgentTestRun> TestRuns { get; set; } = [];
    public List<GccV2AgentReviewFinding> Findings { get; set; } = [];
}

public class GccV2AgentReviewFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AgentVersionId { get; set; }
    public string Severity { get; set; } = "info";
    public string Rule { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Disposition { get; set; } = "unreviewed";
    public string? ReviewerRationale { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DisposedAtUtc { get; set; }
    [JsonIgnore] public GccV2AgentVersion AgentVersion { get; set; } = null!;
}

public class GccV2AgentTestRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AgentVersionId { get; set; }
    public string VersionDigest { get; set; } = string.Empty;
    public string Scenario { get; set; } = "contract";
    public string InputJson { get; set; } = "{}";
    public string Status { get; set; } = "queued";
    public int ProgressPercent { get; set; }
    public string Phase { get; set; } = "queued";
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset QueuedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? ClaimedByInstanceId { get; set; }
    public DateTimeOffset? ClaimedAtUtc { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public int AttemptCount { get; set; }
    public int RecoveryCount { get; set; }
    public DateTimeOffset? CancellationRequestedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    [JsonIgnore] public GccV2AgentVersion AgentVersion { get; set; } = null!;
}

/// <summary>Exact governed skill-version assignment; never resolves a floating package.</summary>
public class GccV2AgentVersionSkillVersion
{
    public Guid AgentVersionId { get; set; }
    public Guid SkillVersionId { get; set; }
    public int Order { get; set; }
    [JsonIgnore] public GccV2AgentVersion AgentVersion { get; set; } = null!;
    public GccV2SkillVersion SkillVersion { get; set; } = null!;
}

public class GccV2AgentStageParticipation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AgentVersionId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int Order { get; set; }
    [JsonIgnore] public GccV2AgentVersion AgentVersion { get; set; } = null!;
}

public class GccV2AgentAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AgentId { get; set; }
    public Guid? AgentVersionId { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public string? DetailJson { get; set; }
    public string? SourceIp { get; set; }
    public string? RequestId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Relational pin supporting referential integrity in addition to the signed job snapshot.</summary>
public class GccV2JobAgentVersion
{
    public Guid JobId { get; set; }
    public Guid AgentVersionId { get; set; }
    public int Order { get; set; }
    [JsonIgnore] public GccV2Job Job { get; set; } = null!;
    public GccV2AgentVersion AgentVersion { get; set; } = null!;
}
