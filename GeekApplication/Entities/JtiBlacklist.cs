namespace GeekApplication.Entities;

public class JtiBlacklist
{
    public required string Jti { get; set; } // Token ID
    public Guid UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime BlacklistedAt { get; set; }
    public string? Reason { get; set; } // 'logout', 'device_revoked', 'password_changed', etc.
}
