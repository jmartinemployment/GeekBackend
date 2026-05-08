namespace GeekApplication.Dtos;

public record UpsertDeviceRequest(
    string DeviceType,
    string DeviceName,
    string BiosId,
    string DeviceFingerprint,
    string Platform
);
