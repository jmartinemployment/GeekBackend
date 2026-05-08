namespace GeekApplication.Entities;

public class DeviceReregistrationRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public required string VerificationCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; } // 15-minute TTL
    public bool IsVerified { get; set; }

    // Navigation properties
    public virtual User User { get; set; } = null!;
    public virtual DeviceOauth Device { get; set; } = null!;
}
