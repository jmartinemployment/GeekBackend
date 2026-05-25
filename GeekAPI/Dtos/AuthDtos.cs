using GeekApplication.Dtos;
using ClientCreateRequest = GeekApplication.Dtos.CreateUserRequest;

namespace GeekAPI.Dtos;

// ── User Auth ────────────────────────────────────────────────────────────────

public record UpdateUserRequest(
    string? Username,
    string? Email,
    bool? TwoFactorEnabled
);

// ── Device ───────────────────────────────────────────────────────────────────

public record UpdateDeviceRequest(
    string? DeviceName,
    bool? IsTrusted
);

// ── Audit Log ────────────────────────────────────────────────────────────────

public record CreateAuditLogRequest(
    Guid UserId,
    string Action,
    string? Details,
    string? IpAddress = null
);

// ── Circuit Reset ────────────────────────────────────────────────────────────

public record CreateCircuitResetRequest(
    Guid UserId,
    int FailureCount
);

// ── Pending Verification ────────────────────────────────────────────────────

public record UpsertPendingVerificationRequest(
    Guid UserId,
    string VerificationType,
    string VerificationCode,
    DateTime ExpiresAt
);

