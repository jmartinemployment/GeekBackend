using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RestaurantAiCredential
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string EncryptedApiKey { get; set; } = null!;

    public string EncryptionIv { get; set; } = null!;

    public string EncryptionTag { get; set; } = null!;

    public string? KeyLastFour { get; set; }

    public bool IsValid { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
