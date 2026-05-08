namespace GeekAPI.Dtos;

// ── Department ───────────────────────────────────────────────────────────────

public record DepartmentDto(
    int Id,
    string Name,
    string Slug,
    string? Description,
    string? IconName,
    int SortOrder
)
{
    public List<UseCaseDto>? UseCases { get; init; }
}

public record DepartmentRequest(
    string Name,
    string Slug,
    string? Description,
    string? IconName,
    int SortOrder
);

// ── UseCase ──────────────────────────────────────────────────────────────────

public record UseCaseDto(
    int Id,
    string DescriptiveName,
    string Slug,
    string? Summary,
    int? CaseStudyId
)
{
    public CaseStudyDto? CaseStudy { get; init; }
}

public record UseCaseRequest(
    string DescriptiveName,
    string Slug,
    string? Summary,
    int DepartmentId,
    int? CaseStudyId
);

// ── CaseStudy (Minimal for nesting) ─────────────────────────────────────────

public class CaseStudyDto
{
    public int Id { get; set; }
    public string? DescriptiveName { get; set; }
    public string? Slug { get; set; }
    public string? ExecutiveSummary { get; set; }
    public string? Subtitle { get; set; }
    public string? PrimaryActor { get; set; }
    public string? Trigger { get; set; }
    public string? ProblemChallenge { get; set; }
    public string? Solution { get; set; }
    public string? PostConditions { get; set; }
    public string? IndustryCitation { get; set; }
}

// ── CaseStudy Summary and Detail ─────────────────────────────────────────────

public record CaseStudySummaryDto(
    int Id,
    string DescriptiveName,
    string Slug,
    string Status,
    string? ExecutiveSummary,
    string? Subtitle,
    string? PrimaryActor,
    DateTime? PublishedAt
);

public record CaseStudyDetailDto(
    int Id,
    string DescriptiveName,
    string Slug,
    string Status,
    string? ExecutiveSummary,
    string? Subtitle,
    string? PrimaryActor,
    string? Trigger,
    string? ProblemChallenge,
    string? Solution,
    string? PostConditions,
    string? Exceptions,
    string? IndustryCitation,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? PublishedAt,
    List<ActorDto> Actors,
    List<MetricDto> Metrics,
    List<EventFlowStepDto> EventFlowSteps
);

public record CaseStudyRequest(
    string DescriptiveName,
    string Slug,
    string Status,
    string? ExecutiveSummary,
    string? Subtitle,
    string? PrimaryActor,
    string? Trigger,
    string? ProblemChallenge,
    string? Solution,
    string? PostConditions,
    string? Exceptions,
    string? IndustryCitation
);

// ── CaseStudy Actor ──────────────────────────────────────────────────────────

public record ActorDto(
    int Id,
    string ActorName,
    string ActorRole,
    int SortOrder
);

public record ActorRequest(
    string ActorName,
    string ActorRole,
    int SortOrder
);

// ── CaseStudy Metric ─────────────────────────────────────────────────────────

public record MetricDto(
    int Id,
    string MetricLabel,
    string MetricValue,
    string? MetricUnit,
    int SortOrder
);

public record MetricRequest(
    string MetricLabel,
    string MetricValue,
    string? MetricUnit,
    int SortOrder
);

// ── CaseStudy Event Flow Step ────────────────────────────────────────────────

public record EventFlowStepDto(
    int Id,
    int StepNumber,
    string StepDescription,
    string? ActorName
);

public record EventFlowStepRequest(
    int StepNumber,
    string StepDescription,
    int? StepActorId
);
