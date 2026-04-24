using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class UserGoogleCredential
{
    public string UserId { get; set; } = null!;

    public string GoogleRefreshToken { get; set; } = null!;

    public DateTime? UpdatedAt { get; set; }
}
