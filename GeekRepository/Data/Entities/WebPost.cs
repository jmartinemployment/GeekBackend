namespace GeekRepository.Data.Entities;

public class WebPost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public ContentStructure ContentStructure { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

public class ContentStructure
{
    public List<ContentSection> Sections { get; set; } = new();
    public string? MainBody { get; set; }
}

public class ContentSection
{
    public string? HeadingText { get; set; }
    public string BodyContent { get; set; } = string.Empty;
    public string? MediaUrl { get; set; }
    public string? MediaAlt { get; set; }
}
