namespace GeekAPI.Models;

public sealed record SiteAnalyzer2SiteProfileExport
{
    public required Guid Id { get; init; }
    public required string SiteUrl { get; init; }
    public string? DisplayName { get; init; }
    public Guid? GeekSeoProjectId { get; init; }
    public string? PrimaryFocus { get; init; }
    public string? FocusDescription { get; init; }
    public IReadOnlyList<string> FocusTags { get; init; } = [];
    public string? BusinessSummary { get; init; }
    public IReadOnlyList<string> GeoAnchorNodes { get; init; } = [];
    public string? ServiceAreaDescription { get; init; }
    public IReadOnlyList<string> CompetitorDomains { get; init; } = [];
    public IReadOnlyList<string> AuthorityPageUrls { get; init; } = [];
    public IReadOnlyList<string> WritingRecommendations { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}
