extern alias GeekRepo;

using GeekApplication.Auth;
using GeekApplication.Interfaces;
using GeekRepo::GeekRepository.Infrastructure;
using GeekRepo::GeekRepository.Repositories;

namespace GeekBackend.IntegrationTests;

public sealed class DeviceTests
{
    [Fact]
    public async Task DeviceChallenge_ValidSignature_Succeeds()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured)
            return;

        await SqlMigrationTestHelper.ApplyAsync();
        var factory = new NpgsqlConnectionFactory(IntegrationTestConfig.DatabaseUrl!);
        var repo = new DeviceOauthRepository(factory);
        var userId = Guid.NewGuid();

        var register = await repo.RegisterAsync(userId, new RegisterDeviceOauthRequest(
            DeviceType: "desktop",
            DeviceName: "Integration Device",
            BiosId: "bios-test",
            DeviceFingerprint: DeviceCrypto.ComputeFingerprint("machine", "bios-test", "darwin"),
            Platform: "darwin"));
        Assert.True(register.IsSuccess, register.Error);

        var device = register.Value!;
        var challenge = await repo.IssueChallengeAsync(device.Id);
        Assert.True(challenge.IsSuccess, challenge.Error);

        using var ecdsa = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var publicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem();
        var signature = Convert.ToBase64String(
            ecdsa.SignData(
                System.Text.Encoding.UTF8.GetBytes(challenge.Value!),
                System.Security.Cryptography.HashAlgorithmName.SHA256));

        var verified = await repo.VerifyChallengeAsync(device.Id, challenge.Value!, signature, publicKeyPem);
        Assert.True(verified.IsSuccess && verified.Value, verified.Error);

        await repo.DeleteAsync(device.Id);
    }
}
