namespace GeekApplication.Entities.OpenIddict;

public sealed class GeekOpenIddictAuthorization
{
    public string Id { get; set; } = string.Empty;
    public string? ApplicationId { get; set; }
    public string? ConcurrencyToken { get; set; }
    public DateTimeOffset? CreationDate { get; set; }
    public string? Properties { get; set; }
    public string? Scopes { get; set; }
    public string? Status { get; set; }
    public string? Subject { get; set; }
    public string? Type { get; set; }
}
