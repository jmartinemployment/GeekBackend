using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CustomOauthProvider
{
    public Guid Id { get; set; }

    public string ProviderType { get; set; } = null!;

    public string Identifier { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string ClientId { get; set; } = null!;

    public string ClientSecret { get; set; } = null!;

    public List<string> AcceptableClientIds { get; set; } = null!;

    public List<string> Scopes { get; set; } = null!;

    public bool PkceEnabled { get; set; }

    public string AttributeMapping { get; set; } = null!;

    public string AuthorizationParams { get; set; } = null!;

    public bool Enabled { get; set; }

    public bool EmailOptional { get; set; }

    public string? Issuer { get; set; }

    public string? DiscoveryUrl { get; set; }

    public bool SkipNonceCheck { get; set; }

    public string? CachedDiscovery { get; set; }

    public DateTime? DiscoveryCachedAt { get; set; }

    public string? AuthorizationUrl { get; set; }

    public string? TokenUrl { get; set; }

    public string? UserinfoUrl { get; set; }

    public string? JwksUri { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
