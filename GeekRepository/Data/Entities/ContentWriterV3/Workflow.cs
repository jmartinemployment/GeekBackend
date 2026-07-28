namespace GeekRepository.Data.Entities.ContentWriterV3;

public class StrategyBrief
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public Guid PainPointId { get; set; }
    public string AudienceProfile { get; set; } = string.Empty;
    public string BuyingStage { get; set; } = string.Empty;
    public string Angle { get; set; } = string.Empty;
    public string CallToAction { get; set; } = string.Empty;
    public string Status { get; set; } = "draft"; // draft, approved, rejected
    public uint RowVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Publication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetVersionId { get; set; }
    public string Status { get; set; } = "draft"; // draft, published, failed
    public Dictionary<string, object>? ResponseSnapshot { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string JobType { get; set; } = string.Empty;
    public string Status { get; set; } = "queued"; // queued, running, completed, failed
    public Dictionary<string, object> PayloadJson { get; set; } = new();
    public string IdempotencyKey { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? RequeuedByUserId { get; set; }
    public DateTime? RequeuedAtUtc { get; set; }
}

public class ApprovalEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetVersionId { get; set; }
    public Guid UserId { get; set; }
    public string Action { get; set; } = string.Empty; // approved, rejected, requested-changes
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class PublicationEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublicationId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = string.Empty; // initiated, success, failed
    public string? Details { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ReviewComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetVersionId { get; set; }
    public Guid UserId { get; set; }
    public string? SectionPath { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Resolution { get; set; } // pending, resolved, dismissed
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
