namespace GeekApplication.Entities.OpenIddict;

public sealed class GeekOpenIddictScope
{
    public string Id { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Descriptions { get; set; }
    public string? DisplayName { get; set; }
    public string? DisplayNames { get; set; }
    public string? Name { get; set; }
    public string? Properties { get; set; }
    public string? Resources { get; set; }
}
