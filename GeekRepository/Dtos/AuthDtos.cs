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
