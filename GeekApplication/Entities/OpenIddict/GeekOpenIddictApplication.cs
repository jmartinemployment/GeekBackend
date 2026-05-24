namespace GeekApplication.Entities.OpenIddict;

public sealed class GeekOpenIddictApplication
{
    public string Id { get; set; } = string.Empty;
    public string? ApplicationType { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? ClientType { get; set; }
    public string? ConsentType { get; set; }
    public string? DisplayName { get; set; }
    public string? DisplayNames { get; set; }
    public string? JsonWebKeySet { get; set; }
    public string? Permissions { get; set; }
    public string? PostLogoutRedirectUris { get; set; }
    public string? Properties { get; set; }
    public string? RedirectUris { get; set; }
    public string? Requirements { get; set; }
    public string? Settings { get; set; }
}
