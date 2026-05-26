namespace GeekApplication.Models.Seo;

public sealed record SerpRequest
{
    public required string Keyword { get; init; }
    public string Location { get; init; } = "United States";
    public string LanguageCode { get; init; } = "en";
    public string CountryCode { get; init; } = "US";
    public int ResultCount { get; init; } = 10;
    public string Device { get; init; } = "desktop";
}

public sealed record SerpResult
{
    public required string Keyword { get; init; }
    public required string Location { get; init; }
    public required IReadOnlyList<SerpOrganicResult> OrganicResults { get; init; }
    public IReadOnlyList<PeopleAlsoAskResult> PeopleAlsoAsk { get; init; } = [];
    public IReadOnlyList<string> RelatedSearches { get; init; } = [];
    public string? FeaturedSnippetText { get; init; }
    public required SerpFeatures Features { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
}

public sealed record SerpFeatures
{
    public bool HasFeaturedSnippet { get; init; }
    public bool HasPeopleAlsoAsk { get; init; }
    public bool HasLocalPack { get; init; }
    public bool HasImagePack { get; init; }
    public bool HasVideoCarousel { get; init; }
    public bool HasKnowledgePanel { get; init; }
}

public sealed record SerpOrganicResult
{
    public required int Position { get; init; }
    public required string Url { get; init; }
    public required string Title { get; init; }
    public required string Snippet { get; init; }
    public string? Domain { get; init; }
}

public sealed record PeopleAlsoAskResult
{
    public required string Question { get; init; }
    public string? Answer { get; init; }
}

/// <summary>Stored in seo_serp_results.results JSON for scoring benchmarks.</summary>
public sealed record SerpBenchmarksPayload
{
    public int AvgWordCount { get; init; }
    public int AvgTitleLength { get; init; }
    public string BenchmarkQuality { get; init; } = "good";
    public IReadOnlyList<string> TopDomains { get; init; } = [];
    public IReadOnlyList<SerpOrganicResult> OrganicResults { get; init; } = [];
}
