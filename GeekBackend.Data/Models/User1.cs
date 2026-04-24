using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class User1
{
    public string Id { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? Name { get; set; }

    public string? Password { get; set; }

    public string Plan { get; set; } = null!;

    public string? SlackUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Device> Devices { get; set; } = new List<Device>();

    public virtual ICollection<OauthToken> OauthTokens { get; set; } = new List<OauthToken>();

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
