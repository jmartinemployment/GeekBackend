namespace GeekApplication.Dtos;

public record CreateUserRequest(
    string Subject,
    string Username,
    string? Email,
    string Password
);
