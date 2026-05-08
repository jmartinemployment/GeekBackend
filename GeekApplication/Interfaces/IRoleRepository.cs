namespace GeekApplication.Interfaces;

public interface IRoleRepository
{
    Task<Result<Role>> CreateAsync(string name, string? description);
    Task<Result<Role>> FindByIdAsync(Guid roleId);
    Task<Result<Role>> FindByNameAsync(string name);
    Task<Result<List<Role>>> GetAllAsync();
    Task<Result<Role>> UpdateAsync(Guid roleId, string name, string? description);
    Task<Result<bool>> DeleteAsync(Guid roleId);
}
