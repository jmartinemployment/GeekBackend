namespace GeekApplication.Entities.OpenIddict;

public sealed class GeekOpenIddictToken
{
    public string Id { get; set; } = string.Empty;
    public string? ApplicationId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? ConcurrencyToken { get; set; }
    public DateTimeOffset? CreationDate { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? Payload { get; set; }
    public string? Properties { get; set; }
    public DateTimeOffset? RedemptionDate { get; set; }
    public string? ReferenceId { get; set; }
    public string? Status { get; set; }
    public string? Subject { get; set; }
    public string? Type { get; set; }
}
