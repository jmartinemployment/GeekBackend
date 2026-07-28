namespace GeekApplication.Models.ContentWriterV3;

public sealed record JobDto(
    Guid Id,
    string JobType,
    string Status,
    Dictionary<string, object> PayloadJson,
    string IdempotencyKey,
    int AttemptCount,
    string? LeaseOwner,
    DateTime? LeaseExpiresAtUtc,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    Guid? RequeuedByUserId,
    DateTime? RequeuedAtUtc);

public sealed record CreateJobCommand(
    string JobType,
    Dictionary<string, object> PayloadJson,
    string IdempotencyKey);

public sealed record UpdateJobStatusCommand(
    Guid Id,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime? CompletedAtUtc);

public sealed record LeaseJobCommand(
    Guid Id,
    string LeaseOwner,
    TimeSpan LeaseDuration);

public sealed record ReleaseJobLeaseCommand(
    Guid Id);

public sealed record ApprovalEventDto(
    Guid Id,
    Guid AssetVersionId,
    Guid UserId,
    string Action,
    string? Notes,
    DateTime CreatedAtUtc);

public sealed record CreateApprovalEventCommand(
    Guid AssetVersionId,
    Guid UserId,
    string Action,
    string? Notes);

public sealed record PublicationEventDto(
    Guid Id,
    Guid PublicationId,
    Guid UserId,
    string Status,
    string? Details,
    DateTime CreatedAtUtc);

public sealed record CreatePublicationEventCommand(
    Guid PublicationId,
    Guid UserId,
    string Status,
    string? Details);

public sealed record ReviewCommentDto(
    Guid Id,
    Guid AssetVersionId,
    Guid UserId,
    string? SectionPath,
    string Content,
    string? Resolution,
    DateTime CreatedAtUtc);

public sealed record CreateReviewCommentCommand(
    Guid AssetVersionId,
    Guid UserId,
    string? SectionPath,
    string Content);

public sealed record ResolveReviewCommentCommand(
    Guid Id,
    string Resolution);

public sealed record ReconciliationProposalDto(
    Guid Id,
    Guid ResearchRunId,
    string ProposalType,
    Guid? PainPointId,
    Dictionary<string, object> ProposedData,
    string Status,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAtUtc,
    DateTime CreatedAtUtc);

public sealed record CreateReconciliationProposalCommand(
    Guid ResearchRunId,
    string ProposalType,
    Guid? PainPointId,
    Dictionary<string, object> ProposedData);

public sealed record ApproveReconciliationProposalCommand(
    Guid Id,
    Guid UserId);

public sealed record DismissReconciliationProposalCommand(
    Guid Id,
    Guid UserId);
