using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Article
{
    public string Id { get; set; } = null!;

    public string SiteId { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string? MetaTitle { get; set; }

    public string? MetaDescription { get; set; }

    public string TargetKeyword { get; set; } = null!;

    public int WordCount { get; set; }

    public int SeoScore { get; set; }

    public int ReadabilityScore { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? PublishedAt { get; set; }

    public string? CmsPostId { get; set; }

    public string? InternalLinks { get; set; }

    public string? FeaturedImageUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Site Site { get; set; } = null!;
}
