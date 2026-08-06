namespace GeekRepository.Data.Entities.ContentCreator;

public class GccCreate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string StartingContentType { get; set; } = "long-form";
    public string Topic { get; set; } = string.Empty;
    public string? Notes { get; set; }
    /// <summary>
    /// Per-create department slug for canonical URL / JSON-LD / meta (replaces CWV2 Project.Department).
    /// Describes what's being written, not which domain was analyzed.
    /// </summary>
    public string Department { get; set; } = "marketing";
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
    /// <summary>processing, ready, or failed.</summary>
    public string Status { get; set; } = "ready";
    public Guid? SeoProjectId { get; set; }
    public Guid? SeoProfileId { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>Site pages and topical neighbors; gaps remain in GapsJson during the compatibility period.</summary>
    public string SiteModelJson { get; set; } = """{"sitePages":[],"topicalNeighbors":[]}""";
    public string GapsJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class GccSiteFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteAnalysisId { get; set; }
    public string FindingType { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string? AffectedUrl { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
