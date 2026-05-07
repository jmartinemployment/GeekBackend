using GeekBackend.Data.Results;

namespace GeekRepository.Repositories;

public interface IRbacRepository
{
    Task<Result<List<RoleWithPermissions>>> GetUserRolesWithPermissionsAsync(string userId);
}

public class RoleWithPermissions
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public List<PermissionDto> Permissions { get; set; } = [];
}

public class PermissionDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}
