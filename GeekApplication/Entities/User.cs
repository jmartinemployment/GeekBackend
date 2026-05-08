namespace GeekApplication.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string Subject { get; set; } // OIDC 'sub' claim
    public required string Username { get; set; }
    public string? Email { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public virtual ICollection<DeviceOauth> Devices { get; set; } = [];
    public virtual ICollection<UserClaim> UserClaims { get; set; } = [];
    public virtual ICollection<UserRole> UserRoles { get; set; } = [];
}
