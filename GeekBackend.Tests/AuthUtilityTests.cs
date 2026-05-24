using GeekApplication.Auth;

namespace GeekBackend.Tests;

public sealed class AuthUtilityTests
{
    [Fact]
    public void PasswordHelper_HashAndVerify_RoundTrip()
    {
        var hash = PasswordHelper.Hash("correct-horse-battery-staple");
        Assert.True(PasswordHelper.Verify("correct-horse-battery-staple", hash));
        Assert.False(PasswordHelper.Verify("wrong-password", hash));
    }

    [Fact]
    public void TotpVerifier_GeneratesAndValidatesCode()
    {
        var secret = TotpVerifier.GenerateSecret();
        var totp = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(secret));
        var code = totp.ComputeTotp();
        Assert.True(TotpVerifier.Verify(secret, code));
    }

    [Fact]
    public void DeviceCrypto_Fingerprint_IsDeterministic()
    {
        var a = DeviceCrypto.ComputeFingerprint("machine", "bios", "darwin");
        var b = DeviceCrypto.ComputeFingerprint("machine", "bios", "darwin");
        Assert.Equal(a, b);
    }
}
