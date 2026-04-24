using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Permission
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();
}
