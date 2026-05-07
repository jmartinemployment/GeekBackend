using GeekBackend.Data.Models;
using GeekBackend.Data.Results;

namespace GeekRepository.Repositories;

public interface IUserAuthRepository
{
    Task<Result<User>> FindByIdAsync(string id);
    Task<Result<User>> FindByEmailAsync(string email);
    Task<Result<User>> FindBySlackIdAsync(string slackUserId);
    Task<Result<User>> CreateAsync(CreateUserRequest request);
    Task<Result<User>> UpdateAsync(string id, UpdateUserRequest request);
    Task<Result<Unit>> DeleteAsync(string id);
}

public class CreateUserRequest
{
    public string Email { get; set; } = null!;
    public string? Name { get; set; }
    public string? Password { get; set; }
    public string Plan { get; set; } = "free";
}

public class UpdateUserRequest
{
    public string? Name { get; set; }
    public string? Password { get; set; }
    public string? Plan { get; set; }
    public string? SlackUserId { get; set; }
}
