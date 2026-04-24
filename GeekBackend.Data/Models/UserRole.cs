using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class UserRole
{
    public string UserId { get; set; } = null!;

    public string RoleId { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Role Role { get; set; } = null!;

    public virtual User1 User { get; set; } = null!;
}
