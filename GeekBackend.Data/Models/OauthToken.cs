using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class OauthToken
{
    public string Id { get; set; } = null!;

    public string AccessToken { get; set; } = null!;

    public DateTime AccessTokenExpiresAt { get; set; }

    public string RefreshToken { get; set; } = null!;

    public DateTime? RefreshTokenExpiresAt { get; set; }

    public string? BiosId { get; set; }

    public string UserId { get; set; } = null!;

    public string ClientId { get; set; } = null!;

    public string? Scope { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User1 User { get; set; } = null!;
}
