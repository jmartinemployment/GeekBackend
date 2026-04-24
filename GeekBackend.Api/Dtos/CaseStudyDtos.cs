namespace GeekBackend.Api.Dtos;

public record DepartmentDto(int Id, string Name, string Slug, string Description, string? IconName, int SortOrder);

public record UseCaseDto(int Id, string DescriptiveName, string Slug, string Summary, int? CaseStudyId);

public record CaseStudySummaryDto(
    int Id, string DescriptiveName, string Slug, string Status,
    string ExecutiveSummary, string PrimaryActor, DateTime? PublishedAt);

public record CaseStudyDetailDto(
    int Id, string DescriptiveName, string Slug, string Status,
    string ExecutiveSummary, string PrimaryActor, string Trigger,
    string ProblemChallenge, string Solution,
    string? PostConditions, string? Exceptions, string? IndustryCitation,
    DateTime? CreatedAt, DateTime? UpdatedAt, DateTime? PublishedAt,
    IReadOnlyList<ActorDto> Actors,
    IReadOnlyList<MetricDto> Metrics,
    IReadOnlyList<EventFlowStepDto> EventFlowSteps);

public record ActorDto(int Id, string ActorName, string ActorRole, int SortOrder);

public record MetricDto(int Id, string MetricLabel, string MetricValue, string? MetricUnit, int SortOrder);

public record EventFlowStepDto(int Id, int StepNumber, string StepDescription, string? StepActorName);
