using GeekApplication.Dtos;
using ClientCreateRequest = GeekApplication.Dtos.CreateUserRequest;

namespace GeekAPI.Dtos;

// ── User Auth ────────────────────────────────────────────────────────────────

public record UpdateUserRequest(
    string? Username,
    string? Email,
    bool? TwoFactorEnabled
);

// ── OAuth Token ── (imported from GeekApplication.Dtos)

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

// ── OAuth Client ── (imported from GeekApplication.Dtos)

// ── Pending Verification ────────────────────────────────────────────────────

public record UpsertPendingVerificationRequest(
    Guid UserId,
    string VerificationType,
    string VerificationCode,
    DateTime ExpiresAt
);

// ── OIDC Storage ─────────────────────────────────────────────────────────────

public record OidcStorageUpsertRequest(
    string Kind,
    string Payload,
    DateTime ExpiresAt,
    string? UserCode = null,
    string? TokenHash = null,
    string? Uid = null,
    string? GrantId = null
);
