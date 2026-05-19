namespace GeekApplication.Models.Seo;

public sealed record ContentScoreHubResult
{
    public ScoreUpdateMessage? ScoreUpdate { get; init; }
    public string? PendingReason { get; init; }
}

public sealed record ScoreUpdateMessage
{
    public required int Score { get; init; }
    public required string Grade { get; init; }
    public required object Components { get; init; }
    public required IReadOnlyList<ScoreSuggestion> Suggestions { get; init; }
    public IReadOnlyList<SerpFeatureGuidance> SerpFeatures { get; init; } = [];
    public IReadOnlyList<EeatAdvisory> EeatAdvisories { get; init; } = [];
    public string BenchmarkQuality { get; init; } = "good";
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed record ScoreSuggestion
{
    public required string Component { get; init; }
    public required int PointValue { get; init; }
    public required string ActionText { get; init; }
}

public sealed record SerpFeatureGuidance
{
    public required string Feature { get; init; }
    public required string ActionText { get; init; }
}

public sealed record EeatAdvisory
{
    public required string Code { get; init; }
    public required string ActionText { get; init; }
}

public sealed record AutoOptimizeResult
{
    public required string ContentHtml { get; init; }
    public required int PreviousScore { get; init; }
    public required int EstimatedScore { get; init; }
    public required IReadOnlyList<string> ChangesApplied { get; init; }
    public ScoreUpdateMessage? ScoreUpdate { get; init; }
}
