namespace GeekApplication.Models.Seo;

public sealed record DeepSerpRequest
{
    public required string Keyword { get; init; }
    public string Location { get; init; } = "United States";
    public string LanguageCode { get; init; } = "en";
}

public sealed record DeepSerpResult
{
    public required string Keyword { get; init; }
    public required string Location { get; init; }
    public required string Provider { get; init; }
    public required IReadOnlyList<DeepSerpOrganic> Organic { get; init; }
    public required IReadOnlyList<string> PeopleAlsoAsk { get; init; }
    public required IReadOnlyList<string> RelatedSearches { get; init; }
    public required SerpIntentSummary Intent { get; init; }
}

public sealed record DeepSerpOrganic
{
    public required int Position { get; init; }
    public required string Url { get; init; }
    public string? Title { get; init; }
    public string? Snippet { get; init; }
    public string? Domain { get; init; }
}

public sealed record SerpIntentSummary
{
    public required string PrimaryIntent { get; init; }
    public required IReadOnlyList<string> ContentFormats { get; init; }
    public required double AvgSnippetLength { get; init; }
}
