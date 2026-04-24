using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class VerificationToken
{
    public string Identifier { get; set; } = null!;

    public string Token { get; set; } = null!;

    public DateTime Expires { get; set; }
}
