using System.Text.Json.Serialization;

namespace GeekRepository.Data.Entities.ContentCreatorV2;

public class GccV2SkillPackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceRepository { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string LifecycleState { get; set; } = "quarantined";
    public bool IsFirstParty { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeprecatedAtUtc { get; set; }
    public List<GccV2SkillVersion> Versions { get; set; } = [];
}

public class GccV2SkillVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public string SemanticVersion { get; set; } = string.Empty;
    public string ImmutableGitRef { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public string ManifestDigest { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string Compatibility { get; set; } = string.Empty;
    public string State { get; set; } = "quarantined";
    public DateTimeOffset ImportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string? Reviewer { get; set; }
    public string? ReviewNotes { get; set; }
    public Guid? SupersedesVersionId { get; set; }
    [JsonIgnore] public GccV2SkillPackage Package { get; set; } = null!;
    public List<GccV2SkillFile> Files { get; set; } = [];
    public List<GccV2SkillApplicability> Applicability { get; set; } = [];
    public List<GccV2SkillReviewFinding> Findings { get; set; } = [];
}

public class GccV2SkillFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string MediaType { get; set; } = "text/plain";
    public long ByteCount { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    [JsonIgnore] public GccV2SkillVersion Version { get; set; } = null!;
}

public class GccV2SkillApplicability
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int Order { get; set; }
    public string ConflictsJson { get; set; } = "[]";
    public string RequiredToolsJson { get; set; } = "[]";
    public string ActivationMode { get; set; } = "automatic";
    [JsonIgnore] public GccV2SkillVersion Version { get; set; } = null!;
}

public class GccV2SkillReviewFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Scanner { get; set; } = string.Empty;
    public string Rule { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public int? Line { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Disposition { get; set; } = "unreviewed";
    public string? ReviewerRationale { get; set; }
    public bool PermanentRejection { get; set; }
    [JsonIgnore] public GccV2SkillVersion Version { get; set; } = null!;
}

public class GccV2SkillAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public Guid? VersionId { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? SourceIp { get; set; }
    public string? RequestId { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
