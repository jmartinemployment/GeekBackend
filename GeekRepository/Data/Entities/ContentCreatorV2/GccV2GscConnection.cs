namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// Owner-scoped Google Search Console connection for Content Creator.
/// Copied from Geek-SEO persistence shape; not keyed to SEO projects.
/// </summary>
public sealed class GccV2GscConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string SiteUrl { get; set; } = string.Empty;
    public string Status { get; set; } = "connected";
    public byte[] EncryptedRefreshToken { get; set; } = [];
    public byte[] EncryptionIv { get; set; } = [];
    public byte[] EncryptionTag { get; set; } = [];
    public DateTimeOffset ConnectedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
