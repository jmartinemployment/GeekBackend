using GeekApplication.Dtos;

namespace GeekApplication.Interfaces;

public interface IUserAuthRepository
{
    Task<Result<User>> CreateAsync(CreateUserRequest request);
    Task<Result<User>> FindByIdAsync(Guid userId);
    Task<Result<User>> FindByUsernameAsync(string username);
    Task<Result<User>> FindByEmailAsync(string email);
    Task<Result<User>> FindBySubjectAsync(string subject);
    Task<Result<User>> FindBySlackIdAsync(string slackUserId);
    Task<Result<User>> UpdateAsync(User user);
    Task<Result<bool>> DeleteAsync(Guid userId);
    Task<Result<UserSecrets>> GetSecretsAsync(Guid userId);
    Task<Result<bool>> UpdateSecretsAsync(Guid userId, UserSecrets secrets);
    Task<Result<bool>> VerifyPasswordAsync(Guid userId, string password);
    Task<Result<bool>> UpdatePasswordAsync(Guid userId, string newPassword);
    Task<Result<List<string>>> GetPasswordHistoryAsync(Guid userId);
}
