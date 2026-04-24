using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class WebauthnChallenge
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public string ChallengeType { get; set; } = null!;

    public string SessionData { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public virtual User? User { get; set; }
}
