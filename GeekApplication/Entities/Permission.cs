namespace GeekApplication.Entities;

public class Permission
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    // Navigation property
    public virtual ICollection<RolePermission> RolePermissions { get; set; } = [];
}
