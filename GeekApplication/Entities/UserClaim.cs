namespace GeekApplication.Entities;

public class UserClaim
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string ClaimType { get; set; }
    public required string ClaimValue { get; set; }

    // Navigation property
    public virtual User User { get; set; } = null!;
}
