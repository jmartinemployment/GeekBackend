namespace GeekRepository.Data.Entities.ContentWriterV3;

public class ContentCampaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Keyword { get; set; } = string.Empty;
    public string Status { get; set; } = "draft"; // draft, research, strategy, drafting, published, archived
    public Guid ProfileVersionId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public uint RowVersion { get; set; }
}

public class ContentAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public string Type { get; set; } = "pillar"; // pillar, companion
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "draft"; // draft, readyForApproval, approved, published
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ContentAssetVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetId { get; set; }
    public int VersionNumber { get; set; }
    /// <summary>
    /// Stored as JSON matching ContentDocument shape from lib/types.ts
    /// (lede: string, sections: { heading, paragraphs, children })
    /// </summary>
    public string BodyDocumentJson { get; set; } = "{}";
    public uint RowVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
