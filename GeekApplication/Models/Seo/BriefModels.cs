namespace GeekApplication.Models.Seo;

public sealed record ContentBrief
{
    public required string Keyword { get; init; }
    public required string Location { get; init; }
    public int TargetWordCount { get; init; }
    public int AvgTitleLength { get; init; }
    public IReadOnlyList<string> RecommendedTerms { get; init; } = [];
    public IReadOnlyList<string> SuggestedHeadings { get; init; } = [];
    public IReadOnlyList<BriefCompetitorSummary> TopCompetitors { get; init; } = [];
    public IReadOnlyList<string> PeopleAlsoAsk { get; init; } = [];
    public string BenchmarkQuality { get; init; } = "good";
}

public sealed record BriefCompetitorSummary
{
    public required int Position { get; init; }
    public required string Url { get; init; }
    public string? Title { get; init; }
    public int WordCount { get; init; }
}

public sealed record GenerateBriefRequest
{
    public required Guid ProjectId { get; init; }
    public required string Keyword { get; init; }
    public string Location { get; init; } = "United States";
    public int CompetitorCount { get; init; } = 10;
}

public sealed record WritingOutlineRequest
{
    public required string Keyword { get; init; }
    public required ContentBrief Brief { get; init; }
}

public sealed record WritingDraftRequest
{
    public required string Keyword { get; init; }
    public required string Outline { get; init; }
    public required ContentBrief Brief { get; init; }
    public int TargetWordCount { get; init; }
}

public sealed record HumanizeRequest
{
    public required Guid DocumentId { get; init; }
    public required string ContentHtml { get; init; }
}

public sealed record DetectAiRequest
{
    public required Guid DocumentId { get; init; }
    public required string ContentHtml { get; init; }
}

public sealed record AiDetectionResult
{
    public required double AiProbability { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public sealed record WritingTextResult
{
    public required string Content { get; init; }
}
