using System.Text.Json.Serialization;

namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>Owner-scoped Geek Content Pipeline definition (versioned stage DAG).</summary>
public sealed class GccV2PipelineDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>draft | published | deprecated</summary>
    public string Status { get; set; } = "draft";
    public int VersionNumber { get; set; } = 1;
    /// <summary>Canonical digest of StagesJson + pins.</summary>
    public string Digest { get; set; } = string.Empty;
    /// <summary>Immutable stage DAG JSON for this version.</summary>
    public string StagesJson { get; set; } = "[]";
    public string PolicyJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<GccV2PipelineRun> Runs { get; set; } = [];
}

/// <summary>One execution of a published pipeline definition version.</summary>
public sealed class GccV2PipelineRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineDefinitionId { get; set; }
    public int DefinitionVersionNumber { get; set; }
    public string DefinitionDigest { get; set; } = string.Empty;
    /// <summary>queued | running | paused | succeeded | failed | cancelled</summary>
    public string Status { get; set; } = "queued";
    public string ActorUserId { get; set; } = string.Empty;
    public string InputJson { get; set; } = "{}";
    public string HistoryJson { get; set; } = "[]";
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? PausedAtUtc { get; set; }
    public string? Error { get; set; }
    [JsonIgnore] public GccV2PipelineDefinition PipelineDefinition { get; set; } = null!;
    public List<GccV2PipelineWorkItem> WorkItems { get; set; } = [];
}

/// <summary>Work item (row) within a pipeline run — isolated failure unit.</summary>
public sealed class GccV2PipelineWorkItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineRunId { get; set; }
    public int WorkItemIndex { get; set; }
    public string InputJson { get; set; } = "{}";
    /// <summary>pending | running | succeeded | failed | cancelled | skipped</summary>
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public GccV2PipelineRun PipelineRun { get; set; } = null!;
    public List<GccV2PipelineStageAttempt> StageAttempts { get; set; } = [];
}

/// <summary>One stage attempt for a work item (task-agent or handoff).</summary>
public sealed class GccV2PipelineStageAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineWorkItemId { get; set; }
    public string StageKey { get; set; } = string.Empty;
    /// <summary>plan | create | adapt | activate | optimize</summary>
    public string LifecycleStage { get; set; } = string.Empty;
    /// <summary>task-agent | handoff | approval</summary>
    public string Kind { get; set; } = "task-agent";
    public string DisplayName { get; set; } = string.Empty;
    public string? CapabilityId { get; set; }
    public string? Handoff { get; set; }
    public int AttemptNumber { get; set; } = 1;
    /// <summary>pending | running | awaiting-approval | succeeded | failed | cancelled | skipped</summary>
    public string Status { get; set; } = "pending";
    public string? OutputJson { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    [JsonIgnore] public GccV2PipelineWorkItem WorkItem { get; set; } = null!;
}
