namespace GeekApplication.Models.Seo;

public sealed record InternalLinkSuggestRequest
{
    public required Guid ProjectId { get; init; }
    public required Guid DocumentId { get; init; }
    public int MaxSuggestions { get; init; } = 10;
}

public sealed record InternalLinkSuggestion
{
    public required string AnchorText { get; init; }
    public required string TargetUrl { get; init; }
    public required string Reason { get; init; }
    public required double RelevanceScore { get; init; }
}
