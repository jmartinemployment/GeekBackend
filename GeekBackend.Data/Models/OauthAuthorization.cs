using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class OauthAuthorization
{
    public Guid Id { get; set; }

    public string AuthorizationId { get; set; } = null!;

    public Guid ClientId { get; set; }

    public Guid? UserId { get; set; }

    public string RedirectUri { get; set; } = null!;

    public string Scope { get; set; } = null!;

    public string? State { get; set; }

    public string? Resource { get; set; }

    public string? CodeChallenge { get; set; }

    public string? AuthorizationCode { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public string? Nonce { get; set; }

    public virtual OauthClient1 Client { get; set; } = null!;

    public virtual User? User { get; set; }
}
