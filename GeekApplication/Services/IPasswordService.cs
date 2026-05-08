namespace GeekApplication.Services;

public interface IPasswordService
{
    Task<Result<bool>> ValidatePasswordComplexityAsync(string password);
    Task<Result<bool>> IsPasswordInHaveIBeenPwnedAsync(string password);
    Task<Result<string>> HashPasswordAsync(string password);
    Task<Result<bool>> VerifyPasswordAsync(string password, string hash);
    Task<Result<bool>> IsPasswordInHistoryAsync(Guid userId, string password);
    Task<Result<bool>> UpdatePasswordHistoryAsync(Guid userId, string newPasswordHash);
    Task<Result<string>> GetPasswordComplexityErrorAsync(string password);
}
