namespace GeekApplication.Entities;

public class OidcStorage
{
    public string Id { get; set; } = null!;
    public required string Kind { get; set; }
    public required string Payload { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? UserCode { get; set; }
    public string? TokenHash { get; set; }
    public string? Uid { get; set; }
    public string? GrantId { get; set; }
    public DateTime CreatedAt { get; set; }
}
