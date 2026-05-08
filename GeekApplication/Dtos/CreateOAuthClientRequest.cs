namespace GeekApplication.Dtos;

public record CreateOAuthClientRequest(
    string ClientId,
    string ClientSecret,
    List<string> RedirectUris,
    List<string> Permissions,
    string Type
);
