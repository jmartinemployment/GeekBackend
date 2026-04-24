using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class OauthClient
{
    public string Id { get; set; } = null!;

    public string ClientId { get; set; } = null!;

    public string? ClientSecret { get; set; }

    public string RedirectUris { get; set; } = null!;

    public string GrantTypes { get; set; } = null!;

    public string ResponseTypes { get; set; } = null!;

    public string Scope { get; set; } = null!;

    public string TokenEndpointAuthMethod { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
