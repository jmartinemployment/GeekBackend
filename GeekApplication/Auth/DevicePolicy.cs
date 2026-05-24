namespace GeekApplication.Auth;

public static class DevicePolicy
{
    public static bool CanRegisterDevice(int currentDeviceCount, bool allowMultipleDevices, int maxDevices) =>
        allowMultipleDevices || currentDeviceCount < 1;

    public static bool IsWithinDeviceLimit(int currentDeviceCount, int maxDevices) =>
        maxDevices <= 0 || currentDeviceCount < maxDevices;

    public static bool RequiresTrustedDevice(bool policyRequiresTrust, bool deviceIsTrusted) =>
        !policyRequiresTrust || deviceIsTrusted;
}
