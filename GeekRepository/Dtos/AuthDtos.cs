namespace GeekRepository.Dtos;

public record UpdateUserRequest(
    string? Username,
    string? Email,
    bool? TwoFactorEnabled
);

public record UpdateDeviceRequest(
    string? DeviceName,
    bool? IsTrusted
);

public record CreateAuditLogRequest(
    Guid UserId,
    string Action,
    string? Details,
    string? IpAddress = null
);

public record CreateCircuitResetRequest(
    Guid UserId,
    int FailureCount
);

public record UpsertPendingVerificationRequest(
    Guid UserId,
    string VerificationType,
    string VerificationCode,
    DateTime ExpiresAt
);

public class OidcStorageUpsertRequest
{
    public string Id { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public Dictionary<string, object> Payload { get; set; } = [];
    public int? ExpiresIn { get; set; }
    public string? UserCode { get; set; }
    public string? TokenHash { get; set; }
    public string? Uid { get; set; }
    public string? GrantId { get; set; }
}
