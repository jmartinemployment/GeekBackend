namespace GeekApplication.Entities;

public class UserSecrets
{
    public Guid UserId { get; set; }
    public required string PasswordHash { get; set; } // BCrypt hash (work factor 12)
    public required string TwoFactorSecret { get; set; } // AES-256 encrypted TOTP secret
    public List<string> RecoveryCodes { get; set; } = []; // BCrypt hashed, single-use, XXXX-XXXX format
    public List<string> PasswordHistory { get; set; } = []; // Last 10 BCrypt hashes
    public DateTime PasswordChangedAt { get; set; }
    public int LoginFailureCount { get; set; }
    public DateTime? LastLoginFailureAt { get; set; }

    // Navigation property
    public virtual User User { get; set; } = null!;
}
