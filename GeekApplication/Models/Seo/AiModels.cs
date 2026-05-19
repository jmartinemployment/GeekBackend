namespace GeekApplication.Models.Seo;

public sealed record AIRequest
{
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    public string Model { get; init; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; init; } = 4096;
    public double Temperature { get; init; } = 0.7;
}

public sealed record AIResponse
{
    public required string Content { get; init; }
    public required string Model { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required string StopReason { get; init; }
}

public sealed record FullArticleRequest
{
    public required Guid ProjectId { get; init; }
    public required string Keyword { get; init; }
    public string Location { get; init; } = "United States";
    public string Title { get; init; } = string.Empty;
}

public sealed record FullArticleJobPayload
{
    public required Guid ProjectId { get; init; }
    public required string Keyword { get; init; }
    public required string Location { get; init; }
    public required string Title { get; init; }
}
