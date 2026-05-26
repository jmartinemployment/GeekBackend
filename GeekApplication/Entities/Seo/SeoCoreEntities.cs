namespace GeekApplication.Entities.Seo;

public sealed class SeoProject
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? OrgId { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public bool GscConnected { get; set; }
    public string DefaultLocation { get; set; } = "United States";
    public string DefaultLanguage { get; set; } = "en";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<SeoContentDocument> ContentDocuments { get; set; } = [];
}

public sealed class SeoContentDocument
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = "Untitled Document";
    public string ContentHtml { get; set; } = string.Empty;
    public string TargetKeyword { get; set; } = string.Empty;
    public string TargetLocation { get; set; } = "United States";
    public int SeoScore { get; set; }
    public int WordCount { get; set; }
    public string ScoreComponentsJson { get; set; } = "{}";
    public DateTimeOffset? LastScoredAt { get; set; }
    public string Status { get; set; } = "planned";
    public int? PublishedScore { get; set; }
    public int? PublishedWordCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public decimal? AiDetectionScore { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public SeoProject? Project { get; set; }
}

public sealed class SeoKeywordCluster
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string Name { get; set; }
    public required string PillarKeyword { get; set; }
    public string KeywordsJson { get; set; } = "[]";
    public int AverageVolume { get; set; }
    public decimal AverageDifficulty { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SeoKeyword
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? ClusterId { get; set; }
    public required string Keyword { get; set; }
    public string Location { get; set; } = "United States";
    public int? SearchVolume { get; set; }
    public decimal? KeywordDifficulty { get; set; }
    public string? Intent { get; set; }
    public DateTimeOffset? CachedAt { get; set; }
}

public sealed class SeoSerpResult
{
    public Guid Id { get; set; }
    public required string Keyword { get; set; }
    public required string Location { get; set; }
    public string LanguageCode { get; set; } = "en";
    public string ResultsJson { get; set; } = "[]";
    public string PeopleAlsoAskJson { get; set; } = "[]";
    public string RelatedSearchesJson { get; set; } = "[]";
    public string? FeaturedSnippet { get; set; }
    public string SerpFeaturesJson { get; set; } = "{}";
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public ICollection<SeoCompetitorPage> CompetitorPages { get; set; } = [];
}

public sealed class SeoCompetitorPage
{
    public Guid Id { get; set; }
    public Guid SerpResultId { get; set; }
    public required string Url { get; set; }
    public string? Domain { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string ContentText { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public string HeadingsJson { get; set; } = "[]";
    public string TermsJson { get; set; } = "{}";
    public int InternalLinkCount { get; set; }
    public int ExternalLinkCount { get; set; }
    public int ImageCount { get; set; }
    public int ImagesMissingAlt { get; set; }
    public bool HasStructuredData { get; set; }
    public string StructuredDataTypesJson { get; set; } = "[]";
    public int HttpStatus { get; set; } = 200;
    public DateTimeOffset CrawledAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public SeoSerpResult? SerpResult { get; set; }
}

public sealed class SeoPageAudit
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public required string Url { get; set; }
    public int Score { get; set; }
    public string IssuesJson { get; set; } = "[]";
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset AuditedAt { get; set; }
}

public sealed class SeoSiteAudit
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Status { get; set; } = "pending";
    public int PagesCrawled { get; set; }
    public decimal? OverallScore { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<SeoSiteAuditPage> Pages { get; set; } = [];
}

public sealed class SeoSiteAuditPage
{
    public Guid Id { get; set; }
    public Guid SiteAuditId { get; set; }
    public required string Url { get; set; }
    public int Score { get; set; }
    public string IssuesJson { get; set; } = "[]";
    public DateTimeOffset CrawledAt { get; set; }

    public SeoSiteAudit? SiteAudit { get; set; }
}

public sealed class SeoRankTracking
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string Keyword { get; set; }
    public required string PageUrl { get; set; }
    public decimal Position { get; set; }
    public int Impressions { get; set; }
    public int Clicks { get; set; }
    public decimal Ctr { get; set; }
    public DateOnly Date { get; set; }
}

public sealed class SeoGscConnection
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public required string SiteUrl { get; set; }
    public byte[] EncryptedRefreshToken { get; set; } = [];
    public byte[] EncryptionIv { get; set; } = [];
    public byte[] EncryptionTag { get; set; } = [];
    public DateTimeOffset ConnectedAt { get; set; }
}

public sealed class SeoSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Tier { get; set; } = "none";
    public string? PaypalSubscriptionId { get; set; }
    public string Status { get; set; } = "inactive";
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SeoReport
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public required string ReportType { get; set; }
    public string? StoragePath { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SeoAlert
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ProjectId { get; set; }
    public required string AlertType { get; set; }
    public required string Message { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SeoUsageCounter
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public required string Feature { get; set; }
    public int Count { get; set; }
}

public sealed class SeoBackgroundJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ProjectId { get; set; }
    public required string JobType { get; set; }
    public string Status { get; set; } = "pending";
    public string PayloadJson { get; set; } = "{}";
    public Guid? ResultId { get; set; }
    public int ProgressPercent { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
