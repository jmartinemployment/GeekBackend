using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Session1
{
    public string Id { get; set; } = null!;

    public string SessionToken { get; set; } = null!;

    public string UserId { get; set; } = null!;

    public DateTime Expires { get; set; }

    public virtual User2 User { get; set; } = null!;
}
