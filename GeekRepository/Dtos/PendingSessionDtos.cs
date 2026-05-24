namespace GeekRepository.Dtos;

public sealed record StorePendingSessionRequest(
    Guid UserId,
    string SessionCode,
    string DeviceFingerprint,
    DateTime ExpiresAt);
