using System;
using System.Collections.Generic;
using System.Net;

namespace GeekBackend.Data.Models;

/// <summary>
/// Auth: Stores session data associated to a user.
/// </summary>
public partial class Session
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? FactorId { get; set; }

    /// <summary>
    /// Auth: Not after is a nullable column that contains a timestamp after which the session should be regarded as expired.
    /// </summary>
    public DateTime? NotAfter { get; set; }

    public DateTime? RefreshedAt { get; set; }

    public string? UserAgent { get; set; }

    public IPAddress? Ip { get; set; }

    public string? Tag { get; set; }

    public Guid? OauthClientId { get; set; }

    /// <summary>
    /// Holds a HMAC-SHA256 key used to sign refresh tokens for this session.
    /// </summary>
    public string? RefreshTokenHmacKey { get; set; }

    /// <summary>
    /// Holds the ID (counter) of the last issued refresh token.
    /// </summary>
    public long? RefreshTokenCounter { get; set; }

    public string? Scopes { get; set; }

    public virtual ICollection<MfaAmrClaim> MfaAmrClaims { get; set; } = new List<MfaAmrClaim>();

    public virtual OauthClient1? OauthClient { get; set; }

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public virtual User User { get; set; } = null!;
}
