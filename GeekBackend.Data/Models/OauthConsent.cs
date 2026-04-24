using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class OauthConsent
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ClientId { get; set; }

    public string Scopes { get; set; } = null!;

    public DateTime GrantedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public virtual OauthClient1 Client { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
