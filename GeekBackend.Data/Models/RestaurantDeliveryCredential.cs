using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RestaurantDeliveryCredential
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string? DoordashApiKeyEncrypted { get; set; }

    public string? DoordashSigningSecretEncrypted { get; set; }

    public string? DoordashMode { get; set; }

    public string? UberClientIdEncrypted { get; set; }

    public string? UberClientSecretEncrypted { get; set; }

    public string? UberCustomerIdEncrypted { get; set; }

    public string? UberWebhookSigningKeyEncrypted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
