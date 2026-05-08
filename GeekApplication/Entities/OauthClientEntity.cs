namespace GeekApplication.Entities;

public class OauthClientEntity
{
    public string Id { get; set; } = null!;
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public required string RedirectUris { get; set; }
    public required string GrantTypes { get; set; }
    public required string ResponseTypes { get; set; }
    public required string Scope { get; set; }
    public required string TokenEndpointAuthMethod { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
