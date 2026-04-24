using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

/// <summary>
/// auth: stores metadata about factors
/// </summary>
public partial class MfaFactor
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string? FriendlyName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? Secret { get; set; }

    public string? Phone { get; set; }

    public DateTime? LastChallengedAt { get; set; }

    public string? WebAuthnCredential { get; set; }

    public Guid? WebAuthnAaguid { get; set; }

    /// <summary>
    /// Stores the latest WebAuthn challenge data including attestation/assertion for customer verification
    /// </summary>
    public string? LastWebauthnChallengeData { get; set; }

    public virtual ICollection<MfaChallenge> MfaChallenges { get; set; } = new List<MfaChallenge>();

    public virtual User User { get; set; } = null!;
}
