namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// One sync-publish attempt of a completed v2 job draft into the existing Geek blog CMS
/// (<c>geek_blog</c> schema, via <c>IBlogRepository</c>). A create/job can have several of these
/// over time (re-publish, publish-live after a draft, retried failures) — this is an append-only
/// audit trail, not a 1:1 mirror of the CMS post.
/// </summary>
public class GccV2PublishRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreateId { get; set; }
    public Guid JobId { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>Publish destination — only <c>blog</c> (the Geek blog CMS) exists today.</summary>
    public string Channel { get; set; } = "blog";

    /// <summary>draft | published | failed</summary>
    public string Status { get; set; } = "draft";

    /// <summary><c>geek_blog.posts.id</c> once the CMS call succeeds.</summary>
    public int? ExternalPostId { get; set; }

    public string Slug { get; set; } = string.Empty;
    public string? PublicUrl { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? MetaDescription { get; set; }
    public string? Error { get; set; }

    /// <summary>The <see cref="GeekAPI"/> job's <c>ContentDocument</c> JSON at publish time, kept
    /// for audit/debugging independent of the job row's own lifecycle.</summary>
    public string? BodyDocumentJson { get; set; }

    public bool IsPublished { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
