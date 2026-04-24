using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class OidcStorage
{
    public string Id { get; set; } = null!;

    public string Kind { get; set; } = null!;

    public string Payload { get; set; } = null!;

    public DateTime? ExpiresAt { get; set; }

    public string? UserCode { get; set; }

    public string? TokenHash { get; set; }

    public string? Uid { get; set; }

    public string? GrantId { get; set; }

    public DateTime CreatedAt { get; set; }
}
