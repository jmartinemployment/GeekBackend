namespace GeekApplication.Models.Seo;

public sealed record BrandVoiceDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string SampleText { get; init; }
    public string? StyleInstructions { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record CreateBrandVoiceRequest
{
    public required string Name { get; init; }
    public required string SampleText { get; init; }
    public string? StyleInstructions { get; init; }
}

public sealed record UpdateBrandVoiceRequest
{
    public string? Name { get; init; }
    public string? SampleText { get; init; }
    public string? StyleInstructions { get; init; }
}
