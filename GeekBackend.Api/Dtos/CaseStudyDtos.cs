namespace GeekBackend.Api.Dtos;

// public record DepartmentDto(int Id, string Name, string Slug, string Description, string? IconName, int SortOrder);

// public record UseCaseDto(int Id, string DescriptiveName, string Slug, string Summary, int? CaseStudyId);

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

public class CaseStudySummaryDto
{
    public CaseStudySummaryDto(int id, string descriptiveName, string slug, string status, string executiveSummary, string primaryActor, DateTime? publishedAt)
    {
        Id = id;
        DescriptiveName = descriptiveName;
        Slug = slug;
        Status = status;
        ExecutiveSummary = executiveSummary;
        PrimaryActor = primaryActor;
        PublishedAt = publishedAt;
    }
    public int Id { get; set; }
    public string DescriptiveName { get; set; }
    public string Slug { get; set; }
    public string Status { get; set; }
    public string ExecutiveSummary { get; set; }
    public string PrimaryActor { get; set; }
    public DateTime? PublishedAt { get; set; }
}
public class CaseStudyDto
{
    public int Id { get; set; }
    public string DescriptiveName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ExecutiveSummary { get; set; } = string.Empty;
    public string PrimaryActor { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string ProblemChallenge { get; set; } = string.Empty;
    public string Solution { get; set; } = string.Empty;
    public string? PostConditions { get; set; }
    public string? IndustryCitation { get; set; }
}