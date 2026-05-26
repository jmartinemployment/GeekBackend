namespace GeekApplication.Models.Seo;

public sealed record PageContent
{
    public required string Url { get; init; }
    public required string FullText { get; init; }
    public string? MetaTitle { get; init; }
    public string? MetaDescription { get; init; }
    public int WordCount { get; init; }
    public int HttpStatusCode { get; init; }
    public IReadOnlyList<PageHeading> Headings { get; init; } = [];
    public bool HasStructuredData { get; init; }
    public IReadOnlyList<string> StructuredDataTypes { get; init; } = [];
    public required DateTimeOffset CrawledAt { get; init; }
}

public sealed record PageHeading
{
    public required int Level { get; init; }
    public required string Text { get; init; }
}
