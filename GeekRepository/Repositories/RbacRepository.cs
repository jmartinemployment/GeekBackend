using GeekBackend.Data.Data;
using GeekBackend.Data.Results;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories;

public class RbacRepository : IRbacRepository
{
    private readonly AppDbContext _context;

    public RbacRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<RoleWithPermissions>>> GetUserRolesWithPermissionsAsync(string userId)
    {
        try
        {
            var userRoles = await _context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Include(ur => ur.Role)
                .ThenInclude(r => r.Permissions)
                .ThenInclude(rp => rp.Permission)
                .ToListAsync();

            var rolesList = userRoles
                .Select(ur => new RoleWithPermissions
                {
                    Id = ur.Role.Id,
                    Name = ur.Role.Name,
                    Description = ur.Role.Description,
                    Permissions = ur.Role.Permissions
                        .Select(rp => new PermissionDto
                        {
                            Id = rp.Permission.Id,
                            Name = rp.Permission.Name
                        })
                        .ToList()
                })
                .ToList();

            return Result<List<RoleWithPermissions>>.Success(rolesList);
        }
        catch (Exception ex)
        {
            return Result<List<RoleWithPermissions>>.Failure(ex.Message);
        }
    }
}
