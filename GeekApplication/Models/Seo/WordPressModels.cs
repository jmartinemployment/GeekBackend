namespace GeekApplication.Models.Seo;

public sealed record WordPressCredentials
{
    public required string SiteUrl { get; init; }
    public required string Username { get; init; }
    public required string ApplicationPassword { get; init; }
}

public sealed record WordPressConnectRequest
{
    public required string SiteUrl { get; init; }
    public required string Username { get; init; }
    public required string ApplicationPassword { get; init; }
    public string DefaultPostStatus { get; init; } = "draft";
}

public sealed record WordPressPublishOptions
{
    public string PostStatus { get; init; } = "draft";
    public string? Slug { get; init; }
}

public sealed record WordPressPublishResult
{
    public required int PostId { get; init; }
    public required string Url { get; init; }
    public required string Status { get; init; }
}

public sealed record WordPressConnectionTestResult
{
    public required bool Success { get; init; }
    public string? SiteTitle { get; init; }
}

public sealed record WordPressPostPayload
{
    public required string Title { get; init; }
    public required string ContentHtml { get; init; }
    public required string Status { get; init; }
    public string? Slug { get; init; }
}

public sealed record WordPressPublishProviderResult
{
    public required int PostId { get; init; }
    public required string Link { get; init; }
    public required string Status { get; init; }
}

public sealed record WordPressConnectionStatus
{
    public required bool Connected { get; init; }
    public string? SiteUrl { get; init; }
    public string? Username { get; init; }
    public string DefaultPostStatus { get; init; } = "draft";
}
