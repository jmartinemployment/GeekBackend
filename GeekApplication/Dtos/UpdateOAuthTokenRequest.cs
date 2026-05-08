namespace GeekApplication.Dtos;

public record UpdateOAuthTokenRequest(
    string? TokenType,
    string? AccessToken,
    int? ExpiresIn,
    string? RefreshToken,
    string? Scope
);
