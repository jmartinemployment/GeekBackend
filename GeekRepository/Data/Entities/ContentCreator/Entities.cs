namespace GeekRepository.Data.Entities.ContentCreator;

public class GccCreate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string StartingContentType { get; set; } = "long-form";
    public string Topic { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? SiteAnalysisId { get; set; }
    public string? SiteSectionJson { get; set; }
    /// <summary>Content Brief JSON (intent, audience, angle, CTA, ToV, length, SERP index fields).</summary>
    public string? BriefJson { get; set; }
    /// <summary>Deep research JSON (SERP index + ≤3 quoteable destination pages).</summary>
    public string? ResearchJson { get; set; }
    public string Status { get; set; } = "draft"; // draft, generating, drafted, revising, approved, repurposed, archived
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class GccArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreateId { get; set; }
    public Guid? ParentArtifactId { get; set; }
    public string Type { get; set; } = "long-form"; // long-form, social, ads, image-prompt, tool:<name>
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "draft"; // draft, readyForApproval, approved, published
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class GccArtifactVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ArtifactId { get; set; }
    public int VersionNumber { get; set; }
    /// <summary>
    /// Structured content JSON — ContentDocument shape for long-form, pack JSON for
    /// social/ads/video, or a plain prompt string wrapped in JSON for image prompts.
    /// </summary>
    public string BodyJson { get; set; } = "{}";
    /// <summary>Optional side-channel metadata (provider used, tool name, source artifact, etc).</summary>
    public string? MetadataJson { get; set; }
    public uint RowVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class GccApprovalEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ArtifactVersionId { get; set; }
    public Guid UserId { get; set; }
    public string Action { get; set; } = "approved";
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class GccSiteAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Domain { get; set; } = string.Empty;
    public string? SeedTopic { get; set; }
    public string GapsJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
