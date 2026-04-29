namespace GeekBackend.Api.Dtos;

public record DepartmentRequest(
    string Name,
    string Slug,
    string Description,
    string? IconName,
    int SortOrder);

public record UseCaseRequest(
    int DepartmentId,
    int CaseStudyId,
    string DescriptiveName,
    string Slug,
    string Summary);

public record CaseStudyRequest(
    string DescriptiveName,
    string Slug,
    string Status,
    string ExecutiveSummary,
    string? Subtitle,
    string PrimaryActor,
    string Trigger,
    string ProblemChallenge,
    string Solution,
    string PostConditions,
    string Exceptions,
    string IndustryCitation);

public record ActorRequest(string ActorName, string ActorRole, int SortOrder);

public record MetricRequest(string MetricLabel, string MetricValue, string? MetricUnit, int SortOrder);

public record EventFlowStepRequest(int StepNumber, string StepDescription, int? StepActorId);
