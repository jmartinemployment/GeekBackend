namespace GeekApplication.Dtos;

public record CreateOAuthTokenRequest(
    string ClientId,
    string TokenType,
    string AccessToken,
    int ExpiresIn,
    string? RefreshToken = null,
    string? Scope = null
);
