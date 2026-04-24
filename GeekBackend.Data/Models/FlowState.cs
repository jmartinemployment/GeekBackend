using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

/// <summary>
/// Stores metadata for all OAuth/SSO login flows
/// </summary>
public partial class FlowState
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public string? AuthCode { get; set; }

    public string? CodeChallenge { get; set; }

    public string ProviderType { get; set; } = null!;

    public string? ProviderAccessToken { get; set; }

    public string? ProviderRefreshToken { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string AuthenticationMethod { get; set; } = null!;

    public DateTime? AuthCodeIssuedAt { get; set; }

    public string? InviteToken { get; set; }

    public string? Referrer { get; set; }

    public Guid? OauthClientStateId { get; set; }

    public Guid? LinkingTargetId { get; set; }

    public bool EmailOptional { get; set; }

    public virtual ICollection<SamlRelayState> SamlRelayStates { get; set; } = new List<SamlRelayState>();
}
