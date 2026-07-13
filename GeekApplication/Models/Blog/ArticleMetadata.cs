using System.Text.Json.Serialization;

namespace GeekApplication.Models.Blog;

public abstract record ArticleMetadata
{
    [JsonPropertyName("@context")]
    public string Context { get; init; } = "https://schema.org";

    [JsonPropertyName("@type")]
    public abstract string Type { get; }

    [JsonPropertyName("headline")]
    public string Headline { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("datePublished")]
    public DateTimeOffset? DatePublished { get; init; }

    [JsonPropertyName("dateModified")]
    public DateTimeOffset? DateModified { get; init; }

    [JsonPropertyName("author")]
    public SchemaPerson? Author { get; init; }

    [JsonPropertyName("image")]
    public string? Image { get; init; }
}

public sealed record BlogPostingMetadata : ArticleMetadata
{
    public override string Type => "BlogPosting";

    [JsonPropertyName("articleSection")]
    public string? ArticleSection { get; init; }

    [JsonPropertyName("keywords")]
    public string[]? Keywords { get; init; }
}

public sealed record TechnicalArticleMetadata : ArticleMetadata
{
    public override string Type => "TechnicalArticle";

    [JsonPropertyName("proficiencyLevel")]
    public string? ProficiencyLevel { get; init; }

    [JsonPropertyName("dependencies")]
    public string[]? Dependencies { get; init; }

    [JsonPropertyName("programmingLanguage")]
    public string[]? ProgrammingLanguage { get; init; }
}

public sealed record NewsArticleMetadata : ArticleMetadata
{
    public override string Type => "NewsArticle";

    [JsonPropertyName("keywords")]
    public string? Keywords { get; init; }
}

public sealed record SchemaPerson
{
    [JsonPropertyName("@type")]
    public string Type { get; init; } = "Person";

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}
