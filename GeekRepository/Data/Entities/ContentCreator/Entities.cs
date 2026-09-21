namespace GeekRepository.Data.Entities.ContentCreator;

public class GccCreate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    /// <summary>
    /// The project this create belongs to. A project is the engagement with one site, and it owns
    /// the partner and competitor URLs generation must be grounded on — without this link a create
    /// cannot reach them, which is why drafts never cited partners or tools.
    /// </summary>
    /// <remarks>
    /// Nullable because creates predate the link. A create with no project cannot resolve
    /// partner/competitor evidence and must be refused for content types that require it, never
    /// silently generated ungrounded.
    /// </remarks>
    public Guid? ProjectId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string StartingContentType { get; set; } = "long-form";
    public string Topic { get; set; } = string.Empty;
    public string? Notes { get; set; }
    /// <summary>
    /// Per-create department slug for canonical URL / JSON-LD / meta (replaces CWV2 Project.Department).
    /// Describes what's being written, not which domain was analyzed.
    /// </summary>
    public string Department { get; set; } = "marketing";
    /// <summary>
    /// The Geek-Crawler-v2 crawl this create is grounded on. The database column is still
    /// "SiteAnalysisId" — renaming the property costs nothing, renaming the column would be a
    /// migration over live rows for no gain.
    /// </summary>
    public Guid? ProjectSiteRunId { get; set; }
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

/// <summary>
/// A client: who they are, how to reach them, and how they are billed.
/// </summary>
/// <remarks>
/// The one client table. There used to be a second, a blob store behind api/clients, and the two
/// assigned unrelated ids to the same client — which is why gcc_projects.client_id, the first
/// foreign key to actually enforce the relationship, could not be satisfied by anything the UI had
/// in hand.
///
/// Contact and billing are required (see the migration's NOT NULL set) because a client that
/// cannot be invoiced is not a client. Rate is the deliberate exception: it stays nullable, and a
/// client without one simply cannot have billable time logged against it.
///
/// The publish target is flattened onto this row rather than kept as its own entity. It holds the
/// names of environment variables, never the secrets themselves — that property is the whole
/// reason it is safe to store at all, and it survives the flattening.
/// </remarks>
public class GccClient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }

    public string? BillingContactName { get; set; }

    /// <summary>Required even when it equals the contact email: stored, never derived at read time.</summary>
    public string BillingEmail { get; set; } = string.Empty;

    public string? ContactAddressLine1 { get; set; }
    public string? ContactAddressLine2 { get; set; }
    public string? ContactCity { get; set; }
    public string? ContactRegion { get; set; }
    public string? ContactPostalCode { get; set; }
    public string? ContactCountry { get; set; }

    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingRegion { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountry { get; set; }

    /// <summary>Whole days; 0 is due on receipt. An integer so a due date can be computed from it.</summary>
    public int PaymentTermsDays { get; set; }

    /// <summary>Hourly rate. Null on purpose — see the class remarks.</summary>
    public decimal? Rate { get; set; }

    /// <summary>ISO-4217, three uppercase letters. No database default.</summary>
    public string Currency { get; set; } = string.Empty;

    public string? TaxId { get; set; }
    public string? PoReference { get; set; }

    /// <summary>GeekBackend publish configuration, flattened. Env-var names only, never secrets.</summary>
    public string? PublishApiBaseUrl { get; set; }
    public string? PublishOAuthTokenEndpoint { get; set; }
    public string? PublishClientIdEnvVar { get; set; }
    public string? PublishClientSecretEnvVar { get; set; }
    public int? PublishDefaultAuthorId { get; set; }
    public string? PublishCategoryStrategy { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
