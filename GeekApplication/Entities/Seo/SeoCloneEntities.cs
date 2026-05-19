namespace GeekApplication.Entities.Seo;

public sealed class SeoWordPressConnection
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public required string SiteUrl { get; set; }
    public required string Username { get; set; }
    public byte[] EncryptedAppPassword { get; set; } = [];
    public byte[] EncryptionIv { get; set; } = [];
    public byte[] EncryptionTag { get; set; } = [];
    public string DefaultPostStatus { get; set; } = "draft";
    public DateTimeOffset ConnectedAt { get; set; }
}

public sealed class SeoPublishedPage
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? DocumentId { get; set; }
    public required string Url { get; set; }
    public int? WordPressPostId { get; set; }
    public string? TargetKeyword { get; set; }
    public DateTimeOffset? LastAuditAt { get; set; }
}

public sealed class SeoContentPerformanceSnapshot
{
    public Guid Id { get; set; }
    public Guid PublishedPageId { get; set; }
    public DateOnly Date { get; set; }
    public decimal? Position { get; set; }
    public int Impressions { get; set; }
    public int Clicks { get; set; }
    public decimal Ctr { get; set; }

    public SeoPublishedPage? PublishedPage { get; set; }
}

public sealed class SeoTopicalMap
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Status { get; set; } = "pending";
    public string ClustersJson { get; set; } = "[]";
    public string ContentGapsJson { get; set; } = "[]";
    public DateTimeOffset? GeneratedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class SeoSitePageInventory
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string Url { get; set; }
    public string? Title { get; set; }
    public string? H1 { get; set; }
    public int WordCount { get; set; }
    public DateTimeOffset CrawledAt { get; set; }
}

public sealed class SeoBrandVoice
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public required string SampleText { get; set; }
    public string? StyleInstructions { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SeoBulkJob
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "pending";
    public string KeywordsJson { get; set; } = "[]";
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class SeoPlagiarismCheck
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public decimal MatchPercent { get; set; }
    public string MatchesJson { get; set; } = "[]";
    public DateTimeOffset CheckedAt { get; set; }
}

public sealed class SeoGa4Connection
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string PropertyId { get; set; }
    public byte[] EncryptedRefreshToken { get; set; } = [];
    public byte[] EncryptionIv { get; set; } = [];
    public byte[] EncryptionTag { get; set; } = [];
    public DateTimeOffset ConnectedAt { get; set; }
}

public sealed class SeoGeoTrackingQuery
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string QueryText { get; set; }
    public string PlatformsJson { get; set; } = "[\"google_aio\"]";
    public bool Enabled { get; set; } = true;

    public ICollection<SeoGeoMentionSnapshot> Snapshots { get; set; } = [];
}

public sealed class SeoGeoMentionSnapshot
{
    public Guid Id { get; set; }
    public Guid QueryId { get; set; }
    public required string Platform { get; set; }
    public bool Mentioned { get; set; }
    public string? Snippet { get; set; }
    public DateTimeOffset CheckedAt { get; set; }

    public SeoGeoTrackingQuery? Query { get; set; }
}

public sealed class SeoCannibalizationIssue
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string Keyword { get; set; }
    public string CompetingUrlsJson { get; set; } = "[]";
    public required string Severity { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
}

public sealed class SeoApiKey
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string KeyHash { get; set; }
    public required string KeyPrefix { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class SeoSerpDeepCache
{
    public Guid Id { get; set; }
    public required string Keyword { get; set; }
    public required string Location { get; set; }
    public int ResultCount { get; set; } = 50;
    public string ResultsJson { get; set; } = "[]";
    public string TermMatrixJson { get; set; } = "{}";
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
