namespace GeekApplication.Models.Seo;

public sealed record KeywordRequest
{
    public required IReadOnlyList<string> Keywords { get; init; }
    public string Location { get; init; } = "United States";
    public string LanguageCode { get; init; } = "en";
}

public sealed record KeywordResult
{
    public required string Keyword { get; init; }
    public required int SearchVolume { get; init; }
    public required double KeywordDifficulty { get; init; }
    public required double CpcUsd { get; init; }
    public required string Competition { get; init; }
    public IReadOnlyList<MonthlySearchVolume> MonthlyTrend { get; init; } = [];
}

public sealed record MonthlySearchVolume
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required int Volume { get; init; }
}

public sealed record KeywordCluster
{
    public required string ClusterName { get; init; }
    public required string PillarKeyword { get; init; }
    public required IReadOnlyList<string> Keywords { get; init; }
    public required double AverageVolume { get; init; }
    public required double AverageDifficulty { get; init; }
}

public sealed record KeywordResearchRequest
{
    public required Guid ProjectId { get; init; }
    public required string SeedKeyword { get; init; }
    public string Location { get; init; } = "United States";
    public int ResultCount { get; init; } = 25;
}

public sealed record ClusterKeywordsRequest
{
    public required Guid ProjectId { get; init; }
    public required IReadOnlyList<string> Keywords { get; init; }
    public string Location { get; init; } = "United States";
}
