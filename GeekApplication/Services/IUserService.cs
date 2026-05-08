using GeekApplication.Dtos;

namespace GeekApplication.Services;

public interface IUserService
{
    Task<Result<User>> RegisterAsync(CreateUserRequest request);
    Task<Result<User>> FindByIdAsync(Guid userId);
    Task<Result<User>> FindByUsernameAsync(string username);
    Task<Result<User>> FindByEmailAsync(string email);
    Task<Result<User>> FindBySubjectAsync(string subject);
    Task<Result<User>> UpdateAsync(User user);
    Task<Result<bool>> DeleteAsync(Guid userId);
    Task<Result<bool>> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
    Task<Result<bool>> EnableTwoFactorAsync(Guid userId);
    Task<Result<bool>> DisableTwoFactorAsync(Guid userId);
}
