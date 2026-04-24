using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class WebauthnCredential
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte[] CredentialId { get; set; } = null!;

    public byte[] PublicKey { get; set; } = null!;

    public string AttestationType { get; set; } = null!;

    public Guid? Aaguid { get; set; }

    public long SignCount { get; set; }

    public string Transports { get; set; } = null!;

    public bool BackupEligible { get; set; }

    public bool BackedUp { get; set; }

    public string FriendlyName { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
