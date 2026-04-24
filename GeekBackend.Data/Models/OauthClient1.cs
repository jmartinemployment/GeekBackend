using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class OauthClient1
{
    public Guid Id { get; set; }

    public string? ClientSecretHash { get; set; }

    public string RedirectUris { get; set; } = null!;

    public string GrantTypes { get; set; } = null!;

    public string? ClientName { get; set; }

    public string? ClientUri { get; set; }

    public string? LogoUri { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string TokenEndpointAuthMethod { get; set; } = null!;

    public virtual ICollection<OauthAuthorization> OauthAuthorizations { get; set; } = new List<OauthAuthorization>();

    public virtual ICollection<OauthConsent> OauthConsents { get; set; } = new List<OauthConsent>();

    public virtual ICollection<Session> Sessions { get; set; } = new List<Session>();
}
