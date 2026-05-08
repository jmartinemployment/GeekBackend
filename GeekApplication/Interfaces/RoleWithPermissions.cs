namespace GeekApplication.Interfaces;

public record RoleWithPermissions(
    Guid Id,
    string Name,
    string? Description,
    List<PermissionDto> Permissions
);

public record PermissionDto(Guid Id, string Name);
