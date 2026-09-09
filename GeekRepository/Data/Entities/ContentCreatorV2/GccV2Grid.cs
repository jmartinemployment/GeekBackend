using System.Text.Json.Serialization;

namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>Owner-scoped durable batch grid (work-item rows + run history).</summary>
public sealed class GccV2Grid
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>draft | ready | running | complete</summary>
    public string Status { get; set; } = "draft";
    /// <summary>Columns + budget policy JSON.</summary>
    public string ConfigJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<GccV2GridRow> Rows { get; set; } = [];
    public List<GccV2GridRun> Runs { get; set; } = [];
}

/// <summary>Work-item row within a grid.</summary>
public sealed class GccV2GridRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GridId { get; set; }
    public int RowIndex { get; set; }
    public string InputJson { get; set; } = "{}";
    public string? OutputJson { get; set; }
    /// <summary>pending | running | succeeded | failed | skipped</summary>
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public GccV2Grid Grid { get; set; } = null!;
}

/// <summary>Sample or full stub run over grid rows.</summary>
public sealed class GccV2GridRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GridId { get; set; }
    /// <summary>sample | full</summary>
    public string Mode { get; set; } = "sample";
    public int? SampleSize { get; set; }
    /// <summary>queued | running | succeeded | failed</summary>
    public string Status { get; set; } = "queued";
    public string ActorUserId { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string BudgetPreviewJson { get; set; } = "{}";
    public string HistoryJson { get; set; } = "[]";
    public int OutputCount { get; set; }
    [JsonIgnore] public GccV2Grid Grid { get; set; } = null!;
}
