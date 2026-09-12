namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// Owner-scoped Google Drive connection for Content Creator Knowledge ingest.
/// Read-only file fetch; shares encryption key with GSC tokens.
/// </summary>
public sealed class GccV2DriveConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    /// <summary>Google account email or operator label (e.g. drive@example.test for stubs).</summary>
    public string AccountLabel { get; set; } = string.Empty;
    public string Status { get; set; } = "connected";
    public byte[] EncryptedRefreshToken { get; set; } = [];
    public byte[] EncryptionIv { get; set; } = [];
    public byte[] EncryptionTag { get; set; } = [];
    public DateTimeOffset ConnectedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
